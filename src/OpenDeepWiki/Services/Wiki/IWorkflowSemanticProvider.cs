using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowSemanticProvider
{
    bool CanHandle(RepositoryWorkspace workspace);

    Task<WorkflowSemanticGraph> BuildGraphAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default);
}
