namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowDocumentRouteResolver
{
    public const string DefaultPromptName = "content-generator";
    public const string WorkflowPromptName = "workflow-content-generator";

    private readonly WorkflowTopicContextService _topicContextService;

    public WorkflowDocumentRouteResolver(WorkflowTopicContextService topicContextService)
    {
        _topicContextService = topicContextService ?? throw new ArgumentNullException(nameof(topicContextService));
    }

    public async Task<WorkflowDocumentRoute> ResolveAsync(
        string branchLanguageId,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _topicContextService.GetWorkflowCandidateAsync(
            branchLanguageId,
            catalogPath,
            cancellationToken);

        return candidate is null
            ? new WorkflowDocumentRoute()
            : new WorkflowDocumentRoute
            {
                PromptName = WorkflowPromptName,
                Candidate = candidate
            };
    }
}

public sealed class WorkflowDocumentRoute
{
    public string PromptName { get; init; } = WorkflowDocumentRouteResolver.DefaultPromptName;

    public WorkflowTopicCandidate? Candidate { get; init; }

    public bool IsWorkflow => Candidate is not null;
}
