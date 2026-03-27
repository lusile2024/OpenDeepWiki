using System.ComponentModel.DataAnnotations;

namespace OpenDeepWiki.Entities;

public class WorkflowTemplateSession : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string RepositoryId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? BranchId { get; set; }

    [StringLength(200)]
    public string? BranchName { get; set; }

    [StringLength(20)]
    public string? LanguageCode { get; set; }

    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "Active";

    [StringLength(200)]
    public string? Title { get; set; }

    [StringLength(64)]
    public string? CurrentDraftKey { get; set; }

    [StringLength(200)]
    public string? CurrentDraftName { get; set; }

    public int CurrentVersionNumber { get; set; }

    public int? AdoptedVersionNumber { get; set; }

    public int MessageCount { get; set; }

    [StringLength(36)]
    public string? CreatedByUserId { get; set; }

    [StringLength(200)]
    public string? CreatedByUserName { get; set; }

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public string? ContextJson { get; set; }

    public virtual Repository? Repository { get; set; }

    public virtual ICollection<WorkflowTemplateMessage> Messages { get; set; } = new List<WorkflowTemplateMessage>();

    public virtual ICollection<WorkflowTemplateDraftVersion> Versions { get; set; } = new List<WorkflowTemplateDraftVersion>();
}
