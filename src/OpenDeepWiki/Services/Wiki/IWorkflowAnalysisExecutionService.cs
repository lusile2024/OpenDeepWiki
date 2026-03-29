namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisExecutionService
{
    Task ExecuteAsync(string analysisSessionId, CancellationToken cancellationToken = default);
}
