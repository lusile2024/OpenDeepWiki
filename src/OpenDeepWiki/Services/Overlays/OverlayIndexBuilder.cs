using System.Text.RegularExpressions;

namespace OpenDeepWiki.Services.Overlays;

public sealed class OverlayIndex
{
    public string ProfileKey { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string BaseBranchName { get; init; } = string.Empty;

    /// <summary>
    /// Whether the preview result was capped by maxFiles.
    /// When true, <see cref="UncappedSummary"/> contains the original counts before capping.
    /// </summary>
    public bool IsCapped { get; init; }

    /// <summary>
    /// The maxFiles value applied when building the preview result.
    /// </summary>
    public int? MaxFilesApplied { get; init; }

    /// <summary>
    /// Original counts before capping (if any). Useful for UI hints.
    /// </summary>
    public OverlayIndexSummary? UncappedSummary { get; init; }

    public List<OverlayOverrideItem> Overrides { get; init; } = new();
    public List<OverlayAddedItem> Added { get; init; } = new();

    public OverlayIndexSummary Summary => new()
    {
        OverrideCount = Overrides.Count,
        AddedCount = Added.Count,
        TotalCount = Overrides.Count + Added.Count
    };
}

public sealed class OverlayIndexSummary
{
    public int OverrideCount { get; set; }
    public int AddedCount { get; set; }
    public int TotalCount { get; set; }
}

public sealed class OverlayOverrideItem
{
    public string VariantKey { get; init; } = string.Empty;
    public string VariantName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty; // relative
    public string BasePath { get; init; } = string.Empty; // relative
    public string DisplayPath { get; init; } = string.Empty; // relative, usually equals BasePath
}

public sealed class OverlayAddedItem
{
    public string VariantKey { get; init; } = string.Empty;
    public string VariantName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty; // relative
    public string DisplayPath { get; init; } = string.Empty; // relative
}

public interface IOverlayIndexBuilder
{
    OverlayIndex Build(string repositoryWorkingDirectory, OverlayProfile profile);
}

public sealed class OverlayIndexBuilder : IOverlayIndexBuilder
{
    public OverlayIndex Build(string repositoryWorkingDirectory, OverlayProfile profile)
    {
        if (string.IsNullOrWhiteSpace(repositoryWorkingDirectory))
        {
            throw new ArgumentException("Working directory cannot be empty.", nameof(repositoryWorkingDirectory));
        }

        if (!Directory.Exists(repositoryWorkingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory does not exist: {repositoryWorkingDirectory}");
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var normalizedRoots = profile.Roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeRelativePath)
            .ToList();

        var variants = profile.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Key))
            .ToList();

        if (variants.Count == 0)
        {
            throw new InvalidOperationException("Overlay profile must contain at least one variant.");
        }

