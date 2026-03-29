namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisQueueService
{
    Task<bool> EnqueueAsync(string analysisSessionId, CancellationToken cancellationToken = default);

    Task<WorkflowAnalysisQueueLease?> TryAcquireNextAsync(CancellationToken cancellationToken = default);
}
