using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Prompts;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Services.Overlays;

public interface IOverlaySuggestionService
{
    Task<OverlaySuggestResponse> SuggestAsync(
        string repositoryId,
        OverlaySuggestRequest? request,
        CancellationToken cancellationToken = default);
}

public sealed partial class OverlaySuggestionService : IOverlaySuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> GenericSegmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "source", "app", "apps", "domain", "domains", "application", "applications",
        "api", "controllers", "controller", "service", "services", "dto", "dtos", "model", "models",
        "entity", "entities", "repository", "repositories", "common", "shared", "infrastructure",
        "core", "base", "tests", "test", "bin", "obj", "wwwroot", "config", "configs", "migrations"
    };

    private readonly IContext _context;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IAdminRepositoryOverlayService _overlayConfigService;
    private readonly AgentFactory _agentFactory;
    private readonly IPromptPlugin _promptPlugin;
    private readonly WikiGeneratorOptions _wikiOptions;
    private readonly ILogger<OverlaySuggestionService> _logger;

    public OverlaySuggestionService(
        IContext context,
        IRepositoryAnalyzer repositoryAnalyzer,
        IAdminRepositoryOverlayService overlayConfigService,
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> wikiOptions,
        ILogger<OverlaySuggestionService> logger)
    {
        _context = context;
        _repositoryAnalyzer = repositoryAnalyzer;
        _overlayConfigService = overlayConfigService;
        _agentFactory = agentFactory;
        _promptPlugin = promptPlugin;
        _wikiOptions = wikiOptions.Value;
        _logger = logger;
    }

    public async Task<OverlaySuggestResponse> SuggestAsync(
        string repositoryId,
        OverlaySuggestRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new OverlaySuggestRequest();

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId && !r.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("仓库不存在");

        var existingConfig = await _overlayConfigService.GetConfigAsync(repositoryId, cancellationToken);
        var warnings = new List<string>();
        var baseBranchName = await ResolveBaseBranchNameAsync(repository, existingConfig, request, warnings, cancellationToken);

        var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            baseBranchName,
            previousCommitId: null,
            cancellationToken);

        try
        {
            var primaryLanguage = await _repositoryAnalyzer.DetectPrimaryLanguageAsync(workspace, cancellationToken)
                                  ?? repository.PrimaryLanguage
                                  ?? string.Empty;
            var analysis = AnalyzeWorkspace(
                workspace.WorkingDirectory,
                primaryLanguage,
                request.MaxVariants,
                request.MaxSamplesPerVariant);

            warnings.AddRange(analysis.Warnings);
            var heuristicConfig = BuildHeuristicConfig(repository, baseBranchName, analysis);

            var aiResult = await TrySuggestWithAiAsync(
                repository,
                baseBranchName,
                primaryLanguage,
                request,
                existingConfig,
                analysis,
                heuristicConfig,
                cancellationToken);

            var config = aiResult?.Config is not null
                ? RepositoryOverlayConfigRules.Sanitize(aiResult.Config)
                : heuristicConfig;

            var usedAi = aiResult?.UsedAi == true;
            if (usedAi)
            {
                try
                {
                    RepositoryOverlayConfigRules.Validate(config);
                }
                catch (Exception ex)
                {
                    usedAi = false;
                    warnings.Add($"AI 返回的配置未通过校验，已回退到启发式配置：{ex.Message}");
                    config = heuristicConfig;
                }
            }

            if (!usedAi)
            {
                warnings.Add("当前建议配置由程序化分析生成，可先预览差异后再微调。");
            }

            return new OverlaySuggestResponse
            {
                RepositoryId = repository.Id,
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BaseBranchName = baseBranchName,
                DetectedPrimaryLanguage = primaryLanguage,
                UsedAi = usedAi,
                Model = _wikiOptions.ContentModel,
                Summary = aiResult?.Summary ?? BuildFallbackSummary(analysis),
                ReasoningSummary = aiResult?.ReasoningSummary ?? BuildFallbackReasoning(analysis),
                Warnings = warnings
                    .Concat(aiResult?.Warnings ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Analysis = analysis.ToResponse(),
                SuggestedConfig = config
            };
        }
        finally
        {
            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
    }

    private async Task<string> ResolveBaseBranchNameAsync(
        Repository repository,
        RepositoryOverlayConfig existingConfig,
        OverlaySuggestRequest request,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.BaseBranchName))
        {
            return request.BaseBranchName.Trim();
        }

        var activeProfile = existingConfig.Profiles.FirstOrDefault(p =>
            string.Equals(p.Key, existingConfig.ActiveProfileKey, StringComparison.OrdinalIgnoreCase))
            ?? existingConfig.Profiles.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(activeProfile?.BaseBranchName))
        {
            return activeProfile.BaseBranchName;
        }

        if (repository.SourceType != RepositorySourceType.Git)
        {
            warnings.Add("当前仓库不是 Git 源，基础分支默认使用 main 作为配置占位值。");
            return "main";
        }

        var branches = await _context.RepositoryBranches
            .AsNoTracking()
            .Where(b => b.RepositoryId == repository.Id)
            .Select(b => b.BranchName)
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                           !name.StartsWith("overlay/", StringComparison.OrdinalIgnoreCase))
            .ToListAsync(cancellationToken);

        foreach (var preferred in new[] { "main", "master", "develop", "dev" })
        {
            var matched = branches.FirstOrDefault(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                return matched;
            }
        }

        var fallback = branches
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            warnings.Add($"未显式指定基础分支，已回退使用仓库已有分支 '{fallback}'。");
            return fallback;
        }

        warnings.Add("未找到已处理过的分支记录，基础分支默认使用 main。");
        return "main";
    }

    private OverlayStructureAnalysisResult AnalyzeWorkspace(
        string workingDirectory,
        string primaryLanguage,
        int maxVariants,
        int maxSamplesPerVariant)
    {
        var files = EnumerateRepositoryFiles(workingDirectory)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(workingDirectory, path)))
            .ToList();
        var fileSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var fileNameLookup = OverlayPathResolver.CreateFileNameLookup(files);

        var candidateStats = new Dictionary<string, VariantCandidateAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in files)
        {
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                continue;
            }

            for (var i = 1; i < segments.Length - 1; i++)
            {
                var variantKey = segments[i];
                if (!IsPotentialVariantSegment(variantKey))
                {
                    continue;
                }

                var mappedPath = RemoveSegmentAt(segments, i);
                if (string.Equals(mappedPath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolution = OverlayPathResolver.Resolve(
                    workingDirectory,
                    relativePath,
                    mappedPath,
                    variantKey,
                    fileSet,
                    fileNameLookup);
                if (!resolution.IsOverride)
                {
                    continue;
                }

                var root = string.Join('/', segments.Take(i));
                var score = ComputeCandidateScore(variantKey, root, resolution);
                var stat = GetOrCreateCandidate(candidateStats, variantKey);
                stat.Score += score;
                stat.OverrideCount++;
                if (resolution.UsedFileNameVariantRemoval)
                {
                    stat.FileNameVariantRemovalCount++;
                }

                if (resolution.UsedCodeOverrideSignal)
                {
                    stat.CodeOverrideSignalCount++;
                }

                if (resolution.UsedBaseTypeFileMatch)
                {
                    stat.BaseTypeFileMatchCount++;
                }

                stat.RootCounts[root] = stat.RootCounts.TryGetValue(root, out var rootCount) ? rootCount + 1 : 1;
                stat.ExtensionCounts[Path.GetExtension(relativePath)] =
                    stat.ExtensionCounts.TryGetValue(Path.GetExtension(relativePath), out var extCount) ? extCount + 1 : 1;

                if (stat.OverrideSamples.Count < Math.Max(1, maxSamplesPerVariant))
                {
                    stat.OverrideSamples.Add(new OverlayOverrideSample
                    {
                        Root = root,
                        ProjectPath = relativePath,
                        BasePath = resolution.BasePath ?? resolution.DisplayPath
                    });
                }
            }
        }

        var rankedCandidates = candidateStats.Values
            .Where(x => x.OverrideCount > 0)
            .ToList();

        var explicitVariantCandidates = rankedCandidates
            .Where(candidate => LooksLikeExplicitVariantKey(candidate.Key))
            .ToList();

        var strongCandidates = rankedCandidates
            .Where(HasStructuralVariantSignals)
            .ToList();

        var candidatePool = explicitVariantCandidates.Count > 0
            ? explicitVariantCandidates
            : strongCandidates.Count > 0
                ? strongCandidates
                : rankedCandidates;

        var selectedCandidates = candidatePool
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.OverrideCount)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxVariants))
            .ToList();

        foreach (var relativePath in files)
        {
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                continue;
            }

            foreach (var candidate in selectedCandidates)
            {
                var index = Array.FindIndex(segments, x => x.Equals(candidate.Key, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    continue;
                }

                var mappedPath = RemoveSegmentAt(segments, index);
                if (string.Equals(mappedPath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolution = OverlayPathResolver.Resolve(
                    workingDirectory,
                    relativePath,
                    mappedPath,
                    candidate.Key,
                    fileSet,
                    fileNameLookup);
                if (resolution.IsOverride)
                {
                    continue;
                }

                candidate.AddedCount++;
                var root = string.Join('/', segments.Take(index));
                candidate.RootCounts[root] = candidate.RootCounts.TryGetValue(root, out var rootCount) ? rootCount + 1 : 1;
                candidate.ExtensionCounts[Path.GetExtension(relativePath)] =
                    candidate.ExtensionCounts.TryGetValue(Path.GetExtension(relativePath), out var extCount) ? extCount + 1 : 1;
                if (candidate.AddedSamples.Count < Math.Max(1, maxSamplesPerVariant))
                {
                    candidate.AddedSamples.Add(new OverlayAddedSample
                    {
                        Root = root,
                        ProjectPath = relativePath,
                        DisplayPath = resolution.DisplayPath
                    });
                }
            }
        }

        var roots = selectedCandidates
            .SelectMany(x => x.RootCounts)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => x.Value))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(6)
            .ToList();

        var includeGlobs = SuggestIncludeGlobs(primaryLanguage, selectedCandidates);

        var warnings = new List<string>();
        if (selectedCandidates.Count == 0)
        {
            warnings.Add("未发现明确的“移除目录段即可映射到基础文件”的样本，建议补充用户规则描述后再试。");
        }

        return new OverlayStructureAnalysisResult
        {
            SuggestedRoots = roots,
            SuggestedIncludeGlobs = includeGlobs,
            Candidates = selectedCandidates,
            Warnings = warnings
        };
    }

    private async Task<OverlayAiSuggestionResult?> TrySuggestWithAiAsync(
        Repository repository,
        string baseBranchName,
        string primaryLanguage,
        OverlaySuggestRequest request,
        RepositoryOverlayConfig existingConfig,
        OverlayStructureAnalysisResult analysis,
        RepositoryOverlayConfig heuristicConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = await _promptPlugin.LoadPromptAsync(
                "overlay-config-suggest",
                new Dictionary<string, string>
                {
                    ["repository_name"] = $"{repository.OrgName}/{repository.RepoName}",
                    ["base_branch_name"] = baseBranchName,
                    ["detected_primary_language"] = string.IsNullOrWhiteSpace(primaryLanguage) ? "(unknown)" : primaryLanguage,
                    ["user_intent"] = string.IsNullOrWhiteSpace(request.UserIntent) ? "(none)" : request.UserIntent.Trim(),
                    ["existing_config_json"] = JsonSerializer.Serialize(existingConfig, JsonOptions),
                    ["analysis_json"] = JsonSerializer.Serialize(analysis.ToResponse(), JsonOptions),
                    ["heuristic_config_json"] = JsonSerializer.Serialize(heuristicConfig, JsonOptions)
                },
                cancellationToken);

            var client = _agentFactory.CreateSimpleChatClient(
                _wikiOptions.ContentModel,
                maxToken: _wikiOptions.MaxOutputTokens,
                requestOptions: _wikiOptions.GetContentRequestOptions());
            var session = await client.CreateSessionAsync(cancellationToken);

            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.User, prompt)
            };

            var sb = new StringBuilder();
            await foreach (var update in client.RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    sb.Append(update.Text);
                }
            }

            var content = StripCodeFenceAndThinkTags(sb.ToString().Trim());
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var json = ExtractJsonObject(content);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize<OverlayAiSuggestionResult>(json, JsonOptions);
            if (parsed?.Config is null)
            {
                return null;
            }

            parsed.UsedAi = true;
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Overlay suggest AI call failed");
            return null;
        }
    }

    private RepositoryOverlayConfig BuildHeuristicConfig(
        Repository repository,
        string baseBranchName,
        OverlayStructureAnalysisResult analysis)
    {
        var profileKey = analysis.Candidates.FirstOrDefault()?.Key ?? "overlay";
        profileKey = SlugifyKey(profileKey);

        var variants = analysis.Candidates.Count == 0
            ? new List<OverlayVariant>
            {
                new()
                {
                    Key = "variant",
                    Name = "variant",
                    DetectionMode = OverlayVariantDetectionMode.PathSegmentEquals
                }
            }
            : analysis.Candidates.Select(candidate => new OverlayVariant
            {
                Key = candidate.Key,
                Name = candidate.SuggestedName,
                DetectionMode = OverlayVariantDetectionMode.PathSegmentEquals
            }).ToList();

        var config = new RepositoryOverlayConfig
        {
            Version = 1,
            ActiveProfileKey = profileKey,
            Profiles =
            [
                new OverlayProfile
                {
                    Key = profileKey,
                    Name = $"{repository.RepoName} Overlay",
                    BaseBranchName = baseBranchName,
                    OverlayBranchNameTemplate = $"overlay/{profileKey}",
                    Roots = analysis.SuggestedRoots,
                    Variants = variants,
                    MappingRules =
                    [
                        new OverlayMappingRule
                        {
                            Type = OverlayMappingRuleType.RemoveVariantSegment
                        }
                    ],
                    IncludeGlobs = analysis.SuggestedIncludeGlobs,
                    Generation = new OverlayGenerationOptions
                    {
                        OnlyShowProjectChanges = true,
                        DiffMode = "B",
                        MaxFiles = 200,
                        MaxFileBytes = 200_000
                    }
                }
            ]
        };

        return RepositoryOverlayConfigRules.Sanitize(config);
    }

    private static string BuildFallbackSummary(OverlayStructureAnalysisResult analysis)
    {
        if (analysis.Candidates.Count == 0)
        {
            return "未发现足够明显的覆盖模式，当前建议仅基于仓库结构给出保守配置。";
        }

        var candidate = analysis.Candidates[0];
        return $"识别到最明显的变体目录为 '{candidate.Key}'，覆盖样本 {candidate.OverrideCount} 个，新增样本 {candidate.AddedCount} 个。";
    }

    private static string BuildFallbackReasoning(OverlayStructureAnalysisResult analysis)
    {
        if (analysis.Candidates.Count == 0)
        {
            return "当前仓库缺少足够多的“去掉某个目录段后仍能命中基础文件”的样本，建议用户补充命名约定或手工调整 roots/variants。";
        }

        var top = analysis.Candidates[0];
        var roots = top.RootCounts.Keys.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3).ToList();
        var rootsText = roots.Count == 0 ? "全仓库" : string.Join("、", roots);
        return $"优先选择 '{top.Key}' 作为 variant，因为它在 {rootsText} 下形成了最多的覆盖映射，且移除该路径段后能稳定对应到基础文件。";
    }

    private static VariantCandidateAccumulator GetOrCreateCandidate(
        Dictionary<string, VariantCandidateAccumulator> candidates,
        string key)
    {
        if (!candidates.TryGetValue(key, out var value))
        {
            value = new VariantCandidateAccumulator(key);
            candidates[key] = value;
        }

        return value;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string root)
    {
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".next", "dist", "build"
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => excludedDirs.Contains(p)))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsPotentialVariantSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        if (segment.Length > 48)
        {
            return false;
        }

        if (GenericSegmentNames.Contains(segment))
        {
            return false;
        }

        return VariantSegmentRegex().IsMatch(segment);
    }

    private static int ComputeCandidateScore(
        string variantKey,
        string root,
        OverlayPathResolution resolution)
    {
        var score = 10;
        if (!string.IsNullOrWhiteSpace(root))
        {
            score += 2;
        }

        if (variantKey.All(char.IsDigit))
        {
            score += 12;
        }

        if (variantKey.Any(char.IsDigit) && variantKey.Any(char.IsLetter))
        {
            score += 8;
        }

        if (variantKey.Length <= 2)
        {
            score -= 6;
        }

        if (variantKey.All(char.IsLetter))
        {
            score -= 6;
        }

        if (resolution.UsedFileNameVariantRemoval)
        {
            score += 16;
        }

        if (resolution.UsedCodeOverrideSignal)
        {
            score += 18;
        }

        if (resolution.UsedBaseTypeFileMatch)
        {
            score += 20;
        }

        return score;
    }

    private static bool LooksLikeExplicitVariantKey(string key)
    {
        return ExplicitVariantKeyRegex().IsMatch(key);
    }

    private static bool HasStructuralVariantSignals(VariantCandidateAccumulator candidate)
    {
        return candidate.FileNameVariantRemovalCount > 0 ||
               candidate.CodeOverrideSignalCount > 0 ||
               candidate.BaseTypeFileMatchCount > 0;
    }

    private static List<string> SuggestIncludeGlobs(
        string primaryLanguage,
        List<VariantCandidateAccumulator> candidates)
    {
        var byLanguage = primaryLanguage.Trim().ToLowerInvariant() switch
        {
            "c#" => new List<string> { "**/*.cs" },
            "java" => new List<string> { "**/*.java" },
            "typescript" => new List<string> { "**/*.ts", "**/*.tsx" },
            "javascript" => new List<string> { "**/*.js", "**/*.jsx" },
            "python" => new List<string> { "**/*.py" },
            "go" => new List<string> { "**/*.go" },
            "rust" => new List<string> { "**/*.rs" },
            "php" => new List<string> { "**/*.php" },
            _ => []
        };

        if (byLanguage.Count > 0)
        {
            return byLanguage;
        }

        return candidates
            .SelectMany(x => x.ExtensionCounts)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => x.Value))
            .Select(g => g.Key)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Take(3)
            .Select(ext => $"**/*{ext}")
            .ToList();
    }

    private static string RemoveSegmentAt(string[] segments, int index)
    {
        return string.Join("/", segments.Where((_, i) => i != index));
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string SlugifyKey(string value)
    {
        var buffer = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            buffer.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        var result = buffer.ToString().Trim('-');
        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(result) ? "overlay" : result;
    }

    private static string StripCodeFenceAndThinkTags(string text)
    {
        var withoutThink = Regex.Replace(text, "<think>[\\s\\S]*?</think>", string.Empty, RegexOptions.IgnoreCase);
        var trimmed = withoutThink.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }

        return trimmed.Trim();
    }

    private static string? ExtractJsonObject(string text)
    {
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            return null;
        }

        return text[first..(last + 1)];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{1,47}$", RegexOptions.Compiled)]
    private static partial Regex VariantSegmentRegex();

    [GeneratedRegex(@"\d{3,}", RegexOptions.Compiled)]
    private static partial Regex ExplicitVariantKeyRegex();

    private sealed class OverlayAiSuggestionResult
    {
        public string? Summary { get; set; }
        public string? ReasoningSummary { get; set; }
        public List<string>? Warnings { get; set; }
        public RepositoryOverlayConfig? Config { get; set; }
        public bool UsedAi { get; set; }
    }

    private sealed class OverlayStructureAnalysisResult
    {
        public List<string> SuggestedRoots { get; init; } = [];
        public List<string> SuggestedIncludeGlobs { get; init; } = [];
        public List<VariantCandidateAccumulator> Candidates { get; init; } = [];
        public List<string> Warnings { get; init; } = [];

        public OverlayRepositoryStructureAnalysis ToResponse()
        {
            return new OverlayRepositoryStructureAnalysis
            {
                SuggestedRoots = SuggestedRoots,
                SuggestedIncludeGlobs = SuggestedIncludeGlobs,
                VariantCandidates = Candidates.Select(candidate => new OverlayVariantCandidate
                {
                    Key = candidate.Key,
                    SuggestedName = candidate.SuggestedName,
                    Score = candidate.Score,
                    OverrideCount = candidate.OverrideCount,
                    AddedCount = candidate.AddedCount,
                    Roots = candidate.RootCounts
                        .OrderByDescending(x => x.Value)
                        .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.Key)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList(),
                    OverrideSamples = candidate.OverrideSamples,
                    AddedSamples = candidate.AddedSamples
                }).ToList()
            };
        }
    }

    private sealed class VariantCandidateAccumulator
    {
        public VariantCandidateAccumulator(string key)
        {
            Key = key;
            SuggestedName = key;
        }

        public string Key { get; }
        public string SuggestedName { get; }
        public int Score { get; set; }
        public int OverrideCount { get; set; }
        public int AddedCount { get; set; }
        public int FileNameVariantRemovalCount { get; set; }
        public int CodeOverrideSignalCount { get; set; }
        public int BaseTypeFileMatchCount { get; set; }
        public Dictionary<string, int> RootCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ExtensionCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<OverlayOverrideSample> OverrideSamples { get; } = [];
        public List<OverlayAddedSample> AddedSamples { get; } = [];
    }
}
