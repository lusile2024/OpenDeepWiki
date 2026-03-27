using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Models.Admin;

public sealed class CreateWorkflowTemplateSessionRequest
{
    public string? BranchId { get; set; }

    public string? LanguageCode { get; set; }

    public string? Title { get; set; }
}

public sealed class WorkflowTemplateMessageRequest
{
    public string Content { get; set; } = string.Empty;
}

public class WorkflowTemplateSessionSummaryDto
{
    public string SessionId { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? BranchId { get; set; }

    public string? BranchName { get; set; }

    public string? LanguageCode { get; set; }

    public string? CurrentDraftKey { get; set; }

    public string? CurrentDraftName { get; set; }

    public int CurrentVersionNumber { get; set; }

    public int? AdoptedVersionNumber { get; set; }

    public int MessageCount { get; set; }

    public DateTime LastActivityAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class WorkflowTemplateSessionDetailDto : WorkflowTemplateSessionSummaryDto
{
    public WorkflowTemplateSessionContextDto? Context { get; set; }

    public RepositoryWorkflowProfile? CurrentDraft { get; set; }

    public List<WorkflowTemplateMessageDto> Messages { get; set; } = [];

    public List<WorkflowTemplateDraftVersionDto> Versions { get; set; } = [];
}

public sealed class WorkflowTemplateSessionContextDto
{
    public string RepositoryName { get; set; } = string.Empty;

    public string? BranchName { get; set; }

    public string? LanguageCode { get; set; }

    public string? PrimaryLanguage { get; set; }

    public string? SourceLocation { get; set; }

    public string DirectoryPreview { get; set; } = string.Empty;

    public List<WorkflowTemplateDiscoveryCandidateDto> DiscoveryCandidates { get; set; } = [];
}

public sealed class WorkflowTemplateDiscoveryCandidateDto
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> TriggerPoints { get; set; } = [];

    public List<string> CompensationTriggerPoints { get; set; } = [];

    public List<string> RequestEntities { get; set; } = [];

    public List<string> SchedulerFiles { get; set; } = [];

    public List<string> ExecutorFiles { get; set; } = [];

    public List<string> EvidenceFiles { get; set; } = [];
}

public sealed class WorkflowTemplateMessageDto
{
    public string Id { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int? VersionNumber { get; set; }

    public string? ChangeSummary { get; set; }

    public DateTime MessageTimestamp { get; set; }
}

public sealed class WorkflowTemplateDraftVersionDto
{
    public string Id { get; set; } = string.Empty;

    public int VersionNumber { get; set; }

    public int? BasedOnVersionNumber { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string? ChangeSummary { get; set; }

    public List<string> RiskNotes { get; set; } = [];

    public List<string> EvidenceFiles { get; set; } = [];

    public List<string> ValidationIssues { get; set; } = [];

    public RepositoryWorkflowProfile Draft { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

public sealed class WorkflowTemplateAdoptResultDto
{
    public WorkflowTemplateSessionDetailDto Session { get; set; } = new();

    public RepositoryWorkflowConfig SavedConfig { get; set; } = new();
}
