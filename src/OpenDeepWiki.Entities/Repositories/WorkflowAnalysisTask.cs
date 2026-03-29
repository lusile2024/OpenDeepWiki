using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class WorkflowAnalysisTask : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string AnalysisSessionId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? ParentTaskId { get; set; }

    public int SequenceNumber { get; set; }

    public int Depth { get; set; }

    [Required]
    [StringLength(40)]
    public string TaskType { get; set; } = "chapter-analysis";

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "Pending";

    [StringLength(4000)]
    public string? Summary { get; set; }

    public string? FocusSymbolsJson { get; set; }

    public string? FocusFilesJson { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? LastActivityAt { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    [ForeignKey(nameof(AnalysisSessionId))]
    public virtual WorkflowAnalysisSession AnalysisSession { get; set; } = null!;
}
