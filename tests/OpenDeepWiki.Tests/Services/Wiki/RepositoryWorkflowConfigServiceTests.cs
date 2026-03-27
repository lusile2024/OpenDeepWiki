using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class RepositoryWorkflowConfigServiceTests
{
    [Fact]
    public async Task SaveConfigAsync_ShouldRoundTripProfilesByRepositoryId()
    {
        var factory = CreateContextFactory();
        var service = CreateService(factory);

        var saved = await service.SaveConfigAsync(
            "repo-1",
            new RepositoryWorkflowConfig
            {
                ActiveProfileKey = "wms-wcs",
                Profiles =
                [
                    new RepositoryWorkflowProfile
                    {
                        Key = "wms-wcs",
                        Name = "WMS/WCS 请求流程",
                        Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
                        AnchorDirectories = ["src/Application/Wcs/WcsRequestExecutors"],
                        PrimaryTriggerDirectories = ["src/WebApi/Controllers/Wcs"],
                        SchedulerDirectories = ["src/Jobs/Wcs"]
                    }
                ]
            });

        var loaded = await service.GetConfigAsync("repo-1");

        Assert.Equal("wms-wcs", saved.ActiveProfileKey);
        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("wms-wcs", profile.Key);
        Assert.Contains("src/Application/Wcs/WcsRequestExecutors", profile.AnchorDirectories);
    }

    [Fact]
    public async Task GetActiveProfileAsync_ShouldReturnNullWhenNoProfilesConfigured()
    {
        var factory = CreateContextFactory();
        var service = CreateService(factory);

        var profile = await service.GetActiveProfileAsync("repo-1");

        Assert.Null(profile);
    }

    private static RepositoryWorkflowConfigService CreateService(TestContextFactory factory)
    {
        return new RepositoryWorkflowConfigService(
            factory.CreateContext(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryWorkflowConfigService>.Instance);
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
