using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class WorkflowAnalysisArtifact : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string AnalysisSessionId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? TaskId { get; set; }

    [Required]
    [StringLength(40)]
    public string ArtifactType { get; set; } = "summary";

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string ContentFormat { get; set; } = "markdown";

    public string Content { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    [ForeignKey(nameof(AnalysisSessionId))]
    public virtual WorkflowAnalysisSession AnalysisSession { get; set; } = null!;

    [ForeignKey(nameof(TaskId))]
    public virtual WorkflowAnalysisTask? Task { get; set; }
}
