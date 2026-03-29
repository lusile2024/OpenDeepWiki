namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowSemanticGraph
{
    public List<WorkflowGraphNode> Nodes { get; init; } = [];

    public List<WorkflowGraphEdge> Edges { get; init; } = [];
}

public sealed class WorkflowGraphNode
{
    public string Id { get; init; } = string.Empty;

    public WorkflowNodeKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string SymbolName { get; init; } = string.Empty;

    public int? LineNumber { get; init; }

    public int? ColumnNumber { get; init; }

    public string? MetadataJson { get; init; }
}

public sealed class WorkflowGraphEdge
{
    public string FromId { get; init; } = string.Empty;

    public string ToId { get; init; } = string.Empty;

    public WorkflowEdgeKind Kind { get; init; }

    public string? EvidenceJson { get; init; }
}

public enum WorkflowNodeKind
{
    Unknown = 0,
    Controller,
    Endpoint,
    BackgroundService,
    HostedService,
    DbContext,
    DbSet,
    Entity,
    RequestEntity,
    Executor,
    ExecutorFactory,
    Handler,
    Service,
    Repository,
    ExternalClient,
    StatusEnum
}

public enum WorkflowEdgeKind
{
    Invokes = 0,
    Implements,
    RegisteredBy,
    Reads,
    Writes,
    Queries,
    Dispatches,
    UpdatesStatus,
    ConsumesEntity,
    ProducesEntity
}
