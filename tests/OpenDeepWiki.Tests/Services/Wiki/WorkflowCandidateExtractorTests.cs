using OpenDeepWiki.Services.Wiki;
using System.Text.Json;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowCandidateExtractorTests
{
    [Fact]
    public void ExtractCandidates_ShouldGroupTriggerToExecutorChainIntoSingleWorkflow()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = CreateContainerPalletInboundGraph(includeExternalClient: false, workflowPrefix: "ContainerPalletInbound");

        var candidates = extractor.ExtractCandidates(graph);

        var candidate = Assert.Single(candidates);
        Assert.Equal("container-pallet-inbound", candidate.Key);
        Assert.Equal("Container Pallet Inbound", candidate.Name);
        Assert.Contains("ContainerPalletInboundRequest", candidate.RequestEntities);
        Assert.Contains("WcsInboundController", candidate.TriggerPoints);
        Assert.Contains("Workers/ContainerPalletInboundRequestScanWorker.cs", candidate.SchedulerFiles);
        Assert.Contains("Executors/ContainerPalletInboundExecutor.cs", candidate.ExecutorFiles);
        Assert.Contains("ContainerPalletInboundRequest", candidate.SeedQueries);
        Assert.Contains("ContainerPalletInboundRequestScanWorker", candidate.SeedQueries);
        Assert.Contains("ContainerPalletInboundExecutor", candidate.SeedQueries);
        Assert.Contains("Status", candidate.StateFields);
    }

    [Fact]
    public void ExtractCandidates_ShouldRejectServiceOnlyClusters()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                CreateNode("service-a", WorkflowNodeKind.Service, "InboundPlanService", "Services/InboundPlanService.cs"),
                CreateNode("service-b", WorkflowNodeKind.Service, "InboundTaskService", "Services/InboundTaskService.cs"),
                CreateNode("service-c", WorkflowNodeKind.Service, "InboundStateService", "Services/InboundStateService.cs")
            ],
            Edges =
            [
                CreateEdge("service-a", "service-b", WorkflowEdgeKind.Invokes),
                CreateEdge("service-b", "service-c", WorkflowEdgeKind.Invokes)
            ]
        };

        var candidates = extractor.ExtractCandidates(graph);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ExtractCandidates_ShouldScoreExternalSignalsHigherThanPureInternalFlow()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = new WorkflowSemanticGraph
        {
            Nodes = [],
            Edges = []
        };

        AddGraph(graph, CreateContainerPalletInboundGraph(includeExternalClient: true, workflowPrefix: "ContainerPalletInbound"));
        AddGraph(graph, CreateContainerPalletInboundGraph(includeExternalClient: false, workflowPrefix: "InventoryInbound"));

        var candidates = extractor.ExtractCandidates(graph);

        var externalCandidate = Assert.Single(candidates, item => item.Key == "container-pallet-inbound");
        var internalCandidate = Assert.Single(candidates, item => item.Key == "inventory-inbound");
        Assert.True(externalCandidate.Score > internalCandidate.Score);
        Assert.Contains("WcsClient", externalCandidate.ExternalSystems);
    }

    [Fact]
    public void ExtractCandidates_ShouldPreferRequestAndExecutorKeywordsOverGenericServiceName()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = CreateContainerPalletInboundGraph(includeExternalClient: false, workflowPrefix: "ContainerPalletInbound");
        var executorNode = Assert.Single(graph.Nodes, node => node.Kind == WorkflowNodeKind.Executor);
        graph.Nodes.Add(CreateNode("generic-service", WorkflowNodeKind.Service, "InboundOrchestrationService", "Services/InboundOrchestrationService.cs"));
        graph.Edges.Add(CreateEdge(executorNode.Id, "generic-service", WorkflowEdgeKind.Invokes));

        var candidates = extractor.ExtractCandidates(graph);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Container Pallet Inbound", candidate.Name);
        Assert.DoesNotContain("Orchestration", candidate.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractCandidates_ShouldSplitSharedRequestBusByConcreteExecutor()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                CreateNode("controller", WorkflowNodeKind.Controller, "WmsJobInterfaceController", "Controllers/Wcs/WmsJobInterfaceController.cs"),
                CreateNode("compensation-controller", WorkflowNodeKind.Controller, "LogExternalInterfaceController", "Controllers/SystemLog/LogExternalInterfaceController.cs"),
                CreateNode("service", WorkflowNodeKind.Service, "WcsRequestService", "Services/WcsRequestService.cs"),
                CreateNode("request", WorkflowNodeKind.RequestEntity, "WcsRequest", "Domain/WcsRequest.cs"),
                CreateNode("job", WorkflowNodeKind.HostedService, "WcsRequestWmsExecutorJob", "Jobs/WcsRequestWmsExecutorJob.cs"),
                CreateNode("contract", WorkflowNodeKind.Executor, "IWcsRequestExecutor", "Executors/IWcsRequestExecutor.cs", typeKind: "Interface", isInterface: true),
                CreateNode(
                    "executor-a",
                    WorkflowNodeKind.Executor,
                    "WcsStnMoveInExecutor",
                    "Wcs/WcsRequestExecutors/WcsStnMoveInExecutor.cs",
                    documentationSummary: "站台移入申请",
                    serviceKeys: ["WcsStnMoveIn.None"]),
                CreateNode("body-a", WorkflowNodeKind.RequestEntity, "WcsStnMoveInBody", "Domain/WcsStnMoveInBody.cs"),
                CreateNode(
                    "executor-b",
                    WorkflowNodeKind.Executor,
                    "LocExceptionRecoverExecutor",
                    "Wcs/WcsRequestExecutors/LocExceptionRecoverExecutor.cs",
                    documentationSummary: "货位异常恢复",
                    serviceKeys: ["LocExceptionRecover.None"]),
                CreateNode("body-b", WorkflowNodeKind.RequestEntity, "LocExceptionRecoverBody", "Domain/LocExceptionRecoverBody.cs"),
                CreateNode("helper", WorkflowNodeKind.Unknown, "MultipartRequestHelper", "Helpers/MultipartRequestHelper.cs")
            ],
            Edges =
            [
                CreateEdge("controller", "service", WorkflowEdgeKind.Invokes),
                CreateEdge("compensation-controller", "service", WorkflowEdgeKind.Invokes),
                CreateEdge("service", "request", WorkflowEdgeKind.Writes),
                CreateEdge("job", "request", WorkflowEdgeKind.Queries),
                CreateEdge("job", "contract", WorkflowEdgeKind.Dispatches),
                CreateEdge("executor-a", "contract", WorkflowEdgeKind.Implements),
                CreateEdge("executor-a", "body-a", WorkflowEdgeKind.ConsumesEntity),
                CreateEdge("executor-a", "request", WorkflowEdgeKind.UpdatesStatus, """{"member":"Status","value":"Processing"}"""),
                CreateEdge("executor-b", "contract", WorkflowEdgeKind.Implements),
                CreateEdge("executor-b", "body-b", WorkflowEdgeKind.ConsumesEntity),
                CreateEdge("executor-b", "request", WorkflowEdgeKind.UpdatesStatus, """{"member":"Status","value":"Completed"}"""),
                CreateEdge("executor-b", "helper", WorkflowEdgeKind.Invokes)
            ]
        };

        var candidates = extractor.ExtractCandidates(graph);

        Assert.Equal(2, candidates.Count);

        var moveInCandidate = Assert.Single(candidates, item => item.Key == "wcs-stn-move-in");
        Assert.Equal("站台移入申请", moveInCandidate.Name);
        Assert.Contains("WmsJobInterfaceController", moveInCandidate.TriggerPoints);
        Assert.Contains("LogExternalInterfaceController", moveInCandidate.CompensationTriggerPoints);
        Assert.Contains("Jobs/WcsRequestWmsExecutorJob.cs", moveInCandidate.SchedulerFiles);
        Assert.Contains("Wcs/WcsRequestExecutors/WcsStnMoveInExecutor.cs", moveInCandidate.ExecutorFiles);
        Assert.DoesNotContain("Wcs/WcsRequestExecutors/LocExceptionRecoverExecutor.cs", moveInCandidate.ExecutorFiles);
        Assert.Contains("WcsStnMoveInBody", moveInCandidate.RequestEntities);

        var locExceptionCandidate = Assert.Single(candidates, item => item.Key == "loc-exception-recover");
        Assert.Equal("货位异常恢复", locExceptionCandidate.Name);
        Assert.Contains("WmsJobInterfaceController", locExceptionCandidate.TriggerPoints);
        Assert.Contains("LogExternalInterfaceController", locExceptionCandidate.CompensationTriggerPoints);
        Assert.Contains("Wcs/WcsRequestExecutors/LocExceptionRecoverExecutor.cs", locExceptionCandidate.ExecutorFiles);
        Assert.DoesNotContain("Wcs/WcsRequestExecutors/WcsStnMoveInExecutor.cs", locExceptionCandidate.ExecutorFiles);
        Assert.Contains("LocExceptionRecoverBody", locExceptionCandidate.RequestEntities);
        Assert.DoesNotContain("MultipartRequestHelper", locExceptionCandidate.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractCandidates_ShouldUseDocumentationSummaryAndServiceKeyInsteadOfHelperNameForWcsProfile()
    {
        var extractor = new WorkflowCandidateExtractor();
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                CreateNode("controller", WorkflowNodeKind.Controller, "WmsJobInterfaceController", "Controllers/Wcs/WmsJobInterfaceController.cs"),
                CreateNode("service", WorkflowNodeKind.Service, "WcsRequestService", "Services/WcsRequestService.cs"),
                CreateNode("request", WorkflowNodeKind.RequestEntity, "WcsRequest", "Domain/WcsRequest.cs"),
                CreateNode("job", WorkflowNodeKind.HostedService, "WcsRequestWmsExecutorJob", "Jobs/WcsRequestWmsExecutorJob.cs"),
                CreateNode("contract", WorkflowNodeKind.Executor, "IWcsRequestExecutor", "Executors/IWcsRequestExecutor.cs", typeKind: "Interface", isInterface: true),
                CreateNode(
                    "executor",
                    WorkflowNodeKind.Executor,
                    "WcsJobErrorLoadError2Executor",
                    "Wcs/WcsRequestExecutors/WcsJobErrorLoadError2Executor.cs",
                    documentationSummary: "任务异常申请-取货异常2：取货外侧占位",
                    serviceKeys: ["WcsJobError.LoadError2"]),
                CreateNode("body", WorkflowNodeKind.RequestEntity, "WcsJobErrorBody", "Domain/WcsJobErrorBody.cs"),
                CreateNode("helper", WorkflowNodeKind.Unknown, "MultipartRequestHelper", "Helpers/MultipartRequestHelper.cs")
            ],
            Edges =
            [
                CreateEdge("controller", "service", WorkflowEdgeKind.Invokes),
                CreateEdge("service", "request", WorkflowEdgeKind.Writes),
                CreateEdge("job", "request", WorkflowEdgeKind.Queries),
                CreateEdge("job", "contract", WorkflowEdgeKind.Dispatches),
                CreateEdge("executor", "contract", WorkflowEdgeKind.Implements),
                CreateEdge("executor", "body", WorkflowEdgeKind.ConsumesEntity),
                CreateEdge("executor", "request", WorkflowEdgeKind.UpdatesStatus, """{"member":"Status","value":"Completed"}"""),
                CreateEdge("executor", "helper", WorkflowEdgeKind.Invokes)
            ]
        };

        var candidate = Assert.Single(extractor.ExtractCandidates(graph));

        Assert.Equal("wcs-job-error-load-error2", candidate.Key);
        Assert.Equal("任务异常申请-取货异常2：取货外侧占位", candidate.Name);
        Assert.DoesNotContain("Multipart", candidate.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("helper", candidate.Key, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowSemanticGraph CreateContainerPalletInboundGraph(bool includeExternalClient, string workflowPrefix)
    {
        var prefix = workflowPrefix.ToLowerInvariant();
        var requestName = $"{workflowPrefix}Request";
        var workerName = $"{workflowPrefix}RequestScanWorker";
        var executorName = $"{workflowPrefix}Executor";
        var serviceName = $"{workflowPrefix}Service";

        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                CreateNode($"{prefix}-controller", WorkflowNodeKind.Controller, "WcsInboundController", "Controllers/WcsInboundController.cs"),
                CreateNode($"{prefix}-request", WorkflowNodeKind.RequestEntity, requestName, $"Domain/{requestName}.cs"),
                CreateNode($"{prefix}-worker", WorkflowNodeKind.BackgroundService, workerName, $"Workers/{workerName}.cs"),
                CreateNode($"{prefix}-factory", WorkflowNodeKind.ExecutorFactory, "InboundExecutorFactory", "Executors/InboundExecutorFactory.cs"),
                CreateNode($"{prefix}-executor", WorkflowNodeKind.Executor, executorName, $"Executors/{executorName}.cs"),
                CreateNode($"{prefix}-service", WorkflowNodeKind.Service, serviceName, $"Services/{serviceName}.cs"),
                CreateNode($"{prefix}-repository", WorkflowNodeKind.Repository, "IInboundRequestRepository", "Domain/IInboundRequestRepository.cs")
            ],
            Edges =
            [
                CreateEdge($"{prefix}-controller", $"{prefix}-repository", WorkflowEdgeKind.Invokes),
                CreateEdge($"{prefix}-controller", $"{prefix}-request", WorkflowEdgeKind.Writes),
                CreateEdge($"{prefix}-worker", $"{prefix}-repository", WorkflowEdgeKind.Invokes),
                CreateEdge($"{prefix}-worker", $"{prefix}-request", WorkflowEdgeKind.Queries),
                CreateEdge($"{prefix}-worker", $"{prefix}-factory", WorkflowEdgeKind.Invokes),
                CreateEdge($"{prefix}-factory", $"{prefix}-executor", WorkflowEdgeKind.Dispatches),
                CreateEdge($"{prefix}-executor", $"{prefix}-service", WorkflowEdgeKind.Invokes),
                CreateEdge($"{prefix}-executor", $"{prefix}-request", WorkflowEdgeKind.UpdatesStatus, """{"member":"Status","value":"Processing"}"""),
                CreateEdge($"{prefix}-executor", $"{prefix}-request", WorkflowEdgeKind.UpdatesStatus, """{"member":"Status","value":"Completed"}""")
            ]
        };

        if (includeExternalClient)
        {
            graph.Nodes.Add(CreateNode($"{prefix}-external", WorkflowNodeKind.ExternalClient, "WcsClient", "Integration/WcsClient.cs"));
            graph.Edges.Add(CreateEdge($"{prefix}-external", $"{prefix}-controller", WorkflowEdgeKind.Invokes));
        }

        return graph;
    }

    private static void AddGraph(WorkflowSemanticGraph target, WorkflowSemanticGraph source)
    {
        target.Nodes.AddRange(source.Nodes);
        target.Edges.AddRange(source.Edges);
    }

    private static WorkflowGraphNode CreateNode(
        string id,
        WorkflowNodeKind kind,
        string displayName,
        string filePath,
        string typeKind = "Class",
        bool isAbstract = false,
        bool isInterface = false,
        string? documentationSummary = null,
        string[]? serviceKeys = null)
    {
        return new WorkflowGraphNode
        {
            Id = id,
            Kind = kind,
            DisplayName = displayName,
            SymbolName = displayName,
            FilePath = filePath,
            MetadataJson = JsonSerializer.Serialize(new
            {
                typeKind,
                isAbstract,
                isInterface,
                documentationSummary,
                serviceKeys = serviceKeys ?? []
            })
        };
    }

    private static WorkflowGraphEdge CreateEdge(string fromId, string toId, WorkflowEdgeKind kind, string? evidenceJson = null)
    {
        return new WorkflowGraphEdge
        {
            FromId = fromId,
            ToId = toId,
            Kind = kind,
            EvidenceJson = evidenceJson
        };
    }
}
