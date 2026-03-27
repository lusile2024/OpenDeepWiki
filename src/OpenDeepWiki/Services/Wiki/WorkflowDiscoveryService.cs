using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowDiscoveryService : IWorkflowDiscoveryService
{
    private readonly IReadOnlyList<IWorkflowSemanticProvider> _providers;
    private readonly WorkflowCandidateExtractor _extractor;
    private readonly IRepositoryWorkflowConfigService _workflowConfigService;
    private readonly ILogger<WorkflowDiscoveryService> _logger;

    public WorkflowDiscoveryService(
        IEnumerable<IWorkflowSemanticProvider> providers,
        WorkflowCandidateExtractor extractor,
        IRepositoryWorkflowConfigService workflowConfigService,
        ILogger<WorkflowDiscoveryService> logger)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _workflowConfigService = workflowConfigService ?? throw new ArgumentNullException(nameof(workflowConfigService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowDiscoveryResult> DiscoverAsync(
        RepositoryWorkspace workspace,
        string? profileKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var provider = _providers.FirstOrDefault(item => item.CanHandle(workspace));
        if (provider is null)
        {
            _logger.LogInformation(
                "No workflow semantic provider could handle repository {Organization}/{Repository}.",
                workspace.Organization,
                workspace.RepositoryName);
            return new WorkflowDiscoveryResult();
        }

        var graph = await provider.BuildGraphAsync(workspace, cancellationToken);
        RepositoryWorkflowProfile? activeProfile = null;
        if (!string.IsNullOrWhiteSpace(workspace.RepositoryId))
        {
            activeProfile = string.IsNullOrWhiteSpace(profileKey)
                ? await _workflowConfigService.GetActiveProfileAsync(workspace.RepositoryId, cancellationToken)
                : await _workflowConfigService.GetProfileAsync(workspace.RepositoryId, profileKey, cancellationToken);
        }

        var candidates = _extractor.ExtractCandidates(graph, activeProfile);
        return new WorkflowDiscoveryResult
        {
            Graph = graph,
            Candidates = candidates
        };
    }
}
