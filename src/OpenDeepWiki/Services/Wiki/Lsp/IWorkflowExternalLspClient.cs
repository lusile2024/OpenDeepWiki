namespace OpenDeepWiki.Services.Wiki.Lsp;

public interface IWorkflowExternalLspClient
{
    Task<WorkflowExternalLspSymbolResult> AnalyzeSymbolAsync(
        WorkflowExternalLspSymbolRequest request,
        CancellationToken cancellationToken = default);
}
