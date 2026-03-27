using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ShouldUseFirstCapableProviderAndReturnCandidates()
    {
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                new WorkflowGraphNode
                {
                    Id = "controller",
                    Kind = WorkflowNodeKind.Controller,
                    DisplayName = "WcsInboundController",
                    SymbolName = "WcsInboundController",
                    FilePath = "Controllers/WcsInboundController.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "request",
                    Kind = WorkflowNodeKind.RequestEntity,
                    DisplayName = "ContainerPalletInboundRequest",
                    SymbolName = "ContainerPalletInboundRequest",
                    FilePath = "Domain/ContainerPalletInboundRequest.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "worker",
                    Kind = WorkflowNodeKind.BackgroundService,
                    DisplayName = "InboundRequestScanWorker",
                    SymbolName = "InboundRequestScanWorker",
                    FilePath = "Workers/InboundRequestScanWorker.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "executor",
                    Kind = WorkflowNodeKind.Executor,
                    DisplayName = "ContainerPalletInboundExecutor",
                    SymbolName = "ContainerPalletInboundExecutor",
                    FilePath = "Executors/ContainerPalletInboundExecutor.cs"
                }
            ],
            Edges =
            [
                new WorkflowGraphEdge { FromId = "controller", ToId = "request", Kind = WorkflowEdgeKind.Writes },
                new WorkflowGraphEdge { FromId = "worker", ToId = "request", Kind = WorkflowEdgeKind.Queries },
                new WorkflowGraphEdge { FromId = "worker", ToId = "executor", Kind = WorkflowEdgeKind.Dispatches }
            ]
        };

        var skippedProvider = new StubWorkflowSemanticProvider(canHandle: false, graph);
        var activeProvider = new StubWorkflowSemanticProvider(canHandle: true, graph);
        var fallbackProvider = new StubWorkflowSemanticProvider(canHandle: true, graph);
        var service = new WorkflowDiscoveryService(
            [skippedProvider, activeProvider, fallbackProvider],
            new WorkflowCandidateExtractor(),
            new StubRepositoryWorkflowConfigService(),
            NullLogger<WorkflowDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(CreateWorkspace());

        Assert.Equal(1, activeProvider.BuildGraphCalls);
        Assert.Equal(0, fallbackProvider.BuildGraphCalls);
        Assert.Single(result.Candidates);
        Assert.Same(graph, result.Graph);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldReturnEmptyWhenNoProviderCanHandle()
    {
        var provider = new StubWorkflowSemanticProvider(canHandle: false, new WorkflowSemanticGraph());
        var service = new WorkflowDiscoveryService(
            [provider],
            new WorkflowCandidateExtractor(),
            new StubRepositoryWorkflowConfigService(),
            NullLogger<WorkflowDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(CreateWorkspace());

        Assert.Empty(result.Graph.Nodes);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldApplyRepositoryWorkflowProfileToCustomDirectories()
    {
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                new WorkflowGraphNode
                {
                    Id = "controller",
                    Kind = WorkflowNodeKind.Controller,
                    DisplayName = "WmsJobInterfaceController",
                    SymbolName = "WmsJobInterfaceController",
                    FilePath = "src/Adapters/WcsControllers/WmsJobInterfaceController.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "service",
                    Kind = WorkflowNodeKind.Service,
                    DisplayName = "WcsRequestService",
                    SymbolName = "WcsRequestService",
                    FilePath = "src/Application/Wcs/WcsRequestService.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "request",
                    Kind = WorkflowNodeKind.RequestEntity,
                    DisplayName = "WcsRequest",
                    SymbolName = "WcsRequest",
                    FilePath = "src/Domain/WcsRequest.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "job",
                    Kind = WorkflowNodeKind.HostedService,
                    DisplayName = "WcsRequestWmsExecutorJob",
                    SymbolName = "WcsRequestWmsExecutorJob",
                    FilePath = "src/Schedulers/WcsJobs/WcsRequestWmsExecutorJob.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "executor-contract",
                    Kind = WorkflowNodeKind.Executor,
                    DisplayName = "IWcsRequestExecutor",
                    SymbolName = "IWcsRequestExecutor",
                    FilePath = "src/Application/Wcs/IWcsRequestExecutor.cs",
                    MetadataJson = """{"typeKind":"Interface","isAbstract":false,"isInterface":true}"""
                },
                new WorkflowGraphNode
                {
                    Id = "executor",
                    Kind = WorkflowNodeKind.Executor,
                    DisplayName = "LocExceptionRecoverExecutor",
                    SymbolName = "LocExceptionRecoverExecutor",
                    FilePath = "src/Features/WcsExecutors/LocExceptionRecoverExecutor.cs",
                    MetadataJson = """{"typeKind":"Class","isAbstract":false,"isInterface":false,"documentationSummary":"货位异常恢复","serviceKeys":["LocExceptionRecover.None"]}"""
                },
                new WorkflowGraphNode
                {
                    Id = "body",
                    Kind = WorkflowNodeKind.RequestEntity,
                    DisplayName = "LocExceptionRecoverBody",
                    SymbolName = "LocExceptionRecoverBody",
                    FilePath = "src/Contracts/LocExceptionRecoverBody.cs"
                }
            ],
            Edges =
            [
                new WorkflowGraphEdge { FromId = "controller", ToId = "service", Kind = WorkflowEdgeKind.Invokes },
                new WorkflowGraphEdge { FromId = "service", ToId = "request", Kind = WorkflowEdgeKind.Writes },
                new WorkflowGraphEdge { FromId = "job", ToId = "request", Kind = WorkflowEdgeKind.Queries },
                new WorkflowGraphEdge { FromId = "job", ToId = "executor-contract", Kind = WorkflowEdgeKind.Dispatches },
                new WorkflowGraphEdge { FromId = "executor", ToId = "executor-contract", Kind = WorkflowEdgeKind.Implements },
                new WorkflowGraphEdge { FromId = "executor", ToId = "body", Kind = WorkflowEdgeKind.ConsumesEntity },
                new WorkflowGraphEdge { FromId = "executor", ToId = "request", Kind = WorkflowEdgeKind.UpdatesStatus, EvidenceJson = """{"member":"Status","value":"Completed"}""" }
            ]
        };

        var provider = new StubWorkflowSemanticProvider(canHandle: true, graph);
        var service = new WorkflowDiscoveryService(
            [provider],
            new WorkflowCandidateExtractor(),
            new StubRepositoryWorkflowConfigService(
                new RepositoryWorkflowProfile
                {
                    Key = "custom-wcs",
                    Name = "自定义 WCS 流程",
                    Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
                    AnchorDirectories = ["src/Features/WcsExecutors"],
                    PrimaryTriggerDirectories = ["src/Adapters/WcsControllers"],
                    SchedulerDirectories = ["src/Schedulers/WcsJobs"],
                    PrimaryTriggerNames = ["WmsJobInterfaceController"],
                    SchedulerNames = ["WcsRequestWmsExecutorJob"],
                    RequestEntityNames = ["WcsRequest"],
                    RequestServiceNames = ["WcsRequestService"],
                    RequestRepositoryNames = ["IWcsRequestRepository"]
                }),
            NullLogger<WorkflowDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(CreateWorkspace(repositoryId: "repo-1"));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("loc-exception-recover", candidate.Key);
        Assert.Equal("货位异常恢复", candidate.Name);
        Assert.Contains("WmsJobInterfaceController", candidate.TriggerPoints);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldUseSpecifiedProfileKeyEvenWhenProfileIsDisabled()
    {
        var graph = new WorkflowSemanticGraph
        {
            Nodes =
            [
                new WorkflowGraphNode
                {
                    Id = "controller",
                    Kind = WorkflowNodeKind.Controller,
                    DisplayName = "WmsJobInterfaceController",
                    SymbolName = "WmsJobInterfaceController",
                    FilePath = "src/Adapters/WcsControllers/WmsJobInterfaceController.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "request",
                    Kind = WorkflowNodeKind.RequestEntity,
                    DisplayName = "WcsRequest",
                    SymbolName = "WcsRequest",
                    FilePath = "src/Domain/WcsRequest.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "job",
                    Kind = WorkflowNodeKind.HostedService,
                    DisplayName = "WcsRequestWmsExecutorJob",
                    SymbolName = "WcsRequestWmsExecutorJob",
                    FilePath = "src/Schedulers/WcsJobs/WcsRequestWmsExecutorJob.cs"
                },
                new WorkflowGraphNode
                {
                    Id = "executor-contract",
                    Kind = WorkflowNodeKind.Executor,
                    DisplayName = "IWcsRequestExecutor",
                    SymbolName = "IWcsRequestExecutor",
                    FilePath = "src/Application/Wcs/IWcsRequestExecutor.cs",
                    MetadataJson = """{"typeKind":"Interface","isAbstract":false,"isInterface":true}"""
                },
                new WorkflowGraphNode
                {
                    Id = "executor",
                    Kind = WorkflowNodeKind.Executor,
                    DisplayName = "LocExceptionRecoverExecutor",
                    SymbolName = "LocExceptionRecoverExecutor",
                    FilePath = "src/Features/WcsExecutors/LocExceptionRecoverExecutor.cs",
                    MetadataJson = """{"typeKind":"Class","isAbstract":false,"isInterface":false,"documentationSummary":"货位异常恢复","serviceKeys":["LocExceptionRecover.None"]}"""
                }
            ],
            Edges =
            [
                new WorkflowGraphEdge { FromId = "controller", ToId = "request", Kind = WorkflowEdgeKind.Writes },
                new WorkflowGraphEdge { FromId = "job", ToId = "request", Kind = WorkflowEdgeKind.Queries },
                new WorkflowGraphEdge { FromId = "job", ToId = "executor-contract", Kind = WorkflowEdgeKind.Dispatches },
                new WorkflowGraphEdge { FromId = "executor", ToId = "executor-contract", Kind = WorkflowEdgeKind.Implements }
            ]
        };

        var provider = new StubWorkflowSemanticProvider(canHandle: true, graph);
        var activeProfile = new RepositoryWorkflowProfile
        {
            Key = "other-flow",
            Name = "其他流程",
            Enabled = true,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            AnchorDirectories = ["src/Other"]
        };
        var targetedProfile = new RepositoryWorkflowProfile
        {
            Key = "loc-exception-recover",
            Name = "货位异常恢复",
            Enabled = false,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            AnchorDirectories = ["src/Features/WcsExecutors"],
            PrimaryTriggerDirectories = ["src/Adapters/WcsControllers"],
            SchedulerDirectories = ["src/Schedulers/WcsJobs"],
            PrimaryTriggerNames = ["WmsJobInterfaceController"],
            SchedulerNames = ["WcsRequestWmsExecutorJob"],
            RequestEntityNames = ["WcsRequest"]
        };
        var service = new WorkflowDiscoveryService(
            [provider],
            new WorkflowCandidateExtractor(),
            new StubRepositoryWorkflowConfigService(activeProfile, [targetedProfile]),
            NullLogger<WorkflowDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(CreateWorkspace(repositoryId: "repo-1"), "loc-exception-recover");

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("loc-exception-recover", candidate.Key);
        Assert.Equal("货位异常恢复", candidate.Name);
    }

    private static RepositoryWorkspace CreateWorkspace(string repositoryId = "")
    {
        return new RepositoryWorkspace
        {
            RepositoryId = repositoryId,
            WorkingDirectory = Path.GetTempPath(),
            Organization = "token",
            RepositoryName = "sample",
            BranchName = "main",
            CommitId = "head"
        };
    }

    private sealed class StubWorkflowSemanticProvider(bool canHandle, WorkflowSemanticGraph graph) : IWorkflowSemanticProvider
    {
        public int BuildGraphCalls { get; private set; }

        public bool CanHandle(RepositoryWorkspace workspace)
        {
            return canHandle;
        }

        public Task<WorkflowSemanticGraph> BuildGraphAsync(
            RepositoryWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            BuildGraphCalls++;
            return Task.FromResult(graph);
        }
    }

    private sealed class StubRepositoryWorkflowConfigService(
        RepositoryWorkflowProfile? activeProfile = null,
        IReadOnlyCollection<RepositoryWorkflowProfile>? extraProfiles = null) : IRepositoryWorkflowConfigService
    {
        public Task<RepositoryWorkflowConfig> GetConfigAsync(string repositoryId, CancellationToken cancellationToken = default)
        {
            var profiles = new List<RepositoryWorkflowProfile>();
            if (activeProfile is not null)
            {
                profiles.Add(activeProfile);
            }

            if (extraProfiles is not null)
            {
                profiles.AddRange(extraProfiles);
            }

            return Task.FromResult(new RepositoryWorkflowConfig
            {
                ActiveProfileKey = activeProfile?.Key,
                Profiles = profiles
            });
        }

        public Task<RepositoryWorkflowProfile?> GetActiveProfileAsync(string repositoryId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(activeProfile);
        }

        public async Task<RepositoryWorkflowProfile?> GetProfileAsync(
            string repositoryId,
            string profileKey,
            CancellationToken cancellationToken = default)
        {
            var config = await GetConfigAsync(repositoryId, cancellationToken);
            return config.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Key, profileKey, StringComparison.OrdinalIgnoreCase));
        }

        public Task<RepositoryWorkflowConfig> SaveConfigAsync(string repositoryId, RepositoryWorkflowConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(config);
        }
    }
}
