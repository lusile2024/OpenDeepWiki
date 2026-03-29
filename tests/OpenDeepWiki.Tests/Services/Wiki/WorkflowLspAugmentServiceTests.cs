using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using OpenDeepWiki.Services.Wiki.Lsp;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowLspAugmentServiceTests
{
    private const string ControllerSymbol = "Cimc.Tianda.Wms.WebApi.Controllers.Wcs.WmsJobInterfaceController";
    private const string JobSymbol = "Cimc.Tianda.Wms.Jobs.Wcs.WcsRequestWmsExecutorJob";
    private const string ExecutorSymbol = "Cimc.Tianda.Wms.Application.Wcs.WcsRequestExecutors.WcsRequestExecutor";
    private const string PluginSymbol = "Cimc.Tianda.Wms.Application.Wcs.WcsRequestExecutors.WcsStnMoveInExecutorPlugin";
    private const string ServiceSymbol = "Cimc.Tianda.Wms.Contracts.Application.Wcs.IWcsRequestExecutor";
    private const string AllocatorSymbol = "Cimc.Tianda.Wms.Domain.Services.Rule.Putaway.IPutawayAllocator";
    private const string StringSymbol = "string";
    private const string EnumerableSymbol = "System.Linq.Enumerable";
    private const string RepositorySymbol = "FreeSql.IBaseRepository<Cimc.Tianda.Wms.Domain.Entities.Inventory.Stock>";
    private const string EntitySymbol = "Cimc.Tianda.Wms.Domain.Entities.Inventory.Stock";
    private const string RequestEntitySymbol = "Cimc.Tianda.Wms.Domain.Entities.Wcs.WcsRequest";
    private const string ListSymbol = "System.Collections.Generic.List<Cimc.Tianda.Wms.Domain.Entities.Inventory.Stock>";

    [Fact]
    public async Task AugmentAsync_ShouldFilterNoiseSymbolsFromFallbackAndKeepBusinessSymbols()
    {
        var externalClient = new Mock<IWorkflowExternalLspClient>(MockBehavior.Strict);
        var service = CreateService(externalClient.Object);

        var result = await service.AugmentAsync(
            CreateWorkspace(),
            CreateProfile(enableLsp: false),
            CreateGraph());

        Assert.Equal("disabled", result.Strategy);
        Assert.Contains(ControllerSymbol, result.SuggestedRootSymbolNames);
        Assert.Contains(JobSymbol, result.SuggestedRootSymbolNames);
        Assert.Contains(ExecutorSymbol, result.SuggestedMustExplainSymbols);
        Assert.Contains(PluginSymbol, result.SuggestedMustExplainSymbols);
        Assert.Contains(ServiceSymbol, result.SuggestedMustExplainSymbols);
        Assert.Contains(AllocatorSymbol, result.SuggestedMustExplainSymbols);

        AssertNoNoise(result.SuggestedRootSymbolNames);
        AssertNoNoise(result.SuggestedMustExplainSymbols);
        Assert.All(result.SuggestedChapterProfiles, chapter =>
        {
            AssertNoNoise(chapter.RootSymbolNames);
            AssertNoNoise(chapter.MustExplainSymbols);
        });

        var branchChapter = Assert.Single(
            result.SuggestedChapterProfiles,
            chapter => string.Equals(chapter.Key, "branch-decisions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ExecutorSymbol, branchChapter.RootSymbolNames);
    }

    [Fact]
    public async Task AugmentAsync_ShouldFilterNoiseSymbolsFromExternalMerge()
    {
        var externalClient = new Mock<IWorkflowExternalLspClient>(MockBehavior.Strict);
        externalClient
            .Setup(item => item.AnalyzeSymbolAsync(
                It.IsAny<WorkflowExternalLspSymbolRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExternalLspSymbolResult
            {
                Attempted = true,
                Success = true,
                Strategy = "external-lsp",
                ServerName = "csharp-ls",
                Definitions =
                [
                    new WorkflowLspResolvedLocation
                    {
                        FilePath = @"D:\repo\src\WcsRequestExecutor.cs",
                        LineNumber = 20,
                        ColumnNumber = 8,
                        Source = "definition"
                    }
                ],
                CallHierarchyEdges =
                [
                    new WorkflowCallHierarchyEdge
                    {
                        FromSymbol = ExecutorSymbol,
                        ToSymbol = AllocatorSymbol,
                        Kind = "outgoing-call"
                    },
                    new WorkflowCallHierarchyEdge
                    {
                        FromSymbol = ExecutorSymbol,
                        ToSymbol = RepositorySymbol,
                        Kind = "outgoing-call"
                    },
                    new WorkflowCallHierarchyEdge
                    {
                        FromSymbol = EnumerableSymbol,
                        ToSymbol = ExecutorSymbol,
                        Kind = "incoming-call"
                    }
                ],
                SuggestedRootSymbolNames = [EnumerableSymbol, ExecutorSymbol],
                SuggestedMustExplainSymbols = [StringSymbol, RepositorySymbol, EntitySymbol, AllocatorSymbol]
            });

        var service = CreateService(externalClient.Object);

        var result = await service.AugmentAsync(
            CreateWorkspace(),
            CreateProfile(enableLsp: true),
            CreateGraph());

        Assert.Equal("external-lsp", result.Strategy);
        Assert.Contains(ExecutorSymbol, result.SuggestedRootSymbolNames);
        Assert.Contains(AllocatorSymbol, result.SuggestedMustExplainSymbols);
        Assert.DoesNotContain(EnumerableSymbol, result.SuggestedRootSymbolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(StringSymbol, result.SuggestedMustExplainSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(RepositorySymbol, result.SuggestedMustExplainSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(EntitySymbol, result.SuggestedMustExplainSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.All(result.CallHierarchyEdges, edge =>
        {
            Assert.False(IsNoise(edge.FromSymbol));
            Assert.False(IsNoise(edge.ToSymbol));
        });

        Assert.NotEmpty(result.SuggestedChapterProfiles);
        var firstChapter = result.SuggestedChapterProfiles[0];
        AssertNoNoise(firstChapter.RootSymbolNames);
        AssertNoNoise(firstChapter.MustExplainSymbols);
    }

    [Fact]
    public async Task AugmentAsync_ShouldUseBusinessWritersAsPersistenceChapterRoots()
    {
        var externalClient = new Mock<IWorkflowExternalLspClient>(MockBehavior.Strict);
        var service = CreateService(externalClient.Object);

        var result = await service.AugmentAsync(
            CreateWorkspace(),
            CreateProfile(enableLsp: false),
            CreateGraph());

        var persistenceChapter = Assert.Single(
            result.SuggestedChapterProfiles,
            chapter => string.Equals(chapter.Key, "persistence-and-status", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(ExecutorSymbol, persistenceChapter.RootSymbolNames);
        Assert.DoesNotContain(RepositorySymbol, persistenceChapter.RootSymbolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(EntitySymbol, persistenceChapter.RootSymbolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(RequestEntitySymbol, persistenceChapter.RootSymbolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(ListSymbol, persistenceChapter.RootSymbolNames, StringComparer.OrdinalIgnoreCase);
        AssertNoNoise(persistenceChapter.MustExplainSymbols);
    }

    private static WorkflowLspAugmentService CreateService(IWorkflowExternalLspClient externalClient)
    {
        return new WorkflowLspAugmentService(
            externalClient,
            NullLogger<WorkflowLspAugmentService>.Instance);
    }

    private static RepositoryWorkspace CreateWorkspace()
    {
        return new RepositoryWorkspace
        {
            RepositoryId = Guid.NewGuid().ToString(),
            WorkingDirectory = @"D:\repo",
            Organization = "local",
            RepositoryName = "OpenDeepWiki",
            BranchName = "main",
            CommitId = "head"
        };
    }

    private static RepositoryWorkflowProfile CreateProfile(bool enableLsp)
    {
        return new RepositoryWorkflowProfile
        {
            Key = "wcs-stn-move-in",
            Name = "处理站台入库申请",
            EntryRoots = [ControllerSymbol],
            SchedulerNames = [JobSymbol],
            Analysis = new WorkflowProfileAnalysisOptions
            {
                DepthBudget = 4,
                MaxNodes = 32
            },
            LspAssist = new WorkflowLspAssistOptions
            {
                Enabled = enableLsp,
                EnableDefinitionLookup = true,
                EnableReferenceLookup = false,
                IncludeCallHierarchy = true,
                EnablePrepareCallHierarchy = true
            }
        };
    }

    private static WorkflowSemanticGraph CreateGraph()
    {
        return new WorkflowSemanticGraph
        {
            Nodes =
            [
                CreateNode("controller", WorkflowNodeKind.Controller, ControllerSymbol, @"D:\repo\src\Controllers\WmsJobInterfaceController.cs"),
                CreateNode("job", WorkflowNodeKind.HostedService, JobSymbol, @"D:\repo\src\Jobs\WcsRequestWmsExecutorJob.cs"),
                CreateNode("executor", WorkflowNodeKind.Executor, ExecutorSymbol, @"D:\repo\src\Application\WcsRequestExecutor.cs"),
                CreateNode("plugin", WorkflowNodeKind.Handler, PluginSymbol, @"D:\repo\src\Application\WcsStnMoveInExecutorPlugin.cs"),
                CreateNode("service", WorkflowNodeKind.Service, ServiceSymbol, @"D:\repo\src\Contracts\IWcsRequestExecutor.cs"),
                CreateNode("allocator", WorkflowNodeKind.Service, AllocatorSymbol, @"D:\repo\src\Domain\IPutawayAllocator.cs"),
                CreateNode("string", WorkflowNodeKind.Entity, StringSymbol, @"D:\repo\src\Domain\string.cs"),
                CreateNode("enumerable", WorkflowNodeKind.Service, EnumerableSymbol, @"D:\repo\src\Domain\Enumerable.cs"),
                CreateNode("repository", WorkflowNodeKind.Repository, RepositorySymbol, @"D:\repo\src\Domain\StockRepository.cs"),
                CreateNode("entity", WorkflowNodeKind.Entity, EntitySymbol, @"D:\repo\src\Domain\Stock.cs"),
                CreateNode("request", WorkflowNodeKind.RequestEntity, RequestEntitySymbol, @"D:\repo\src\Domain\WcsRequest.cs"),
                CreateNode("list", WorkflowNodeKind.Entity, ListSymbol, @"D:\repo\src\Domain\StockList.cs")
            ],
            Edges =
            [
                CreateEdge("controller", "job", WorkflowEdgeKind.Dispatches),
                CreateEdge("job", "executor", WorkflowEdgeKind.Dispatches),
                CreateEdge("executor", "plugin", WorkflowEdgeKind.Invokes),
                CreateEdge("executor", "service", WorkflowEdgeKind.Invokes),
                CreateEdge("executor", "allocator", WorkflowEdgeKind.Invokes),
                CreateEdge("executor", "repository", WorkflowEdgeKind.Queries),
                CreateEdge("executor", "entity", WorkflowEdgeKind.Writes),
                CreateEdge("executor", "request", WorkflowEdgeKind.UpdatesStatus),
                CreateEdge("executor", "string", WorkflowEdgeKind.Invokes),
                CreateEdge("executor", "enumerable", WorkflowEdgeKind.Invokes),
                CreateEdge("executor", "list", WorkflowEdgeKind.Queries),
                CreateEdge("plugin", "allocator", WorkflowEdgeKind.Invokes)
            ]
        };
    }

    private static WorkflowGraphNode CreateNode(
        string id,
        WorkflowNodeKind kind,
        string symbolName,
        string filePath)
    {
        return new WorkflowGraphNode
        {
            Id = id,
            Kind = kind,
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            SymbolName = symbolName,
            LineNumber = 10,
            ColumnNumber = 5
        };
    }

    private static WorkflowGraphEdge CreateEdge(string fromId, string toId, WorkflowEdgeKind kind)
    {
        return new WorkflowGraphEdge
        {
            FromId = fromId,
            ToId = toId,
            Kind = kind
        };
    }

    private static void AssertNoNoise(IEnumerable<string> symbols)
    {
        Assert.DoesNotContain(symbols, IsNoise);
    }

    private static bool IsNoise(string symbol)
    {
        return string.Equals(symbol, StringSymbol, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(symbol, EnumerableSymbol, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(symbol, RepositorySymbol, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(symbol, EntitySymbol, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(symbol, RequestEntitySymbol, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(symbol, ListSymbol, StringComparison.OrdinalIgnoreCase);
    }
}
