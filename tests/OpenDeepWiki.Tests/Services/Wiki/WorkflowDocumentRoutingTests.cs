using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowDocumentRoutingTests
{
    [Fact]
    public async Task ResolveAsync_ShouldRouteWorkflowCatalogPathToWorkflowPrompt()
    {
        var factory = CreateContextFactory();
        var topicContextService = new WorkflowTopicContextService(factory);
        await topicContextService.UpsertWorkflowContextsAsync(
            "branch-language-1",
            [
                new WorkflowTopicContextItem
                {
                    CatalogPath = "business-workflows/container-pallet-inbound",
                    Candidate = new WorkflowTopicCandidate
                    {
                        Key = "container-pallet-inbound",
                        Name = "容器托盘入库流程",
                        Summary = "workflow"
                    }
                }
            ]);

        var resolver = new WorkflowDocumentRouteResolver(topicContextService);

        var route = await resolver.ResolveAsync("branch-language-1", "business-workflows/container-pallet-inbound");

        Assert.True(route.IsWorkflow);
        Assert.Equal(WorkflowDocumentRouteResolver.WorkflowPromptName, route.PromptName);
        Assert.Equal("container-pallet-inbound", route.Candidate!.Key);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseDefaultPromptForNonWorkflowCatalogPath()
    {
        var factory = CreateContextFactory();
        var resolver = new WorkflowDocumentRouteResolver(new WorkflowTopicContextService(factory));

        var route = await resolver.ResolveAsync("branch-language-1", "architecture");

        Assert.False(route.IsWorkflow);
        Assert.Equal(WorkflowDocumentRouteResolver.DefaultPromptName, route.PromptName);
        Assert.Null(route.Candidate);
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
            return new TestDbContext(_options);
        }
    }
}
