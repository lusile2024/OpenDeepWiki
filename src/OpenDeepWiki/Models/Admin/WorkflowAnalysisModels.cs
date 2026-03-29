using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Models.Admin;

public sealed class WorkflowTemplateAugmentRequest
{
    public bool ApplyToDraftVersion { get; set; } = true;
}

public sealed class CreateWorkflowAnalysisSessionRequest
{
    public string? ChapterKey { get; set; }

    public string? Objective { get; set; }
}

public sealed class WorkflowLspAugmentResultDto
{
    public string ProfileKey { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

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

public sealed class WorkflowTemplateAugmentResultDto
{
    public WorkflowLspAugmentResultDto Augment { get; set; } = new();

    public WorkflowTemplateSessionDetailDto Session { get; set; } = new();

    public int? CreatedVersionNumber { get; set; }
}

public class WorkflowAnalysisSessionSummaryDto
{
    public string AnalysisSessionId { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string WorkflowTemplateSessionId { get; set; } = string.Empty;

    public string? ProfileKey { get; set; }

    public int? DraftVersionNumber { get; set; }

    public string? ChapterKey { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Objective { get; set; }

    public string? Summary { get; set; }

    public int TotalTasks { get; set; }

    public int CompletedTasks { get; set; }

    public int FailedTasks { get; set; }

    public int PendingTaskCount { get; set; }

    public int RunningTaskCount { get; set; }

    public string? CurrentTaskId { get; set; }

    public string? ProgressMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime LastActivityAt { get; set; }
}

public sealed class WorkflowAnalysisSessionDetailDto : WorkflowAnalysisSessionSummaryDto
{
    public List<WorkflowAnalysisTaskDto> Tasks { get; set; } = [];

    public List<WorkflowAnalysisArtifactDto> Artifacts { get; set; } = [];

    public List<WorkflowAnalysisLogDto> RecentLogs { get; set; } = [];
}

public sealed class WorkflowAnalysisTaskDto
{
    public string Id { get; set; } = string.Empty;

    public string? ParentTaskId { get; set; }

    public int SequenceNumber { get; set; }

    public int Depth { get; set; }

    public string TaskType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public List<string> FocusSymbols { get; set; } = [];

    public List<string> FocusFiles { get; set; } = [];

    public Dictionary<string, string> Metadata { get; set; } = [];

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class WorkflowAnalysisArtifactDto
{
    public string Id { get; set; } = string.Empty;

    public string? TaskId { get; set; }

    public string ArtifactType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ContentFormat { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class WorkflowAnalysisLogDto
{
    public string Id { get; set; } = string.Empty;

    public string? TaskId { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
