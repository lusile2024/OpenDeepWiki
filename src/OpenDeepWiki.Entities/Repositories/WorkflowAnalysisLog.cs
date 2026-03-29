using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class WorkflowAnalysisLog : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string AnalysisSessionId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? TaskId { get; set; }

    [Required]
    [StringLength(20)]
    public string Level { get; set; } = "info";

    [Required]
    [StringLength(4000)]
    public string Message { get; set; } = string.Empty;

    [ForeignKey(nameof(AnalysisSessionId))]
    public virtual WorkflowAnalysisSession AnalysisSession { get; set; } = null!;
}
