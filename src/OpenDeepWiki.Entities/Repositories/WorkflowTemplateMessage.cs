using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class WorkflowTemplateMessage : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string SessionId { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    [Required]
    [StringLength(20)]
    public string Role { get; set; } = "User";

    [Required]
    public string Content { get; set; } = string.Empty;

    public int? VersionNumber { get; set; }

    [StringLength(1000)]
    public string? ChangeSummary { get; set; }

    public DateTime MessageTimestamp { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual WorkflowTemplateSession Session { get; set; } = null!;
}
