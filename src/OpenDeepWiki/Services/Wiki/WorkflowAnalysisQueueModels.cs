namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisQueueLease
{
    public string AnalysisSessionId { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string WorkflowTemplateSessionId { get; set; } = string.Empty;

    public string? ProfileKey { get; set; }

    public string? ChapterKey { get; set; }
}
