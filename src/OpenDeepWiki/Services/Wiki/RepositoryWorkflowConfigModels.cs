using System.Text.Json.Serialization;

namespace OpenDeepWiki.Services.Wiki;

public sealed class RepositoryWorkflowConfig
{
    public int Version { get; set; } = 1;

    public string? ActiveProfileKey { get; set; }

    public List<RepositoryWorkflowProfile> Profiles { get; set; } = [];
}

public sealed class RepositoryWorkflowProfile
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RepositoryWorkflowProfileMode Mode { get; set; } = RepositoryWorkflowProfileMode.WcsRequestExecutor;

    public List<string> EntryRoots { get; set; } = [];

    public List<string> EntryKinds { get; set; } = [];

    public List<string> AnchorDirectories { get; set; } = [];

    public List<string> AnchorNames { get; set; } = [];

    public List<string> PrimaryTriggerDirectories { get; set; } = [];

    public List<string> CompensationTriggerDirectories { get; set; } = [];

    public List<string> SchedulerDirectories { get; set; } = [];

    public List<string> ServiceDirectories { get; set; } = [];

    public List<string> RepositoryDirectories { get; set; } = [];

    public List<string> PrimaryTriggerNames { get; set; } = [];

    public List<string> CompensationTriggerNames { get; set; } = [];

    public List<string> SchedulerNames { get; set; } = [];

    public List<string> RequestEntityNames { get; set; } = [];

    public List<string> RequestServiceNames { get; set; } = [];

    public List<string> RequestRepositoryNames { get; set; } = [];

    public RepositoryWorkflowProfileSource Source { get; set; } = new();

    public WorkflowDocumentPreferences DocumentPreferences { get; set; } = new();

    public WorkflowProfileAnalysisOptions Analysis { get; set; } = new();

    public List<WorkflowChapterProfile> ChapterProfiles { get; set; } = [];

    public WorkflowLspAssistOptions LspAssist { get; set; } = new();

    public WorkflowAcpOptions Acp { get; set; } = new();
}

public enum RepositoryWorkflowProfileMode
{
    WcsRequestExecutor = 0
}

public sealed class RepositoryWorkflowProfileSource
{
    public string Type { get; set; } = "manual";

    public string? SessionId { get; set; }

    public int? VersionNumber { get; set; }

    public string? UpdatedByUserId { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public sealed class WorkflowDocumentPreferences
{
    public string? WritingHint { get; set; }

    public List<string> PreferredTerms { get; set; } = [];

    public List<string> RequiredSections { get; set; } = [];

    public List<string> AvoidPrimaryTriggerNames { get; set; } = [];
}

public sealed class WorkflowProfileAnalysisOptions
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowProfileAnalysisMode Mode { get; set; } = WorkflowProfileAnalysisMode.Hybrid;

    public List<string> EntryDirectories { get; set; } = [];

    public List<string> RootSymbolNames { get; set; } = [];

    public List<string> MustExplainSymbols { get; set; } = [];

    public List<string> AllowedNamespaces { get; set; } = [];

    public List<string> StopNamespacePrefixes { get; set; } = [];

    public List<string> StopNamePatterns { get; set; } = [];

    public int DepthBudget { get; set; } = 4;

    public int MaxNodes { get; set; } = 48;

    public bool EnableCoverageValidation { get; set; } = true;
}

public enum WorkflowProfileAnalysisMode
{
    Manual = 0,
    Roslyn = 1,
    Hybrid = 2
}

public sealed class WorkflowChapterProfile
{
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowChapterAnalysisMode AnalysisMode { get; set; } = WorkflowChapterAnalysisMode.Standard;

    public List<string> RootSymbolNames { get; set; } = [];

    public List<string> MustExplainSymbols { get; set; } = [];

    public List<string> RequiredSections { get; set; } = [];

    public List<string> OutputArtifacts { get; set; } = [];

    public int DepthBudget { get; set; } = 3;

    public int MaxNodes { get; set; } = 28;

    public bool IncludeFlowchart { get; set; } = true;

    public bool IncludeMindmap { get; set; } = true;
}

public enum WorkflowChapterAnalysisMode
{
    Standard = 0,
    Deep = 1
}

public sealed class WorkflowLspAssistOptions
{
    public bool Enabled { get; set; } = true;

    public string? PreferredServer { get; set; }

    public bool IncludeCallHierarchy { get; set; } = true;

    public int RequestTimeoutMs { get; set; } = 10000;

    public bool EnableDefinitionLookup { get; set; } = true;

    public bool EnableReferenceLookup { get; set; } = false;

    public bool EnablePrepareCallHierarchy { get; set; } = true;

    public List<string> AdditionalEntrySymbolHints { get; set; } = [];

    public List<string> SuggestedEntryDirectories { get; set; } = [];

    public List<string> SuggestedRootSymbolNames { get; set; } = [];

    public List<string> SuggestedMustExplainSymbols { get; set; } = [];

    public List<WorkflowCallHierarchyEdge> CallHierarchyEdges { get; set; } = [];

    public DateTime? LastAugmentedAt { get; set; }
}

public sealed class WorkflowCallHierarchyEdge
{
    public string FromSymbol { get; set; } = string.Empty;

    public string ToSymbol { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

public sealed class WorkflowAcpOptions
{
    public bool Enabled { get; set; } = true;

    public string Objective { get; set; } = "深挖业务流主线与分支";

    public int MaxBranchTasks { get; set; } = 4;

    public int MaxParallelTasks { get; set; } = 2;

    public string SplitStrategy { get; set; } = "by-chapter-and-branch";

    public bool GenerateMindMapSeed { get; set; } = true;

    public bool GenerateFlowchartSeed { get; set; } = true;
}
