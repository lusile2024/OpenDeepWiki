using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

public class DocTopicContext : AggregateRoot<string>
{
    [Required]
    [StringLength(36)]
    public string BranchLanguageId { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string CatalogPath { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string TopicKind { get; set; } = string.Empty;

    [Required]
    public string ContextJson { get; set; } = string.Empty;

    [ForeignKey("BranchLanguageId")]
    public virtual BranchLanguage? BranchLanguage { get; set; }
}
