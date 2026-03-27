namespace OpenDeepWiki.Services.Overlays;

public sealed class OverlaySuggestRequest
{
    public string? UserIntent { get; set; }
    public string? BaseBranchName { get; set; }
    public int MaxVariants { get; set; } = 3;
    public int MaxSamplesPerVariant { get; set; } = 8;
}

public sealed class OverlaySuggestResponse
{
    public string RepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string BaseBranchName { get; set; } = "main";
    public string DetectedPrimaryLanguage { get; set; } = string.Empty;
    public bool UsedAi { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ReasoningSummary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public OverlayRepositoryStructureAnalysis Analysis { get; set; } = new();
    public RepositoryOverlayConfig SuggestedConfig { get; set; } = new();
}

public sealed class OverlayRepositoryStructureAnalysis
{
    public List<string> SuggestedRoots { get; set; } = [];
    public List<string> SuggestedIncludeGlobs { get; set; } = [];
    public List<OverlayVariantCandidate> VariantCandidates { get; set; } = [];
}

public sealed class OverlayVariantCandidate
{
    public string Key { get; set; } = string.Empty;
    public string SuggestedName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int OverrideCount { get; set; }
    public int AddedCount { get; set; }
    public List<string> Roots { get; set; } = [];
    public List<OverlayOverrideSample> OverrideSamples { get; set; } = [];
    public List<OverlayAddedSample> AddedSamples { get; set; } = [];
}

public sealed class OverlayOverrideSample
{
    public string Root { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
}

public sealed class OverlayAddedSample
{
    public string Root { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
}
