using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Tests.Chat.Sessions;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class ProcessingLogServiceTests
{
    [Fact]
    public async Task GetLogsAsync_ShouldParseWorkflowRegenerationProgress()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IContext>(provider => provider.GetRequiredService<TestDbContext>());

        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var repositoryId = Guid.NewGuid().ToString();
            var startedAt = DateTime.UtcNow.AddMinutes(-1);

            context.RepositoryProcessingLogs.AddRange(
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Workspace,
                    Message = "开始准备业务流程重建工作区",
                    CreatedAt = startedAt
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "开始重建业务流程文档，候选数: 3",
                    CreatedAt = startedAt.AddSeconds(1)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "开始重建业务流程 (1/3): 站台入库申请",
                    CreatedAt = startedAt.AddSeconds(2)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "业务流程文档完成 (1/3): 站台入库申请 - 成功",
                    CreatedAt = startedAt.AddSeconds(3)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "开始重建业务流程 (2/3): 工位入库",
                    CreatedAt = startedAt.AddSeconds(4)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "业务流程文档完成 (2/3): 工位入库 - 成功",
                    CreatedAt = startedAt.AddSeconds(5)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "开始重建业务流程 (3/3): 货位异常恢复",
                    CreatedAt = startedAt.AddSeconds(6)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Content,
                    Message = "业务流程文档完成 (3/3): 货位异常恢复 - 成功",
                    CreatedAt = startedAt.AddSeconds(7)
                },
                new RepositoryProcessingLog
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repositoryId,
                    Step = ProcessingStep.Complete,
                    Message = "全部业务流程重建完成，流程数: 3，耗时 1234ms",
                    CreatedAt = startedAt.AddSeconds(8)
                });

            await context.SaveChangesAsync();
        }

        var service = new ProcessingLogService(provider.GetRequiredService<IServiceScopeFactory>());
        var repositoryIdForAssert = string.Empty;

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            repositoryIdForAssert = await context.RepositoryProcessingLogs
                .Select(item => item.RepositoryId)
                .FirstAsync();
        }

        var response = await service.GetLogsAsync(repositoryIdForAssert);

        Assert.Equal(3, response.TotalDocuments);
        Assert.Equal(3, response.CompletedDocuments);
        Assert.Equal(ProcessingStep.Complete, response.CurrentStep);
        Assert.NotNull(response.StartedAt);
        Assert.Equal(9, response.Logs.Count);
    }
}
