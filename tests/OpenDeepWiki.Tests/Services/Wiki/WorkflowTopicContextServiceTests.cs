using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowTopicContextServiceTests
{
    [Fact]
    public async Task UpsertWorkflowContextsAsync_ShouldUpsertByBranchLanguageAndCatalogPath()
    {
        var factory = CreateContextFactory();
        var service = new WorkflowTopicContextService(factory);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = CreateCandidate("container-pallet-inbound", "Container Pallet Inbound", "v1")
                }
            ]);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = CreateCandidate("container-pallet-inbound", "Container Pallet Inbound", "v2")
                }
            ]);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.DocTopicContexts.CountAsync());

        var candidate = await service.GetWorkflowCandidateAsync(
            "branch-language-1",
            "business-workflows/container-pallet-inbound");

        Assert.NotNull(candidate);
        Assert.Equal("v2", candidate!.Summary);
    }

    [Fact]
    public async Task ClearBranchAsync_ShouldRemoveAllTopicContextsForBranchLanguage()
    {
        var factory = CreateContextFactory();
        var service = new WorkflowTopicContextService(factory);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = CreateCandidate("container-pallet-inbound", "Container Pallet Inbound", "v1")
                }
            ]);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-2",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/inventory-inbound",
                    Candidate = CreateCandidate("inventory-inbound", "Inventory Inbound", "v1")
                }
            ]);

        await service.ClearBranchAsync("branch-language-1");

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.DocTopicContexts.CountAsync());
        Assert.NotNull(await service.GetWorkflowCandidateAsync("branch-language-2", "business-workflows/inventory-inbound"));
        Assert.Null(await service.GetWorkflowCandidateAsync("branch-language-1", "business-workflows/container-pallet-inbound"));
    }

    [Fact]
    public async Task GetWorkflowCandidateAsync_ShouldDeserializeTypedCandidate()
    {
        var factory = CreateContextFactory();
        var service = new WorkflowTopicContextService(factory);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = new WorkflowTopicCandidate
                    {
                        Key = "container-pallet-inbound",
                        Name = "Container Pallet Inbound",
                        Summary = "Container pallet inbound workflow",
                        Actors = ["WcsInboundController"],
                        TriggerPoints = ["WcsInboundController"],
                        CompensationTriggerPoints = ["LogExternalInterfaceController"],
                        RequestEntities = ["ContainerPalletInboundRequest"],
                        SchedulerFiles = ["Workers/InboundRequestScanWorker.cs"],
                        ExecutorFiles = ["Executors/ContainerPalletInboundExecutor.cs"],
                        ServiceFiles = ["Services/ContainerPalletInboundService.cs"],
                        EvidenceFiles = ["Controllers/WcsInboundController.cs"],
                        SeedQueries = ["ContainerPalletInboundRequest"],
                        ExternalSystems = ["WcsClient"],
                        StateFields = ["Status"]
                    }
                }
            ]);

        var candidate = await service.GetWorkflowCandidateAsync(
            "branch-language-1",
            "business-workflows/container-pallet-inbound");

        Assert.NotNull(candidate);
        Assert.Equal("container-pallet-inbound", candidate!.Key);
        Assert.Contains("WcsClient", candidate.ExternalSystems);
        Assert.Contains("LogExternalInterfaceController", candidate.CompensationTriggerPoints);
        Assert.Contains("Status", candidate.StateFields);
    }

    [Fact]
    public async Task GetWorkflowCandidateAsync_ShouldSupportParallelReads()
    {
        var factory = CreateContextFactory();
        var service = new WorkflowTopicContextService(factory);

        await service.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = CreateCandidate("container-pallet-inbound", "Container Pallet Inbound", "v1")
                }
            ]);

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => service.GetWorkflowCandidateAsync(
                "branch-language-1",
                "business-workflows/container-pallet-inbound"));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, candidate =>
        {
            Assert.NotNull(candidate);
            Assert.Equal("container-pallet-inbound", candidate!.Key);
        });
    }

    private static WorkflowTopicCandidate CreateCandidate(string key, string name, string summary)
    {
        return new WorkflowTopicCandidate
        {
            Key = key,
            Name = name,
            Summary = summary,
            SeedQueries = [key]
        };
    }

    private static TestContextFactory CreateContextFactory()
    {
        return new TestContextFactory(Guid.NewGuid().ToString());
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options)
    {
    }

    private sealed class TestContextFactory(string databaseName) : IContextFactory
    {
        private readonly DbContextOptions<TestDbContext> _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        public IContext CreateContext()
        {
            return CreateDbContext();
        }

        public TestDbContext CreateDbContext()
        {
            return new TestDbContext(_options);
        }
    }
}
