using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowDiscoveryService
{
    Task<WorkflowDiscoveryResult> DiscoverAsync(
        RepositoryWorkspace workspace,
        string? profileKey = null,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowDiscoveryResult
{
    public WorkflowSemanticGraph Graph { get; init; } = new();

    public List<WorkflowTopicCandidate> Candidates { get; init; } = [];
}
