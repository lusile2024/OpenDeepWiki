namespace OpenDeepWiki.Services.Overlays;

public static class RepositoryOverlayConfigRules
{
    private static readonly string[] DefaultExcludeGlobs =
    [
        "**/bin/**",
        "**/obj/**",
        "**/.git/**",
        "**/node_modules/**",
        "**/.next/**",
        "**/dist/**",
        "**/build/**"
    ];

    public static RepositoryOverlayConfig Sanitize(RepositoryOverlayConfig? config)
    {
        config ??= new RepositoryOverlayConfig();
        config.Version = config.Version <= 0 ? 1 : config.Version;
        config.Profiles ??= [];

        foreach (var profile in config.Profiles)
        {
            profile.Key = string.IsNullOrWhiteSpace(profile.Key)
                ? SlugifyKey(profile.Name)
                : profile.Key.Trim();
            profile.Name = string.IsNullOrWhiteSpace(profile.Name)
                ? profile.Key
                : profile.Name.Trim();
            profile.BaseBranchName = string.IsNullOrWhiteSpace(profile.BaseBranchName)
                ? "main"
                : profile.BaseBranchName.Trim();
            profile.OverlayBranchNameTemplate = string.IsNullOrWhiteSpace(profile.OverlayBranchNameTemplate)
                ? $"overlay/{profile.Key}"
                : profile.OverlayBranchNameTemplate.Trim();

            profile.Roots ??= [];
            profile.Roots = profile.Roots
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => NormalizeRelativePath(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.Variants ??= [];
            foreach (var variant in profile.Variants)
            {
                variant.Key = variant.Key?.Trim() ?? string.Empty;
                variant.Name = string.IsNullOrWhiteSpace(variant.Name) ? null : variant.Name.Trim();
            }

            profile.MappingRules ??= [];
            foreach (var rule in profile.MappingRules)
            {
                rule.Segment = string.IsNullOrWhiteSpace(rule.Segment) ? null : rule.Segment.Trim();
                rule.Pattern = string.IsNullOrWhiteSpace(rule.Pattern) ? null : rule.Pattern.Trim();
                rule.Replacement ??= rule.Type == OverlayMappingRuleType.RegexReplace ? string.Empty : null;
            }

            profile.IncludeGlobs ??= [];
            profile.IncludeGlobs = profile.IncludeGlobs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.ExcludeGlobs ??= [];
            IEnumerable<string> excludeGlobs = profile.ExcludeGlobs.Count == 0
                ? DefaultExcludeGlobs
                : profile.ExcludeGlobs;
            profile.ExcludeGlobs = excludeGlobs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.Generation ??= new OverlayGenerationOptions();
            if (string.IsNullOrWhiteSpace(profile.Generation.DiffMode))
            {
                profile.Generation.DiffMode = "B";
            }

            if (profile.Generation.MaxFiles <= 0)
            {
                profile.Generation.MaxFiles = 200;
            }

            if (profile.Generation.MaxFileBytes <= 0)
            {
                profile.Generation.MaxFileBytes = 200_000;
            }
        }

        config.ActiveProfileKey = string.IsNullOrWhiteSpace(config.ActiveProfileKey)
            ? config.Profiles.FirstOrDefault()?.Key
            : config.ActiveProfileKey.Trim();

        return config;
    }

    public static void Validate(RepositoryOverlayConfig? rawConfig)
    {
        var config = Sanitize(rawConfig);

        if (config.Profiles.Count == 0)
        {
            throw new InvalidOperationException("Overlay config must contain at least one profile.");
        }

        var duplicateProfileKey = config.Profiles
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateProfileKey is not null)
        {
            throw new InvalidOperationException($"Duplicate overlay profile key: '{duplicateProfileKey.Key}'.");
        }

        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Key))
            {
                throw new InvalidOperationException("Overlay profile key cannot be empty.");
            }

            if (profile.Key.Length > 64)
            {
                throw new InvalidOperationException("Overlay profile key is too long.");
            }

            if (profile.Variants.Count == 0)
            {
                throw new InvalidOperationException($"Overlay profile '{profile.Key}' must contain at least one variant.");
            }

            var duplicateVariantKey = profile.Variants
                .Where(v => !string.IsNullOrWhiteSpace(v.Key))
                .GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateVariantKey is not null)
            {
                throw new InvalidOperationException(
                    $"Overlay profile '{profile.Key}' contains duplicate variant key '{duplicateVariantKey.Key}'.");
            }

            foreach (var variant in profile.Variants)
            {
                if (string.IsNullOrWhiteSpace(variant.Key))
                {
                    throw new InvalidOperationException($"Overlay profile '{profile.Key}' has an empty variant key.");
                }
            }

            if (string.IsNullOrWhiteSpace(profile.OverlayBranchNameTemplate) ||
                !profile.OverlayBranchNameTemplate.StartsWith("overlay/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Overlay profile '{profile.Key}' OverlayBranchNameTemplate must start with 'overlay/'.");
            }

            foreach (var rule in profile.MappingRules)
            {
                if (rule.Type == OverlayMappingRuleType.RegexReplace && string.IsNullOrWhiteSpace(rule.Pattern))
                {
                    throw new InvalidOperationException(
                        $"Overlay profile '{profile.Key}' has a RegexReplace rule without a pattern.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(config.ActiveProfileKey) &&
            config.Profiles.All(p => !string.Equals(p.Key, config.ActiveProfileKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ActiveProfileKey does not match any profile.");
        }
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
            return "overlay";
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

        return string.IsNullOrWhiteSpace(result) ? "overlay" : result;
    }
}
