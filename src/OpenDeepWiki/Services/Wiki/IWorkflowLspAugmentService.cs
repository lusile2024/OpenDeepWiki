using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowLspAugmentService
{
    Task<WorkflowLspAugmentResult> AugmentAsync(
        RepositoryWorkspace workspace,
        RepositoryWorkflowProfile profile,
        WorkflowSemanticGraph graph,
        CancellationToken cancellationToken = default);
}
