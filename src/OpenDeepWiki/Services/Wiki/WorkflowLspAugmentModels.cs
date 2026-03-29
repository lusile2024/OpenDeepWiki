namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowLspAugmentResult
{
    public string ProfileKey { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Strategy { get; set; } = "roslyn-fallback";

    public string? FallbackReason { get; set; }

    public string? LspServerName { get; set; }

    public List<string> SuggestedEntryDirectories { get; set; } = [];

    public List<string> SuggestedRootSymbolNames { get; set; } = [];

    public List<string> SuggestedMustExplainSymbols { get; set; } = [];

    public List<WorkflowChapterProfile> SuggestedChapterProfiles { get; set; } = [];

    public List<WorkflowCallHierarchyEdge> CallHierarchyEdges { get; set; } = [];

    public List<string> EvidenceFiles { get; set; } = [];

    public List<WorkflowLspDiagnostic> Diagnostics { get; set; } = [];

    public List<WorkflowLspResolvedLocation> ResolvedDefinitions { get; set; } = [];

    public List<WorkflowLspResolvedLocation> ResolvedReferences { get; set; } = [];
}

public sealed class WorkflowLspDiagnostic
{
    public string Level { get; set; } = "info";

    public string Message { get; set; } = string.Empty;
}

public sealed class WorkflowLspResolvedLocation
{
    public string? SymbolName { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int? LineNumber { get; set; }

    public int? ColumnNumber { get; set; }

    public string? Source { get; set; }
}
