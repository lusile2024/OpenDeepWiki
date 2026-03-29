using System.ComponentModel.DataAnnotations;

namespace OpenDeepWiki.Entities;

public class WorkflowAnalysisSession : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string RepositoryId { get; set; } = string.Empty;

    [Required]
    [StringLength(36)]
    public string WorkflowTemplateSessionId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? BranchId { get; set; }

    [StringLength(200)]
    public string? BranchName { get; set; }

    [StringLength(20)]
    public string? LanguageCode { get; set; }

    [StringLength(64)]
    public string? ProfileKey { get; set; }

    public int? DraftVersionNumber { get; set; }

    [StringLength(120)]
    public string? ChapterKey { get; set; }

    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "Pending";

    [StringLength(1000)]
    public string? Objective { get; set; }

    [StringLength(2000)]
    public string? Summary { get; set; }

    public int TotalTasks { get; set; }

    public int CompletedTasks { get; set; }

    public int FailedTasks { get; set; }

    public int PendingTaskCount { get; set; }

    public int RunningTaskCount { get; set; }

    [StringLength(36)]
    public string? CurrentTaskId { get; set; }

    [StringLength(2000)]
    public string? ProgressMessage { get; set; }

    [StringLength(36)]
    public string? CreatedByUserId { get; set; }

    [StringLength(200)]
    public string? CreatedByUserName { get; set; }

    public DateTime? QueuedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string? MetadataJson { get; set; }

    public virtual Repository? Repository { get; set; }

    public virtual WorkflowTemplateSession? WorkflowTemplateSession { get; set; }

    public virtual ICollection<WorkflowAnalysisTask> Tasks { get; set; } = new List<WorkflowAnalysisTask>();

    public virtual ICollection<WorkflowAnalysisArtifact> Artifacts { get; set; } = new List<WorkflowAnalysisArtifact>();

    public virtual ICollection<WorkflowAnalysisLog> Logs { get; set; } = new List<WorkflowAnalysisLog>();
}
