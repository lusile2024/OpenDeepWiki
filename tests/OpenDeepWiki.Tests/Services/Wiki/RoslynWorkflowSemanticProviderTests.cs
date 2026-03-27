using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class RoslynWorkflowSemanticProviderTests
{
    [Fact]
    public async Task BuildGraphAsync_ShouldDetectWorkflowNodesAndDomainEdges()
    {
        using var sample = WorkflowSemanticSampleBuilder.CreateWmsInboundSample();
        var provider = CreateProvider();

        var graph = await provider.BuildGraphAsync(sample.CreateWorkspace());

        var controller = AssertNode(graph, "WcsInboundController", WorkflowNodeKind.Controller);
        var requestEntity = AssertNode(graph, "ContainerPalletInboundRequest", WorkflowNodeKind.RequestEntity);
        var worker = AssertNode(graph, "InboundRequestScanWorker", WorkflowNodeKind.BackgroundService);
        var executorFactory = AssertNode(graph, "InboundExecutorFactory", WorkflowNodeKind.ExecutorFactory);
        var executor = AssertNode(graph, "ContainerPalletInboundExecutor", WorkflowNodeKind.Executor);

        AssertEdge(graph, controller.Id, requestEntity.Id, WorkflowEdgeKind.Writes);
        AssertEdge(graph, worker.Id, requestEntity.Id, WorkflowEdgeKind.Queries);
        AssertEdge(graph, executorFactory.Id, executor.Id, WorkflowEdgeKind.Dispatches);

        var statusEdges = graph.Edges
            .Where(edge => edge.FromId == executor.Id &&
                           edge.ToId == requestEntity.Id &&
                           edge.Kind == WorkflowEdgeKind.UpdatesStatus)
            .ToList();

        Assert.Contains(statusEdges, edge => EvidenceContains(edge.EvidenceJson, "value", "Processing"));
        Assert.Contains(statusEdges, edge => EvidenceContains(edge.EvidenceJson, "value", "Completed"));
    }

    [Fact]
    public async Task BuildGraphAsync_ShouldLinkRegistrationsAndInvocationChain()
    {
        using var sample = WorkflowSemanticSampleBuilder.CreateWmsInboundSample();
        var provider = CreateProvider();

        var graph = await provider.BuildGraphAsync(sample.CreateWorkspace());

        var registration = AssertNode(graph, "Program", WorkflowNodeKind.Service);
        var repository = AssertNode(graph, "IInboundRequestRepository", WorkflowNodeKind.Repository);
        var worker = AssertNode(graph, "InboundRequestScanWorker", WorkflowNodeKind.BackgroundService);
        var executorFactory = AssertNode(graph, "InboundExecutorFactory", WorkflowNodeKind.ExecutorFactory);
        var executor = AssertNode(graph, "ContainerPalletInboundExecutor", WorkflowNodeKind.Executor);
        var controller = AssertNode(graph, "WcsInboundController", WorkflowNodeKind.Controller);

        AssertEdge(graph, registration.Id, worker.Id, WorkflowEdgeKind.RegisteredBy);
        AssertEdge(graph, registration.Id, executor.Id, WorkflowEdgeKind.RegisteredBy);

        AssertEdge(graph, controller.Id, repository.Id, WorkflowEdgeKind.Invokes);
        AssertEdge(graph, worker.Id, repository.Id, WorkflowEdgeKind.Invokes);
        AssertEdge(graph, worker.Id, executorFactory.Id, WorkflowEdgeKind.Invokes);
        AssertEdge(graph, executor.Id, repository.Id, WorkflowEdgeKind.Invokes);
    }

    [Fact]
    public async Task BuildGraphAsync_ShouldDetectQuartzDispatchAndRequestBodyConsumption()
    {
        using var sample = WorkflowSemanticSampleBuilder.CreateWcsNamedExecutorSample();
        var provider = CreateProvider();

        var graph = await provider.BuildGraphAsync(sample.CreateWorkspace());

        var controller = AssertNode(graph, "WmsJobInterfaceController", WorkflowNodeKind.Controller);
        var service = AssertNode(graph, "IWcsRequestService", WorkflowNodeKind.Service);
        var request = AssertNode(graph, "WcsRequest", WorkflowNodeKind.RequestEntity);
        var job = AssertNode(graph, "WcsRequestWmsExecutorJob", WorkflowNodeKind.HostedService);
        var executorContract = AssertNode(graph, "IWcsRequestExecutor", WorkflowNodeKind.Executor);
        var executor = AssertNode(graph, "WcsStnMoveInExecutor", WorkflowNodeKind.Executor);
        var requestBody = AssertNode(graph, "WcsStnMoveInBody", WorkflowNodeKind.RequestEntity);

        AssertEdge(graph, controller.Id, service.Id, WorkflowEdgeKind.Invokes);
        AssertEdge(graph, job.Id, request.Id, WorkflowEdgeKind.Queries);
        AssertEdge(graph, job.Id, executorContract.Id, WorkflowEdgeKind.Dispatches);
        AssertEdge(graph, executor.Id, executorContract.Id, WorkflowEdgeKind.Implements);
        AssertEdge(graph, executor.Id, requestBody.Id, WorkflowEdgeKind.ConsumesEntity);

        var statusEdges = graph.Edges
            .Where(edge => edge.FromId == executor.Id &&
                           edge.ToId == request.Id &&
                           edge.Kind == WorkflowEdgeKind.UpdatesStatus)
            .ToList();

        Assert.Contains(statusEdges, edge => EvidenceContains(edge.EvidenceJson, "value", "\"Processing\""));
        Assert.Contains(statusEdges, edge => EvidenceContains(edge.EvidenceJson, "value", "TargetStatus"));
    }

    [Fact]
    public async Task BuildGraphAsync_ShouldCaptureDocumentationSummaryServiceKeysAndIgnoreHelperNoise()
    {
        using var sample = WorkflowSemanticSampleBuilder.CreateWcsNamedExecutorSample();
        var provider = CreateProvider();

        var graph = await provider.BuildGraphAsync(sample.CreateWorkspace());

        var executor = AssertNode(graph, "WcsStnMoveInExecutor", WorkflowNodeKind.Executor);
        var helper = AssertNode(graph, "MultipartRequestHelper", WorkflowNodeKind.Unknown);

        Assert.NotNull(helper);

        using var metadata = JsonDocument.Parse(executor.MetadataJson!);
        Assert.Equal("站台移入申请", metadata.RootElement.GetProperty("documentationSummary").GetString());

        var serviceKeys = metadata.RootElement.GetProperty("serviceKeys")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Contains("WcsStnMoveIn.None", serviceKeys);
    }

    private static RoslynWorkflowSemanticProvider CreateProvider()
    {
        return new RoslynWorkflowSemanticProvider(
            new MsBuildWorkspaceBootstrap(NullLogger<MsBuildWorkspaceBootstrap>.Instance),
            NullLogger<RoslynWorkflowSemanticProvider>.Instance);
    }

    private static WorkflowGraphNode AssertNode(
        WorkflowSemanticGraph graph,
        string displayName,
        WorkflowNodeKind expectedKind)
    {
        var node = Assert.Single(graph.Nodes, item => item.DisplayName == displayName);
        Assert.Equal(expectedKind, node.Kind);
        return node;
    }

    private static WorkflowGraphEdge AssertEdge(
        WorkflowSemanticGraph graph,
        string fromId,
        string toId,
        WorkflowEdgeKind expectedKind)
    {
        return Assert.Single(graph.Edges, edge =>
            edge.FromId == fromId &&
            edge.ToId == toId &&
            edge.Kind == expectedKind);
    }

    private static bool EvidenceContains(string? evidenceJson, string key, string expectedValue)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return false;
        }

        using var document = JsonDocument.Parse(evidenceJson);
        return document.RootElement.TryGetProperty(key, out var property) &&
               string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);
    }
}
