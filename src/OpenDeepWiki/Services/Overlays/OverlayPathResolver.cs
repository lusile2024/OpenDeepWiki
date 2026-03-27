using System.Text.RegularExpressions;

namespace OpenDeepWiki.Services.Overlays;

internal sealed class OverlayPathResolution
{
    public bool IsOverride { get; init; }
    public string DisplayPath { get; init; } = string.Empty;
    public string? BasePath { get; init; }
    public bool UsedFileNameVariantRemoval { get; init; }
    public bool UsedCodeOverrideSignal { get; init; }
    public bool UsedBaseTypeFileMatch { get; init; }
}

internal static partial class OverlayPathResolver
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateFileNameLookup(IEnumerable<string> relativePaths)
    {
        return relativePaths
            .Select(NormalizeRelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static OverlayPathResolution Resolve(
        string workingDirectory,
        string projectPath,
        string mappedPath,
        string variantKey,
        ISet<string> fileSet,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileNameLookup)
    {
        var candidatePaths = ExpandCandidatePaths(mappedPath, variantKey);
        var matchedBasePath = candidatePaths.FirstOrDefault(fileSet.Contains);

        if (!string.IsNullOrWhiteSpace(matchedBasePath))
        {
            return new OverlayPathResolution
            {
                IsOverride = true,
                DisplayPath = matchedBasePath,
                BasePath = matchedBasePath,
                UsedFileNameVariantRemoval = !string.Equals(matchedBasePath, mappedPath, StringComparison.OrdinalIgnoreCase)
            };
        }

        var baseTypeMatchedPath = TryResolveByBaseTypeFileMatch(
            workingDirectory,
            projectPath,
            mappedPath,
            variantKey,
            fileNameLookup);

        if (!string.IsNullOrWhiteSpace(baseTypeMatchedPath))
        {
            return new OverlayPathResolution
            {
                IsOverride = true,
                DisplayPath = baseTypeMatchedPath,
                BasePath = baseTypeMatchedPath,
                UsedFileNameVariantRemoval = !string.Equals(baseTypeMatchedPath, mappedPath, StringComparison.OrdinalIgnoreCase),
                UsedBaseTypeFileMatch = true
            };
        }

        var preferredPath = candidatePaths[^1];
        if (HasCodeOverrideSignal(workingDirectory, projectPath))
        {
            return new OverlayPathResolution
            {
                IsOverride = true,
                DisplayPath = preferredPath,
                BasePath = preferredPath,
                UsedFileNameVariantRemoval = !string.Equals(preferredPath, mappedPath, StringComparison.OrdinalIgnoreCase),
                UsedCodeOverrideSignal = true
            };
        }

        return new OverlayPathResolution
        {
            IsOverride = false,
            DisplayPath = preferredPath,
            BasePath = null,
            UsedFileNameVariantRemoval = !string.Equals(preferredPath, mappedPath, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? TryResolveByBaseTypeFileMatch(
        string workingDirectory,
        string projectPath,
        string mappedPath,
        string variantKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileNameLookup)
    {
        if (!projectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var declaration = TryReadPrimaryTypeDeclaration(workingDirectory, projectPath);
        if (declaration is null || declaration.BaseTypeNames.Count == 0)
        {
            return null;
        }

        var strippedProjectPath = RemoveVariantKeyFromFileName(projectPath, variantKey);
        var expectedBaseFileName = Path.GetFileName(strippedProjectPath);
        var expectedBaseTypeName = Path.GetFileNameWithoutExtension(expectedBaseFileName);
        if (string.IsNullOrWhiteSpace(expectedBaseFileName) || string.IsNullOrWhiteSpace(expectedBaseTypeName))
        {
            return null;
        }

        if (!declaration.BaseTypeNames.Any(baseType =>
                string.Equals(baseType, expectedBaseTypeName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!fileNameLookup.TryGetValue(expectedBaseFileName, out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        var normalizedProjectPath = NormalizeRelativePath(projectPath);
        var normalizedMappedPath = NormalizeRelativePath(mappedPath);
        var normalizedStrippedProjectPath = NormalizeRelativePath(strippedProjectPath);

        return candidates
            .Where(candidate => !string.Equals(candidate, normalizedProjectPath, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => !ContainsPathSegment(candidate, variantKey))
            .OrderByDescending(candidate => ScoreBasePathCandidate(
                candidate,
                normalizedMappedPath,
                normalizedStrippedProjectPath))
            .ThenBy(candidate => candidate.Count(ch => ch == '/'))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static List<string> ExpandCandidatePaths(string mappedPath, string variantKey)
    {
        var candidates = new List<string>();
        AddDistinct(candidates, NormalizeRelativePath(mappedPath));

        var strippedFileNamePath = RemoveVariantKeyFromFileName(mappedPath, variantKey);
        AddDistinct(candidates, NormalizeRelativePath(strippedFileNamePath));

        return candidates;
    }

    private static void AddDistinct(List<string> paths, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        paths.Add(path);
    }

    private static int ScoreBasePathCandidate(string candidatePath, params string[] references)
    {
        var bestScore = 0;
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            bestScore = Math.Max(bestScore, ScoreBasePathCandidate(candidatePath, reference));
        }

        return bestScore;
    }

    private static int ScoreBasePathCandidate(string candidatePath, string referencePath)
    {
        var candidateSegments = NormalizeRelativePath(candidatePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var referenceSegments = NormalizeRelativePath(referencePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var prefixMatches = 0;
        var maxPrefix = Math.Min(candidateSegments.Length, referenceSegments.Length);
        while (prefixMatches < maxPrefix &&
               candidateSegments[prefixMatches].Equals(referenceSegments[prefixMatches], StringComparison.OrdinalIgnoreCase))
        {
            prefixMatches++;
        }

        var suffixMatches = 0;
        var maxSuffix = Math.Min(candidateSegments.Length, referenceSegments.Length);
        while (suffixMatches < maxSuffix &&
               candidateSegments[candidateSegments.Length - suffixMatches - 1].Equals(
                   referenceSegments[referenceSegments.Length - suffixMatches - 1],
                   StringComparison.OrdinalIgnoreCase))
        {
            suffixMatches++;
        }

        return prefixMatches * 3 + suffixMatches * 5;
    }

    private static bool HasCodeOverrideSignal(string workingDirectory, string projectPath)
    {
        if (!projectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fullPath = Path.Combine(workingDirectory, projectPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(fullPath);
            return PartialKeywordRegex().IsMatch(content) || OverrideKeywordRegex().IsMatch(content);
        }
        catch
        {
            return false;
        }
    }

    private static CSharpTypeDeclaration? TryReadPrimaryTypeDeclaration(string workingDirectory, string projectPath)
    {
        var fullPath = Path.Combine(workingDirectory, projectPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(fullPath);
            var match = TypeDeclarationRegex().Match(content);
            if (!match.Success)
            {
                return null;
            }

            return new CSharpTypeDeclaration
            {
                BaseTypeNames = ParseBaseTypeNames(match.Groups["bases"].Value)
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ParseBaseTypeNames(string baseTypes)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(baseTypes))
        {
            return results;
        }

        foreach (var rawPart in baseTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeTypeName(rawPart);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (results.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(normalized);
        }

        return results;
    }

    private static string NormalizeTypeName(string rawTypeName)
    {
        if (string.IsNullOrWhiteSpace(rawTypeName))
        {
            return string.Empty;
        }

        var normalized = rawTypeName.Trim();
        var genericIndex = normalized.IndexOf('<');
        if (genericIndex >= 0)
        {
            normalized = normalized[..genericIndex];
        }

        var globalAliasIndex = normalized.LastIndexOf("::", StringComparison.Ordinal);
        if (globalAliasIndex >= 0)
        {
            normalized = normalized[(globalAliasIndex + 2)..];
        }

        var namespaceIndex = normalized.LastIndexOf('.');
        if (namespaceIndex >= 0)
        {
            normalized = normalized[(namespaceIndex + 1)..];
        }

        return normalized.Trim();
    }

    private static string RemoveVariantKeyFromFileName(string path, string variantKey)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(variantKey))
        {
            return path;
        }

        var normalized = NormalizeRelativePath(path);
        var lastSlashIndex = normalized.LastIndexOf('/');
        var directory = lastSlashIndex >= 0 ? normalized[..(lastSlashIndex + 1)] : string.Empty;
        var fileName = lastSlashIndex >= 0 ? normalized[(lastSlashIndex + 1)..] : normalized;

        var extension = Path.GetExtension(fileName);
        var fileStem = fileName[..Math.Max(0, fileName.Length - extension.Length)];
        var strippedStem = RemoveVariantKeyFromStem(fileStem, variantKey);
        if (string.Equals(strippedStem, fileStem, StringComparison.Ordinal))
        {
            return normalized;
        }

        return $"{directory}{strippedStem}{extension}";
    }

    private static string RemoveVariantKeyFromStem(string fileStem, string variantKey)
    {
        if (string.IsNullOrWhiteSpace(fileStem) || string.IsNullOrWhiteSpace(variantKey))
        {
            return fileStem;
        }

        var working = fileStem;
        foreach (var pattern in new[]
                 {
                     $"-{variantKey}",
                     $"_{variantKey}",
                     $".{variantKey}",
                     $"{variantKey}-",
                     $"{variantKey}_",
                     $"{variantKey}."
                 })
        {
            var index = working.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                working = working.Remove(index, pattern.Length);
                return NormalizeFileStem(working, fileStem);
            }
        }

        var plainIndex = working.IndexOf(variantKey, StringComparison.OrdinalIgnoreCase);
        if (plainIndex < 0)
        {
            return fileStem;
        }

        working = working.Remove(plainIndex, variantKey.Length);
        return NormalizeFileStem(working, fileStem);
    }

    private static string NormalizeFileStem(string value, string fallback)
    {
        var normalized = value.Trim('-', '_', '.', ' ');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var segments = NormalizeRelativePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    [GeneratedRegex(@"\bpartial\s+(class|record|struct|interface|void)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PartialKeywordRegex();

    [GeneratedRegex(@"\boverride\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OverrideKeywordRegex();

    [GeneratedRegex(
        @"^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|protected|internal|private|abstract|sealed|partial|static|new)\s+)*(?:class|interface|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^>{}\r\n]+>)?(?:\s*:\s*(?<bases>[^{\r\n]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex TypeDeclarationRegex();

    private sealed class CSharpTypeDeclaration
    {
        public List<string> BaseTypeNames { get; init; } = [];
    }
}
