namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisTaskRunner
{
    Task<WorkflowAnalysisTaskExecutionResult> ExecuteAsync(
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken = default);
}
