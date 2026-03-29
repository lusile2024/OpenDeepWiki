namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowChapterSlice
{
    public string ChapterKey { get; set; } = string.Empty;

    public string ChapterTitle { get; set; } = string.Empty;

    public List<string> RootSymbolNames { get; set; } = [];

    public int NodeCount { get; set; }

    public int EdgeCount { get; set; }

    public List<WorkflowChapterDecisionPoint> DecisionPoints { get; set; } = [];

    public List<WorkflowChapterStateChange> StateChanges { get; set; } = [];

    public List<WorkflowChapterExtensionPoint> ExtensionPoints { get; set; } = [];

    public List<string> MissingMustExplainSymbols { get; set; } = [];

    public List<string> IncludedSymbols { get; set; } = [];

    public List<string> RequiredSections { get; set; } = [];

    public string MindMapSeedMarkdown { get; set; } = string.Empty;

    public string FlowchartSeedMermaid { get; set; } = string.Empty;
}

public sealed class WorkflowChapterDecisionPoint
{
    public string SymbolName { get; set; } = string.Empty;

    public List<string> OutgoingSymbols { get; set; } = [];

    public string Summary { get; set; } = string.Empty;
}

public sealed class WorkflowChapterStateChange
{
    public string FromSymbol { get; set; } = string.Empty;

    public string ToSymbol { get; set; } = string.Empty;

    public string ChangeType { get; set; } = string.Empty;
}

public sealed class WorkflowChapterExtensionPoint
{
    public string SymbolName { get; set; } = string.Empty;

    public string ExtensionType { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}
