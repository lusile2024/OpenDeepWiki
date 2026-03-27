using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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

public sealed class OverlayGenerationResult
{
    public string OverlayBranchName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "zh";
    public OverlayIndexSummary Summary { get; set; } = new();
}

public interface IOverlayWikiService
{
    Task<OverlayIndex> PreviewAsync(
        string repositoryId,
        string? profileKey,
        CancellationToken cancellationToken = default);

    Task<OverlayGenerationResult> GenerateAsync(
        string repositoryId,
        string? profileKey,
        CancellationToken cancellationToken = default);
}

public sealed class OverlayWikiService : IOverlayWikiService
{
    private static readonly JsonSerializerOptions SourceFilesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IContext _context;
    private readonly IAdminRepositoryOverlayService _overlayConfigService;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IOverlayIndexBuilder _indexBuilder;
    private readonly AgentFactory _agentFactory;
    private readonly IPromptPlugin _promptPlugin;
    private readonly WikiGeneratorOptions _wikiOptions;
    private readonly ILogger<OverlayWikiService> _logger;

    public OverlayWikiService(
        IContext context,
        IAdminRepositoryOverlayService overlayConfigService,
        IRepositoryAnalyzer repositoryAnalyzer,
        IOverlayIndexBuilder indexBuilder,
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> wikiOptions,
        ILogger<OverlayWikiService> logger)
    {
        _context = context;
        _overlayConfigService = overlayConfigService;
        _repositoryAnalyzer = repositoryAnalyzer;
        _indexBuilder = indexBuilder;
        _agentFactory = agentFactory;
        _promptPlugin = promptPlugin;
        _wikiOptions = wikiOptions.Value;
        _logger = logger;
    }

    public async Task<OverlayIndex> PreviewAsync(
        string repositoryId,
        string? profileKey,
        CancellationToken cancellationToken = default)
    {
        var repository = await GetRepositoryAsync(repositoryId, cancellationToken);
        var config = await _overlayConfigService.GetConfigAsync(repositoryId, cancellationToken);
        var profile = ResolveProfile(config, profileKey);

        var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            profile.BaseBranchName,
            previousCommitId: null,
            cancellationToken);

        try
        {
            var index = _indexBuilder.Build(workspace.WorkingDirectory, profile);
            return CapIndex(index, profile.Generation.MaxFiles);
        }
        finally
        {
            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
    }

    public async Task<OverlayGenerationResult> GenerateAsync(
        string repositoryId,
        string? profileKey,
        CancellationToken cancellationToken = default)
    {
        var repository = await GetRepositoryAsync(repositoryId, cancellationToken);
        var config = await _overlayConfigService.GetConfigAsync(repositoryId, cancellationToken);
        var profile = ResolveProfile(config, profileKey);

        var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            profile.BaseBranchName,
            previousCommitId: null,
            cancellationToken);

        try
        {
            var index = _indexBuilder.Build(workspace.WorkingDirectory, profile);
            var capped = CapIndex(index, profile.Generation.MaxFiles);

            var overlayBranchName = BuildOverlayBranchName(profile);
            var branch = await GetOrCreateBranchAsync(repositoryId, overlayBranchName, cancellationToken);
            var language = await GetOrCreateLanguageAsync(branch.Id, "zh", cancellationToken);

            await ClearExistingDocsAsync(language.Id, cancellationToken);

            await WriteOverlayDocsAsync(
                workspace.WorkingDirectory,
                repository,
                branch,
                language,
                profile,
                capped,
                cancellationToken);

            return new OverlayGenerationResult
            {
                OverlayBranchName = overlayBranchName,
                LanguageCode = language.LanguageCode,
                Summary = capped.Summary
            };
        }
        finally
        {
            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
    }

    private static OverlayIndex CapIndex(OverlayIndex index, int maxFiles)
    {
        var max = Math.Max(1, maxFiles);
        var uncappedSummary = index.Summary;
        var overrides = index.Overrides.Take(max).ToList();
        var remaining = Math.Max(0, max - overrides.Count);
        var added = index.Added.Take(remaining).ToList();
        var isCapped = uncappedSummary.TotalCount > max;

        return new OverlayIndex
        {
            ProfileKey = index.ProfileKey,
            ProfileName = index.ProfileName,
            BaseBranchName = index.BaseBranchName,
            IsCapped = isCapped,
            MaxFilesApplied = max,
            UncappedSummary = isCapped
                ? new OverlayIndexSummary
                {
                    OverrideCount = uncappedSummary.OverrideCount,
                    AddedCount = uncappedSummary.AddedCount,
                    TotalCount = uncappedSummary.TotalCount
                }
                : null,
            Overrides = overrides,
            Added = added
        };
    }

