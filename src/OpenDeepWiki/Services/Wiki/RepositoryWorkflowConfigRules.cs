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
        profile.Description = string.IsNullOrWhiteSpace(profile.Description)
            ? null
            : profile.Description.Trim();

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
        profile.Source.SessionId = string.IsNullOrWhiteSpace(profile.Source.SessionId)
            ? null
            : profile.Source.SessionId.Trim();
        profile.Source.UpdatedByUserId = string.IsNullOrWhiteSpace(profile.Source.UpdatedByUserId)
            ? null
            : profile.Source.UpdatedByUserId.Trim();
        profile.Source.UpdatedByUserName = string.IsNullOrWhiteSpace(profile.Source.UpdatedByUserName)
            ? null
            : profile.Source.UpdatedByUserName.Trim();

        profile.DocumentPreferences ??= new WorkflowDocumentPreferences();
        profile.DocumentPreferences.WritingHint = string.IsNullOrWhiteSpace(profile.DocumentPreferences.WritingHint)
            ? null
            : profile.DocumentPreferences.WritingHint.Trim();
        profile.DocumentPreferences.PreferredTerms = NormalizeNames(profile.DocumentPreferences.PreferredTerms);
        profile.DocumentPreferences.RequiredSections = NormalizeNames(profile.DocumentPreferences.RequiredSections);
        profile.DocumentPreferences.AvoidPrimaryTriggerNames = NormalizeNames(profile.DocumentPreferences.AvoidPrimaryTriggerNames);

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
            issues.Add("需要至少指定一个锚点目录或锚点名称，用来锁定具体 executor。");
        }

        if (profile.PrimaryTriggerDirectories.Count == 0 && profile.PrimaryTriggerNames.Count == 0)
        {
            issues.Add("建议补充主入口控制器或入口目录，否则主业务入口容易识别不准。");
        }

        if (profile.SchedulerDirectories.Count == 0 && profile.SchedulerNames.Count == 0)
        {
            issues.Add("建议补充调度任务或扫描任务信息，否则流程中段可能缺失。");
        }

        return issues;
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