        var allFiles = EnumerateRepositoryFiles(repositoryWorkingDirectory)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(repositoryWorkingDirectory, path)))
            .ToList();

        // Precompute a set for quick existence checks.
        var fileSet = new HashSet<string>(allFiles, StringComparer.OrdinalIgnoreCase);
        var fileNameLookup = OverlayPathResolver.CreateFileNameLookup(allFiles);

        var overrides = new List<OverlayOverrideItem>();
        var added = new List<OverlayAddedItem>();

        foreach (var relativePath in allFiles)
        {
            if (!IsWithinRoots(relativePath, normalizedRoots))
            {
                continue;
            }

            if (IsExcluded(relativePath, profile.ExcludeGlobs))
            {
                continue;
            }

            if (profile.IncludeGlobs.Count > 0 && !IsIncluded(relativePath, profile.IncludeGlobs))
            {
                continue;
            }

            var match = TryMatchVariant(relativePath, variants);
            if (match is null)
            {
                continue;
            }

            var (variant, variantSegment) = match.Value;

            var basePath = MapToBasePath(relativePath, profile.MappingRules, variantSegment);
            if (string.IsNullOrWhiteSpace(basePath) || basePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Avoid mapping to another overlay variant path; base should be the base version.
            if (TryMatchVariant(basePath, variants) != null)
            {
                continue;
            }

            var resolution = OverlayPathResolver.Resolve(
                repositoryWorkingDirectory,
                relativePath,
                basePath,
                variant.Key,
                fileSet,
                fileNameLookup);

            if (resolution.IsOverride)
            {
                overrides.Add(new OverlayOverrideItem
                {
                    VariantKey = variant.Key,
                    VariantName = string.IsNullOrWhiteSpace(variant.Name) ? variant.Key : variant.Name!,
                    ProjectPath = relativePath,
                    BasePath = resolution.BasePath ?? resolution.DisplayPath,
                    DisplayPath = resolution.DisplayPath
                });
            }
            else
            {
                added.Add(new OverlayAddedItem
                {
                    VariantKey = variant.Key,
                    VariantName = string.IsNullOrWhiteSpace(variant.Name) ? variant.Key : variant.Name!,
                    ProjectPath = relativePath,
                    DisplayPath = resolution.DisplayPath
                });
            }
        }

        overrides = overrides
            .OrderBy(o => o.VariantKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        added = added
            .OrderBy(a => a.VariantKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OverlayIndex
        {
            ProfileKey = profile.Key,
            ProfileName = profile.Name,
            BaseBranchName = profile.BaseBranchName,
            Overrides = overrides,
            Added = added
        };
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string root)
    {
        // Keep this conservative: skip obvious huge dirs quickly at top-level.
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

    private static bool IsWithinRoots(string relativePath, List<string> roots)
    {
        if (roots.Count == 0)
        {
            return true;
        }

        return roots.Any(root =>
            relativePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static (OverlayVariant Variant, string VariantSegment)? TryMatchVariant(
        string relativePath,
        List<OverlayVariant> variants)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var variant in variants)
        {
            if (variant.DetectionMode == OverlayVariantDetectionMode.PathSegmentEquals)
            {
                if (segments.Any(s => s.Equals(variant.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    return (variant, variant.Key);
                }
            }
        }

        return null;
    }

    private static string MapToBasePath(
        string projectPath,
        List<OverlayMappingRule> rules,
        string variantSegment)
    {
        var mapped = projectPath;
        var appliedAny = false;

        foreach (var rule in rules)
        {
            switch (rule.Type)
            {
                case OverlayMappingRuleType.RemoveVariantSegment:
                {
                    var segment = string.IsNullOrWhiteSpace(rule.Segment) ? variantSegment : rule.Segment!;
                    var next = RemoveFirstPathSegment(mapped, segment);
                    if (!next.Equals(mapped, StringComparison.OrdinalIgnoreCase))
                    {
                        mapped = next;
                        appliedAny = true;
                    }
                    break;
                }
                case OverlayMappingRuleType.RegexReplace:
                {
                    if (string.IsNullOrWhiteSpace(rule.Pattern) || rule.Replacement is null)
                    {
                        break;
                    }

                    var next = Regex.Replace(mapped, rule.Pattern, rule.Replacement, RegexOptions.IgnoreCase);
                    if (!next.Equals(mapped, StringComparison.OrdinalIgnoreCase))
                    {
                        mapped = next;
                        appliedAny = true;
                    }
                    break;
                }
            }
        }

        if (!appliedAny)
        {
            // Default behavior to keep MVP usable: remove the variant segment once.
            mapped = RemoveFirstPathSegment(mapped, variantSegment);
        }

        return NormalizeRelativePath(mapped);
    }

    private static string RemoveFirstPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(segment))
        {
            return path;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var index = parts.FindIndex(p => p.Equals(segment, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return path;
        }

        parts.RemoveAt(index);
        return string.Join("/", parts);
    }

    private static bool IsExcluded(string relativePath, List<string> excludeGlobs)
        => MatchesAnyGlob(relativePath, excludeGlobs);

    private static bool IsIncluded(string relativePath, List<string> includeGlobs)
        => MatchesAnyGlob(relativePath, includeGlobs);

    private static bool MatchesAnyGlob(string relativePath, List<string> globs)
    {
        if (globs.Count == 0)
        {
            return false;
        }

        foreach (var glob in globs)
        {
            if (string.IsNullOrWhiteSpace(glob))
            {
                continue;
            }

            if (GlobToRegex(glob).IsMatch(relativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex GlobToRegex(string glob)
    {
        // A small glob implementation (similar to GitTool).
        // Supports *, **, ?; always uses forward slashes.
        var pattern = "^";
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        pattern += ".*";
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                        }
                    }
                    else
                    {
                        pattern += "[^/]*";
                    }
                    break;
                case '?':
                    pattern += "[^/]";
                    break;
                case '.':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '+':
                case '^':
                case '$':
                case '|':
                case '\\':
                    pattern += "\\" + c;
                    break;
                case '/':
                    pattern += "/";
                    break;
                default:
                    pattern += c;
                    break;
            }
        }

        pattern += "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        normalized = normalized.Replace("//", "/");
        return normalized;
    }
}