    private OverlayProfile ResolveProfile(RepositoryOverlayConfig config, string? profileKey)
    {
        var key = profileKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = config.ActiveProfileKey;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return config.Profiles[0];
        }

        var found = config.Profiles.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        return found ?? config.Profiles[0];
    }

    private static string BuildOverlayBranchName(OverlayProfile profile)
    {
        var branchName = profile.OverlayBranchNameTemplate.Replace("{profileKey}", profile.Key);
        branchName = branchName.Trim();
        return branchName;
    }

    private async Task<Repository> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId && !r.IsDeleted, cancellationToken);

        if (repository is null)
        {
            throw new KeyNotFoundException("仓库不存在");
        }

        return repository;
    }

    private async Task<RepositoryBranch> GetOrCreateBranchAsync(
        string repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        var branch = await _context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.RepositoryId == repositoryId && b.BranchName == branchName, cancellationToken);

        if (branch is not null)
        {
            return branch;
        }

        branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchName = branchName
        };

        _context.RepositoryBranches.Add(branch);
        await _context.SaveChangesAsync(cancellationToken);
        TryClearChangeTracker();
        return branch;
    }

    private async Task<BranchLanguage> GetOrCreateLanguageAsync(
        string branchId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var language = await _context.BranchLanguages
            .FirstOrDefaultAsync(l => l.RepositoryBranchId == branchId && l.LanguageCode == languageCode, cancellationToken);

        if (language is not null)
        {
            return language;
        }

        language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branchId,
            LanguageCode = languageCode,
            UpdateSummary = string.Empty,
            IsDefault = false
        };

        _context.BranchLanguages.Add(language);
        await _context.SaveChangesAsync(cancellationToken);
        TryClearChangeTracker();
        return language;
    }

    private async Task ClearExistingDocsAsync(string branchLanguageId, CancellationToken cancellationToken)
    {
        var catalogs = await _context.DocCatalogs
            .Where(c => c.BranchLanguageId == branchLanguageId)
            .ToListAsync(cancellationToken);

        var docFileIds = catalogs
            .Where(c => !string.IsNullOrEmpty(c.DocFileId))
            .Select(c => c.DocFileId!)
            .Distinct()
            .ToList();

        if (catalogs.Count > 0)
        {
            _context.DocCatalogs.RemoveRange(catalogs);
        }

        if (docFileIds.Count > 0)
        {
            var docFiles = await _context.DocFiles
                .Where(f => docFileIds.Contains(f.Id))
                .ToListAsync(cancellationToken);

            if (docFiles.Count > 0)
            {
                _context.DocFiles.RemoveRange(docFiles);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        TryClearChangeTracker();
    }

    private async Task WriteOverlayDocsAsync(
        string workingDirectory,
        Repository repository,
        RepositoryBranch overlayBranch,
        BranchLanguage language,
        OverlayProfile profile,
        OverlayIndex index,
        CancellationToken cancellationToken)
    {
        // Root pages/folders
        var rootOverview = await CreateCatalogWithDocAsync(
            language.Id,
            parentId: null,
            title: "差异概览",
            path: "overview",
            order: 0,
            content: BuildOverviewMarkdown(repository, overlayBranch, profile, index),
            sourceFiles: [],
            cancellationToken);

        var overridesFolder = await CreateCatalogAsync(
            language.Id,
            parentId: null,
            title: "覆盖文件",
            path: "overrides",
            order: 1,
            cancellationToken);

        var addedFolder = await CreateCatalogAsync(
            language.Id,
            parentId: null,
            title: "新增文件",
            path: "added",
            order: 2,
            cancellationToken);

        // Group by variant
        var variantOverrides = index.Overrides.GroupBy(o => o.VariantKey, StringComparer.OrdinalIgnoreCase).ToList();
        var variantAdded = index.Added.GroupBy(a => a.VariantKey, StringComparer.OrdinalIgnoreCase).ToList();

        var order = 0;
        foreach (var group in variantOverrides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variantKey = group.Key;
            var variantName = group.First().VariantName;
            var variantFolder = await CreateCatalogAsync(
                language.Id,
                overridesFolder.Id,
                title: variantName,
                path: $"overrides/{SlugifySegment(variantKey)}",
                order: order++,
                cancellationToken);

            await WriteFileTreeAsync(
                workingDirectory,
                language.Id,
                variantFolder.Id,
                variantFolder.Path,
                group.Select(item => new OverlayFileNode
                {
                    Title = item.DisplayPath,
                    SlugPath = item.DisplayPath,
                    SourceFiles = [item.BasePath, item.ProjectPath],
                    ContentFactory = ct => GenerateOverrideDocAsync(
                        workingDirectory,
                        profile,
                        item,
                        ct)
                }).ToList(),
                cancellationToken);
        }

        order = 0;
        foreach (var group in variantAdded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variantKey = group.Key;
            var variantName = group.First().VariantName;
            var variantFolder = await CreateCatalogAsync(
                language.Id,
                addedFolder.Id,
                title: variantName,
                path: $"added/{SlugifySegment(variantKey)}",
                order: order++,
                cancellationToken);

            await WriteFileTreeAsync(
                workingDirectory,
                language.Id,
                variantFolder.Id,
                variantFolder.Path,
                group.Select(item => new OverlayFileNode
                {
                    Title = item.DisplayPath,
                    SlugPath = item.DisplayPath,
                    SourceFiles = [item.ProjectPath],
                    ContentFactory = ct => GenerateAddedDocAsync(
                        workingDirectory,
                        profile,
                        item,
                        ct)
                }).ToList(),
                cancellationToken);
        }
    }

    private sealed class OverlayFileNode
    {
        public string Title { get; init; } = string.Empty;
        public string SlugPath { get; init; } = string.Empty; // display path (used to build folder structure)
        public List<string> SourceFiles { get; init; } = new();
        public Func<CancellationToken, Task<string>> ContentFactory { get; init; } = _ => Task.FromResult(string.Empty);
    }

    private async Task WriteFileTreeAsync(
        string workingDirectory,
        string branchLanguageId,
        string parentId,
        string parentPathPrefix,
        List<OverlayFileNode> nodes,
        CancellationToken cancellationToken)
    {
        // Create nested folders based on path segments, then leaf pages for files.
        // To keep url paths stable, use a deterministic slug for each segment and leaf.
        var folders = new Dictionary<string, DocCatalog>(StringComparer.OrdinalIgnoreCase);
        var preparedNodes = PrepareNodesForWrite(nodes);

        int order = 0;
        foreach (var node in preparedNodes.OrderBy(n => n.SlugPath, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segments = node.SlugPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentParentId = parentId;
            var currentPathPrefix = ""; // relative to variant folder slug path

            // Create folders for all directory segments.
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                var segSlug = SlugifySegment(seg);
                currentPathPrefix = string.IsNullOrEmpty(currentPathPrefix)
                    ? segSlug
                    : $"{currentPathPrefix}/{segSlug}";

                var folderKey = $"{currentParentId}:{currentPathPrefix}";
                if (!folders.TryGetValue(folderKey, out var folder))
                {
                    folder = await CreateCatalogAsync(
                        branchLanguageId,
                        currentParentId,
                        title: seg,
                        path: $"{parentPathPrefix}/{currentPathPrefix}".Trim('/'),
                        order: order++,
                        cancellationToken);
                    folders[folderKey] = folder;
                }

                currentParentId = folder.Id;
            }

            var fileName = segments[^1];
            var leafSlug = SlugifyLeaf(fileName, BuildLeafIdentityKey(node));
            var leafPath = string.IsNullOrEmpty(currentPathPrefix)
                ? $"{parentPathPrefix}/{leafSlug}".Trim('/')
                : $"{parentPathPrefix}/{currentPathPrefix}/{leafSlug}".Trim('/');

            var content = await node.ContentFactory(cancellationToken);

            await CreateCatalogWithDocAsync(
                branchLanguageId,
                currentParentId,
                title: node.Title,
                path: leafPath,
                order: order++,
                content: content,
                sourceFiles: node.SourceFiles,
                cancellationToken);
        }
    }

    private static List<OverlayFileNode> PrepareNodesForWrite(List<OverlayFileNode> nodes)
    {
        var results = new List<OverlayFileNode>(nodes.Count);
        foreach (var group in nodes.GroupBy(node => node.SlugPath, StringComparer.OrdinalIgnoreCase))
        {
            var isDuplicate = group.Count() > 1;
            foreach (var node in group)
            {
                results.Add(new OverlayFileNode
                {
                    Title = BuildLeafTitle(node, isDuplicate),
                    SlugPath = node.SlugPath,
                    SourceFiles = node.SourceFiles,
                    ContentFactory = node.ContentFactory
                });
            }
        }

        return results;
    }

    private static string BuildLeafTitle(OverlayFileNode node, bool isDuplicate)
    {
        var fileName = Path.GetFileName(node.SlugPath);
        if (!isDuplicate)
        {
            return fileName;
        }

        var sourceHint = BuildSourceHint(node.SourceFiles.LastOrDefault());
        return string.IsNullOrWhiteSpace(sourceHint)
            ? fileName
            : $"{fileName} [{sourceHint}]";
    }

    private static string BuildSourceHint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments.TakeLast(Math.Min(3, segments.Length)));
    }

    private static string BuildLeafIdentityKey(OverlayFileNode node)
    {
        if (node.SourceFiles.Count == 0)
        {
            return node.SlugPath;
        }

        return $"{node.SlugPath}|{string.Join("|", node.SourceFiles)}";
    }

    private async Task<string> GenerateOverrideDocAsync(
        string workingDirectory,
        OverlayProfile profile,
        OverlayOverrideItem item,
        CancellationToken cancellationToken)
    {
        var baseFull = Path.Combine(workingDirectory, item.BasePath.Replace('/', Path.DirectorySeparatorChar));
        var projectFull = Path.Combine(workingDirectory, item.ProjectPath.Replace('/', Path.DirectorySeparatorChar));

        var baseText = ReadFileTruncated(baseFull, profile.Generation.MaxFileBytes);
        var projectText = ReadFileTruncated(projectFull, profile.Generation.MaxFileBytes);

        var prompt = await _promptPlugin.LoadPromptAsync(
            "overlay-file-diff",
            new Dictionary<string, string>
            {
                ["diff_mode"] = profile.Generation.DiffMode,
                ["base_path"] = item.BasePath,
                ["project_path"] = item.ProjectPath,
                ["base_content"] = baseText,
                ["project_content"] = projectText
            },
            cancellationToken);

        return await RunModelAsync(prompt, cancellationToken);
    }

    private async Task<string> GenerateAddedDocAsync(
        string workingDirectory,
        OverlayProfile profile,
        OverlayAddedItem item,
        CancellationToken cancellationToken)
    {
        var projectFull = Path.Combine(workingDirectory, item.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var projectText = ReadFileTruncated(projectFull, profile.Generation.MaxFileBytes);

        var prompt = await _promptPlugin.LoadPromptAsync(
            "overlay-file-added",
            new Dictionary<string, string>
            {
                ["project_path"] = item.ProjectPath,
                ["project_content"] = projectText
            },
            cancellationToken);

        return await RunModelAsync(prompt, cancellationToken);
    }

    private async Task<string> RunModelAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var model = _wikiOptions.ContentModel;
            var client = _agentFactory.CreateSimpleChatClient(model, maxToken: _wikiOptions.MaxOutputTokens, requestOptions: _wikiOptions.GetContentRequestOptions());
            var thread = await client.CreateSessionAsync(cancellationToken);

            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(ChatRole.User, prompt)
            };

            var sb = new StringBuilder();
            await foreach (var update in client.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    sb.Append(update.Text);
                }
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result)
                ? "生成内容为空（模型未返回正文）"
                : StripThinkTags(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Overlay wiki model call failed");
            return $"生成失败：{ex.Message}";
        }
    }

    private static string StripThinkTags(string text)
    {
        // Keep it simple; only remove <think>...</think> blocks if present.
        var start = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text;
        }

        var end = text.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return text;
        }

        return (text[..start] + text[(end + "</think>".Length)..]).Trim();
    }

    private static string ReadFileTruncated(string fullPath, int maxBytes)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                return "[文件不存在]";
            }

            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var toRead = (int)Math.Min(maxBytes, fs.Length);
            var buffer = new byte[toRead];
            var read = fs.Read(buffer, 0, toRead);

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (fs.Length > toRead)
            {
                text += "\n\n[... 文件过大，已截断 ...]";
            }

            return text;
        }
        catch
        {
            return "[读取失败]";
        }
    }

    private static string BuildOverviewMarkdown(
        Repository repository,
        RepositoryBranch overlayBranch,
        OverlayProfile profile,
        OverlayIndex index)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 差异概览");
        sb.AppendLine();
        sb.AppendLine($"- 仓库：`{repository.OrgName}/{repository.RepoName}`");
        sb.AppendLine($"- 基础分支：`{profile.BaseBranchName}`");
        sb.AppendLine($"- Overlay 分支：`{overlayBranch.BranchName}`");
        sb.AppendLine($"- Profile：`{profile.Key}` ({profile.Name})");
        sb.AppendLine();
        sb.AppendLine("## 统计");
        sb.AppendLine();
        sb.AppendLine($"- 覆盖文件：{index.Summary.OverrideCount}");
        sb.AppendLine($"- 新增文件：{index.Summary.AddedCount}");
        sb.AppendLine($"- 合计：{index.Summary.TotalCount}");
        sb.AppendLine();

        sb.AppendLine("## Variants");
        sb.AppendLine();
        foreach (var variant in profile.Variants)
        {
            sb.AppendLine($"- `{variant.Key}`");
        }

        sb.AppendLine();
        sb.AppendLine("## 说明");
        sb.AppendLine();
        sb.AppendLine("此分支为 OpenDeepWiki 生成的虚拟 Overlay Wiki，仅包含项目版相对于基础版的新增与覆盖文件。");
        sb.AppendLine("覆盖文件页面包含：变更摘要、关键差异点，以及若干关键 diff 片段（Diff 模式 B）。");

        return sb.ToString();
    }

    private async Task<DocCatalog> CreateCatalogAsync(
        string branchLanguageId,
        string? parentId,
        string title,
        string path,
        int order,
        CancellationToken cancellationToken)
    {
        var catalog = new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = branchLanguageId,
            ParentId = parentId,
            Title = title,
            Path = path.Trim('/'),
            Order = order,
            DocFileId = null
        };

        _context.DocCatalogs.Add(catalog);
        await _context.SaveChangesAsync(cancellationToken);
        TryClearChangeTracker();
        return catalog;
    }

    private async Task<DocCatalog> CreateCatalogWithDocAsync(
        string branchLanguageId,
        string? parentId,
        string title,
        string path,
        int order,
        string content,
        List<string> sourceFiles,
        CancellationToken cancellationToken)
    {
        var docFile = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = branchLanguageId,
            Content = content ?? string.Empty,
            SourceFiles = sourceFiles.Count == 0 ? "[]" : JsonSerializer.Serialize(sourceFiles, SourceFilesJsonOptions)
        };

        var catalog = new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = branchLanguageId,
            ParentId = parentId,
            Title = title,
            Path = path.Trim('/'),
            Order = order,
            DocFileId = docFile.Id
        };

        _context.DocFiles.Add(docFile);
        _context.DocCatalogs.Add(catalog);
        await _context.SaveChangesAsync(cancellationToken);
        TryClearChangeTracker();
        return catalog;
    }

    private void TryClearChangeTracker()
    {
        if (_context is DbContext dbContext)
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static string SlugifyLeaf(string fileName, string fullPath)
    {
        // Add a short stable hash suffix to avoid collisions across folders with same filename.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))[..8]
            .ToLowerInvariant();
        return $"{SlugifySegment(fileName)}-{hash}";
    }

    private static string SlugifySegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "x";
        }

        var sb = new StringBuilder();
        foreach (var ch in segment.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append('-');
            }
        }

        var value = sb.ToString().Trim('-');
        while (value.Contains("--", StringComparison.Ordinal))
        {
            value = value.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(value) ? "x" : value;
    }
}
