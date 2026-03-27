using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class WorkflowTemplateDraftVersion : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string SessionId { get; set; } = string.Empty;

    public int VersionNumber { get; set; }

    public int? BasedOnVersionNumber { get; set; }

    [Required]
    [StringLength(20)]
    public string SourceType { get; set; } = "assistant";

    [StringLength(1000)]
    public string? ChangeSummary { get; set; }

    public string DraftJson { get; set; } = string.Empty;

    public string? RiskNotesJson { get; set; }

    public string? EvidenceFilesJson { get; set; }

    public string? ValidationIssuesJson { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual WorkflowTemplateSession Session { get; set; } = null!;
}
