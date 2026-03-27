using System.Text.Json.Serialization;

namespace OpenDeepWiki.Services.Overlays;

/// <summary>
/// Repository overlay configuration. Stored as JSON in SystemSetting.
/// The goal is to map "project version" files onto "base version" files and
/// generate a wiki that only shows overrides and additions.
/// </summary>
public sealed class RepositoryOverlayConfig
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Active profile key for generation/preview.
    /// </summary>
    public string? ActiveProfileKey { get; set; }

    public List<OverlayProfile> Profiles { get; set; } = new();
}

public sealed class OverlayProfile
{
    /// <summary>
    /// Stable profile key (slug). Used in branch name template.
    /// Example: "wms-1397", "customer-a", "overlay".
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which real branch we use as the "base snapshot" when the repository source is Git.
    /// Default: "main".
    /// </summary>
    public string BaseBranchName { get; set; } = "main";

    /// <summary>
    /// Branch name template for generated overlay wiki.
    /// Variables: {profileKey}
    /// Default: "overlay/{profileKey}"
    /// </summary>
    public string OverlayBranchNameTemplate { get; set; } = "overlay/{profileKey}";

    /// <summary>
    /// Roots to scan, relative to repository root.
    /// Example: ["src/Domain", "src/Application"]
    /// If empty, scan the whole repository (except exclude globs).
    /// </summary>
    public List<string> Roots { get; set; } = new();

    /// <summary>
    /// Overlay variants. Commonly the project code folder like "1397",
    /// but can be any marker segment.
    /// </summary>
    public List<OverlayVariant> Variants { get; set; } = new();

    /// <summary>
    /// Mapping rules applied sequentially to map a project path to a base path.
    /// </summary>
    public List<OverlayMappingRule> MappingRules { get; set; } = new();

    /// <summary>
    /// Optional include globs (relative paths with forward slashes).
    /// If empty, include all files under Roots.
    /// </summary>
    public List<string> IncludeGlobs { get; set; } = new();

    /// <summary>
    /// Exclude globs.
    /// </summary>
    public List<string> ExcludeGlobs { get; set; } = new()
    {
        "**/bin/**",
        "**/obj/**",
        "**/.git/**",
        "**/node_modules/**",
        "**/.next/**",
        "**/dist/**",
        "**/build/**"
    };

    public OverlayGenerationOptions Generation { get; set; } = new();
}

public sealed class OverlayVariant
{
    /// <summary>
    /// Variant key. For project-code style overlays this is typically the project code "1397".
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Display name (optional). If empty, uses Key.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// How to detect overlay files for this variant.
    /// Default: PathSegmentEquals (any segment equals Key).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayVariantDetectionMode DetectionMode { get; set; } = OverlayVariantDetectionMode.PathSegmentEquals;
}

public enum OverlayVariantDetectionMode
{
    PathSegmentEquals = 0
}

public sealed class OverlayGenerationOptions
{
    /// <summary>
    /// Only generate wiki for overrides and additions.
    /// Kept as an option so future versions can include base-only docs if desired.
    /// </summary>
    public bool OnlyShowProjectChanges { get; set; } = true;

    /// <summary>
    /// Diff mode:
    /// A = summary only
    /// B = summary + key diff snippets (token cost higher)
    /// </summary>
    public string DiffMode { get; set; } = "B";

    /// <summary>
    /// Max files to generate in one request to avoid runaway cost.
    /// </summary>
    public int MaxFiles { get; set; } = 200;

    /// <summary>
    /// Max bytes to read per source file (base/project) before truncation.
    /// </summary>
    public int MaxFileBytes { get; set; } = 200_000;
}

public sealed class OverlayMappingRule
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayMappingRuleType Type { get; set; } = OverlayMappingRuleType.RemoveVariantSegment;

    /// <summary>
    /// For RemoveVariantSegment: remove the first occurrence of this segment.
    /// If null, uses the matched variant key.
    /// </summary>
    public string? Segment { get; set; }

    /// <summary>
    /// For RegexReplace: .NET regex pattern (path uses forward slashes).
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// For RegexReplace: replacement.
    /// </summary>
    public string? Replacement { get; set; }
}

public enum OverlayMappingRuleType
{
    /// <summary>
    /// Remove a path segment that indicates the variant folder (project version marker).
    /// Example: src/Domain/1397/Services/X.cs -> src/Domain/Services/X.cs
    /// </summary>
    RemoveVariantSegment = 0,

    /// <summary>
    /// Apply a regex replacement to the relative path.
    /// </summary>
    RegexReplace = 1
}

