namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowDeepAnalysisInput
{
    public RepositoryWorkflowProfile Profile { get; set; } = new();

    public WorkflowSemanticGraph Graph { get; set; } = new();

    public WorkflowChapterProfile? ChapterProfile { get; set; }

    public string? Objective { get; set; }
}

public sealed class WorkflowDeepAnalysisResult
{
    public string Status { get; set; } = "Completed";

    public string Summary { get; set; } = string.Empty;

    public List<WorkflowChapterSlice> ChapterSlices { get; set; } = [];

    public List<WorkflowDeepAnalysisTaskResult> Tasks { get; set; } = [];

    public List<WorkflowDeepAnalysisArtifactResult> Artifacts { get; set; } = [];
}

public sealed class WorkflowDeepAnalysisTaskResult
{
    public string TaskType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Depth { get; set; }

    public List<string> FocusSymbols { get; set; } = [];

    public string Summary { get; set; } = string.Empty;

    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class WorkflowDeepAnalysisArtifactResult
{
    public string ArtifactType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ContentFormat { get; set; } = "markdown";

    public string Content { get; set; } = string.Empty;

    public Dictionary<string, string> Metadata { get; set; } = [];
}
