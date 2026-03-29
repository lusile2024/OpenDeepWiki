namespace OpenDeepWiki.Services.Wiki;

public static class RepositoryWorkflowConfigRules
{
    public static RepositoryWorkflowConfig Sanitize(RepositoryWorkflowConfig? config)
    {
        config ??= new RepositoryWorkflowConfig();
        config.Version = config.Version <= 0 ? 1 : config.Version;
        config.Profiles ??= [];

        foreach (var profile in config.Profiles)
        {
            SanitizeProfile(profile);
        }

        config.ActiveProfileKey = string.IsNullOrWhiteSpace(config.ActiveProfileKey)
            ? config.Profiles.FirstOrDefault(profile => profile.Enabled)?.Key
            : config.ActiveProfileKey.Trim();

        return config;
    }

    public static void Validate(RepositoryWorkflowConfig? rawConfig)
    {
        var config = Sanitize(rawConfig);

        var duplicateProfileKey = config.Profiles
            .GroupBy(profile => profile.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProfileKey is not null)
        {
            throw new InvalidOperationException($"Duplicate workflow profile key: '{duplicateProfileKey.Key}'.");
        }

        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Key))
            {
                throw new InvalidOperationException("Workflow profile key cannot be empty.");
            }

            if (profile.Key.Length > 64)
            {
                throw new InvalidOperationException($"Workflow profile '{profile.Key}' key is too long.");
            }

            if (profile.Mode == RepositoryWorkflowProfileMode.WcsRequestExecutor &&
                profile.AnchorDirectories.Count == 0 &&
                profile.AnchorNames.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Workflow profile '{profile.Key}' must configure at least one anchor directory or anchor name.");
            }

            var duplicateChapterKey = profile.ChapterProfiles
                .GroupBy(chapter => chapter.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
            if (duplicateChapterKey is not null)
            {
                throw new InvalidOperationException(
                    $"Workflow profile '{profile.Key}' contains duplicate chapter key '{duplicateChapterKey.Key}'.");
            }

            foreach (var chapter in profile.ChapterProfiles)
            {
                if (string.IsNullOrWhiteSpace(chapter.Key))
                {
                    throw new InvalidOperationException(
                        $"Workflow profile '{profile.Key}' contains an empty chapter key.");
                }

                if (chapter.RootSymbolNames.Count == 0 && chapter.MustExplainSymbols.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Workflow profile '{profile.Key}' chapter '{chapter.Key}' must configure root symbols or must-explain symbols.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(config.ActiveProfileKey) &&
            config.Profiles.All(profile => !string.Equals(profile.Key, config.ActiveProfileKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ActiveProfileKey does not match any workflow profile.");
        }
    }

    public static RepositoryWorkflowProfile SanitizeProfile(RepositoryWorkflowProfile? profile)
    {
        profile ??= new RepositoryWorkflowProfile();
        profile.Key = string.IsNullOrWhiteSpace(profile.Key)
            ? SlugifyKey(profile.Name)
            : profile.Key.Trim();
        profile.Name = string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Key
            : profile.Name.Trim();
        profile.Description = NormalizeOptionalValue(profile.Description);
        profile.EntryRoots = NormalizeNames(profile.EntryRoots);
        profile.EntryKinds = NormalizeNames(profile.EntryKinds);

        profile.AnchorDirectories = NormalizeRelativePaths(profile.AnchorDirectories);
        profile.AnchorNames = NormalizeNames(profile.AnchorNames);
        profile.PrimaryTriggerDirectories = NormalizeRelativePaths(profile.PrimaryTriggerDirectories);
        profile.CompensationTriggerDirectories = NormalizeRelativePaths(profile.CompensationTriggerDirectories);
        profile.SchedulerDirectories = NormalizeRelativePaths(profile.SchedulerDirectories);
        profile.ServiceDirectories = NormalizeRelativePaths(profile.ServiceDirectories);
        profile.RepositoryDirectories = NormalizeRelativePaths(profile.RepositoryDirectories);

        profile.PrimaryTriggerNames = NormalizeNames(profile.PrimaryTriggerNames);
        profile.CompensationTriggerNames = NormalizeNames(profile.CompensationTriggerNames);
        profile.SchedulerNames = NormalizeNames(profile.SchedulerNames);
        profile.RequestEntityNames = NormalizeNames(profile.RequestEntityNames);
        profile.RequestServiceNames = NormalizeNames(profile.RequestServiceNames);
        profile.RequestRepositoryNames = NormalizeNames(profile.RequestRepositoryNames);

        profile.Source ??= new RepositoryWorkflowProfileSource();
        profile.Source.Type = string.IsNullOrWhiteSpace(profile.Source.Type)
            ? "manual"
            : profile.Source.Type.Trim();
        profile.Source.SessionId = NormalizeOptionalValue(profile.Source.SessionId);
        profile.Source.UpdatedByUserId = NormalizeOptionalValue(profile.Source.UpdatedByUserId);
        profile.Source.UpdatedByUserName = NormalizeOptionalValue(profile.Source.UpdatedByUserName);

        profile.DocumentPreferences ??= new WorkflowDocumentPreferences();
        profile.DocumentPreferences.WritingHint = NormalizeOptionalValue(profile.DocumentPreferences.WritingHint);
        profile.DocumentPreferences.PreferredTerms = NormalizeNames(profile.DocumentPreferences.PreferredTerms);
        profile.DocumentPreferences.RequiredSections = NormalizeNames(profile.DocumentPreferences.RequiredSections);
        profile.DocumentPreferences.AvoidPrimaryTriggerNames = NormalizeNames(profile.DocumentPreferences.AvoidPrimaryTriggerNames);

        profile.Analysis ??= new WorkflowProfileAnalysisOptions();
        profile.Analysis.EntryDirectories = NormalizeRelativePaths(profile.Analysis.EntryDirectories);
        profile.Analysis.RootSymbolNames = NormalizeNames(profile.Analysis.RootSymbolNames);
        profile.Analysis.MustExplainSymbols = NormalizeNames(profile.Analysis.MustExplainSymbols);
        profile.Analysis.AllowedNamespaces = NormalizeNames(profile.Analysis.AllowedNamespaces);
        profile.Analysis.StopNamespacePrefixes = NormalizeNames(profile.Analysis.StopNamespacePrefixes);
        profile.Analysis.StopNamePatterns = NormalizeNames(profile.Analysis.StopNamePatterns);
        profile.Analysis.DepthBudget = NormalizeInteger(profile.Analysis.DepthBudget, 1, 8, 4);
        profile.Analysis.MaxNodes = NormalizeInteger(profile.Analysis.MaxNodes, 8, 200, 48);

        profile.ChapterProfiles = (profile.ChapterProfiles ?? [])
            .Select(SanitizeChapterProfile)
            .DistinctBy(chapter => chapter.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        profile.LspAssist ??= new WorkflowLspAssistOptions();
        profile.LspAssist.PreferredServer = NormalizeOptionalValue(profile.LspAssist.PreferredServer);
        profile.LspAssist.RequestTimeoutMs = NormalizeInteger(profile.LspAssist.RequestTimeoutMs, 1000, 120000, 10000);
        profile.LspAssist.AdditionalEntrySymbolHints = NormalizeNames(profile.LspAssist.AdditionalEntrySymbolHints);
        profile.LspAssist.SuggestedEntryDirectories = NormalizeRelativePaths(profile.LspAssist.SuggestedEntryDirectories);
        profile.LspAssist.SuggestedRootSymbolNames = NormalizeNames(profile.LspAssist.SuggestedRootSymbolNames);
        profile.LspAssist.SuggestedMustExplainSymbols = NormalizeNames(profile.LspAssist.SuggestedMustExplainSymbols);
        profile.LspAssist.CallHierarchyEdges = NormalizeCallHierarchyEdges(profile.LspAssist.CallHierarchyEdges);

        profile.Acp ??= new WorkflowAcpOptions();
        profile.Acp.Objective = string.IsNullOrWhiteSpace(profile.Acp.Objective)
            ? "深挖业务流主线与分支"
            : profile.Acp.Objective.Trim();
        profile.Acp.MaxBranchTasks = NormalizeInteger(profile.Acp.MaxBranchTasks, 1, 16, 4);
        profile.Acp.MaxParallelTasks = NormalizeInteger(profile.Acp.MaxParallelTasks, 1, profile.Acp.MaxBranchTasks, 2);
        profile.Acp.SplitStrategy = string.IsNullOrWhiteSpace(profile.Acp.SplitStrategy)
            ? "by-chapter-and-branch"
            : profile.Acp.SplitStrategy.Trim();

        return profile;
    }

    public static List<string> GetDraftValidationIssues(RepositoryWorkflowProfile? rawProfile)
    {
        var profile = SanitizeProfile(rawProfile);
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            issues.Add("需要填写业务流名称。");
        }

        if (profile.AnchorDirectories.Count == 0 && profile.AnchorNames.Count == 0)
        {
            issues.Add("需要至少指定一个锚点目录或锚点名称，用来锁定核心 executor / service。");
        }

        if (profile.PrimaryTriggerDirectories.Count == 0 && profile.PrimaryTriggerNames.Count == 0)
        {
            issues.Add("建议补充主入口控制器或主入口目录，否则主业务入口容易识别不准。");
        }

        if (profile.EntryRoots.Count == 0)
        {
            issues.Add("建议补充 entryRoots，明确业务流主入口符号，便于 LSP 增强与章节深挖。");
        }

        if (profile.SchedulerDirectories.Count == 0 && profile.SchedulerNames.Count == 0)
        {
            issues.Add("建议补充调度任务或扫描任务信息，否则流程中段可能缺失。");
        }

        if (profile.Analysis.RootSymbolNames.Count == 0)
        {
            issues.Add("建议补充 analysis.rootSymbolNames，便于后续调用链深挖和章节切片。");
        }

        if (profile.Analysis.MustExplainSymbols.Count == 0)
        {
            issues.Add("建议补充 analysis.mustExplainSymbols，便于覆盖校验核心方法/服务是否真正被解释到。");
        }

        if (profile.ChapterProfiles.Count == 0)
        {
            issues.Add("建议补充 chapterProfiles，把入口链路、分支判断、持久化/状态变更拆成可深挖章节。");
        }

        return issues;
    }

    private static WorkflowChapterProfile SanitizeChapterProfile(WorkflowChapterProfile? chapter)
    {
        chapter ??= new WorkflowChapterProfile();
        chapter.Key = string.IsNullOrWhiteSpace(chapter.Key)
            ? SlugifyKey(chapter.Title)
            : chapter.Key.Trim();
        chapter.Title = string.IsNullOrWhiteSpace(chapter.Title)
            ? chapter.Key
            : chapter.Title.Trim();
        chapter.Description = NormalizeOptionalValue(chapter.Description);
        chapter.RootSymbolNames = NormalizeNames(chapter.RootSymbolNames);
        chapter.MustExplainSymbols = NormalizeNames(chapter.MustExplainSymbols);
        chapter.RequiredSections = NormalizeNames(chapter.RequiredSections);
        chapter.OutputArtifacts = NormalizeNames(chapter.OutputArtifacts);
        if (chapter.OutputArtifacts.Count == 0)
        {
            chapter.OutputArtifacts = ["markdown"];
        }

        if (chapter.IncludeFlowchart &&
            !chapter.OutputArtifacts.Contains("flowchart", StringComparer.OrdinalIgnoreCase))
        {
            chapter.OutputArtifacts.Add("flowchart");
        }

        if (chapter.IncludeMindmap &&
            !chapter.OutputArtifacts.Contains("mindmap", StringComparer.OrdinalIgnoreCase))
        {
            chapter.OutputArtifacts.Add("mindmap");
        }

        chapter.DepthBudget = NormalizeInteger(chapter.DepthBudget, 1, 8, 3);
        chapter.MaxNodes = NormalizeInteger(chapter.MaxNodes, 4, 120, 28);
        return chapter;
    }

    private static List<WorkflowCallHierarchyEdge> NormalizeCallHierarchyEdges(
        IEnumerable<WorkflowCallHierarchyEdge>? edges)
    {
        return (edges ?? [])
            .Where(edge =>
                edge is not null &&
                !string.IsNullOrWhiteSpace(edge.FromSymbol) &&
                !string.IsNullOrWhiteSpace(edge.ToSymbol))
            .Select(edge => new WorkflowCallHierarchyEdge
            {
                FromSymbol = edge.FromSymbol.Trim(),
                ToSymbol = edge.ToSymbol.Trim(),
                Kind = string.IsNullOrWhiteSpace(edge.Kind) ? "Invokes" : edge.Kind.Trim(),
                Reason = NormalizeOptionalValue(edge.Reason)
            })
            .DistinctBy(
                edge => $"{edge.FromSymbol}|{edge.ToSymbol}|{edge.Kind}",
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeRelativePaths(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => NormalizeRelativePath(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeNames(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int NormalizeInteger(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string SlugifyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workflow-profile";
        }

        var chars = value.Trim().Select(ch =>
        {
            if (char.IsLetterOrDigit(ch))
            {
                return char.ToLowerInvariant(ch);
            }

            return '-';
        }).ToArray();

        var result = new string(chars).Trim('-');
        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(result) ? "workflow-profile" : result;
    }
}
