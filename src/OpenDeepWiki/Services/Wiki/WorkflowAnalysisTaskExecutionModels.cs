namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisTaskExecutionRequest
{
    public string AnalysisSessionId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Depth { get; set; }

    public string? Summary { get; set; }

    public IReadOnlyList<string> FocusSymbols { get; set; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkflowDeepAnalysisArtifactResult> PlannedArtifacts { get; set; } = [];
}

public sealed class WorkflowAnalysisTaskExecutionResult
{
    public string? Summary { get; set; }

    public List<string> LogMessages { get; set; } = [];

    public List<WorkflowDeepAnalysisArtifactResult> Artifacts { get; set; } = [];
}
