using Moq;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using OpenDeepWiki.Tests.Chat.Sessions;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Admin;

public class AdminRepositoryServiceTests
{
    [Fact]
    public async Task RegenerateWorkflowDocumentsAsync_ShouldRebuildSelectedBranchLanguageOnly()
    {
        using var context = TestDbContext.Create();

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "WmsServerV4Dev",
            GitUrl = @"local/D:\WMS4\DevWorkSpace\WmsServerV4Dev",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = "snapshot-1",
            CreatedAt = DateTime.UtcNow
        };
        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        await context.SaveChangesAsync();

        var workspace = new RepositoryWorkspace
        {
            RepositoryId = repository.Id,
            WorkingDirectory = @"D:\data\local\WmsServerV4Dev\tree",
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branch.BranchName,
            CommitId = "snapshot-2",
            PreviousCommitId = branch.LastCommitId
        };

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                It.Is<Repository>(repo => repo.Id == repository.Id),
                branch.BranchName,
                branch.LastCommitId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);
        analyzer
            .Setup(item => item.CleanupWorkspaceAsync(workspace, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(item => item.RegenerateWorkflowDocumentsAsync(
                workspace,
                It.Is<BranchLanguage>(branchLanguage => branchLanguage.Id == language.Id),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AdminRepositoryService(
            context,
            Mock.Of<IGitPlatformService>(),
            analyzer.Object,
            wikiGenerator.Object,
            Mock.Of<IProcessingLogService>());

        var result = await service.RegenerateWorkflowDocumentsAsync(
            repository.Id,
            new RegenerateRepositoryWorkflowRequest
            {
                BranchId = branch.Id,
                LanguageCode = "zh"
            });

        Assert.True(result.Success);
        Assert.Equal("全部业务流程重建已完成", result.Message);
        analyzer.VerifyAll();
        wikiGenerator.VerifyAll();
    }

    [Fact]
    public async Task RegenerateWorkflowDocumentsAsync_ShouldPassProfileKeyToWikiGenerator()
    {
        using var context = TestDbContext.Create();

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "WmsServerV4Dev",
            GitUrl = @"local/D:\WMS4\DevWorkSpace\WmsServerV4Dev",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = "snapshot-1",
            CreatedAt = DateTime.UtcNow
        };
        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        await context.SaveChangesAsync();

        var workspace = new RepositoryWorkspace
        {
            RepositoryId = repository.Id,
            WorkingDirectory = @"D:\data\local\WmsServerV4Dev\tree",
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branch.BranchName,
            CommitId = "snapshot-2",
            PreviousCommitId = branch.LastCommitId
        };

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                It.Is<Repository>(repo => repo.Id == repository.Id),
                branch.BranchName,
                branch.LastCommitId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);
        analyzer
            .Setup(item => item.CleanupWorkspaceAsync(workspace, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(item => item.RegenerateWorkflowDocumentsAsync(
                workspace,
                It.Is<BranchLanguage>(branchLanguage => branchLanguage.Id == language.Id),
                "stn-inbound",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AdminRepositoryService(
            context,
            Mock.Of<IGitPlatformService>(),
            analyzer.Object,
            wikiGenerator.Object,
            Mock.Of<IProcessingLogService>());

        var result = await service.RegenerateWorkflowDocumentsAsync(
            repository.Id,
            new RegenerateRepositoryWorkflowRequest
            {
                BranchId = branch.Id,
                LanguageCode = "zh",
                ProfileKey = "stn-inbound"
            });

        Assert.True(result.Success);
        Assert.Equal("已选正式业务流程重建已完成：stn-inbound", result.Message);
        analyzer.VerifyAll();
        wikiGenerator.VerifyAll();
    }

    [Fact]
    public async Task RegenerateWorkflowDocumentsAsync_ShouldFailWhenLanguageDoesNotExist()
    {
        using var context = TestDbContext.Create();

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "WmsServerV4Dev",
            GitUrl = @"local/D:\WMS4\DevWorkSpace\WmsServerV4Dev",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main",
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        await context.SaveChangesAsync();

        var service = new AdminRepositoryService(
            context,
            Mock.Of<IGitPlatformService>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IWikiGenerator>(),
            Mock.Of<IProcessingLogService>());

        var result = await service.RegenerateWorkflowDocumentsAsync(
            repository.Id,
            new RegenerateRepositoryWorkflowRequest
            {
                BranchId = branch.Id,
                LanguageCode = "zh"
            });

        Assert.False(result.Success);
        Assert.Equal("语言不存在", result.Message);
    }

    [Fact]
    public async Task RegenerateWorkflowDocumentsAsync_ShouldDetachLongRunningWorkFromRequestCancellation()
    {
        using var context = TestDbContext.Create();

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "WmsServerV4Dev",
            GitUrl = @"local/D:\WMS4\DevWorkSpace\WmsServerV4Dev",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = "snapshot-1",
            CreatedAt = DateTime.UtcNow
        };
        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        await context.SaveChangesAsync();

        var workspace = new RepositoryWorkspace
        {
            RepositoryId = repository.Id,
            WorkingDirectory = @"D:\data\local\WmsServerV4Dev\tree",
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branch.BranchName,
            CommitId = "snapshot-2",
            PreviousCommitId = branch.LastCommitId
        };

        var detachedTokenMatcher = It.Is<CancellationToken>(token => !token.CanBeCanceled);

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                It.Is<Repository>(repo => repo.Id == repository.Id),
                branch.BranchName,
                branch.LastCommitId,
                detachedTokenMatcher))
            .ReturnsAsync(workspace);
        analyzer
            .Setup(item => item.CleanupWorkspaceAsync(workspace, detachedTokenMatcher))
            .Returns(Task.CompletedTask);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(item => item.RegenerateWorkflowDocumentsAsync(
                workspace,
                It.Is<BranchLanguage>(branchLanguage => branchLanguage.Id == language.Id),
                null,
                detachedTokenMatcher))
            .Returns(Task.CompletedTask);

        var processingLogs = new Mock<IProcessingLogService>(MockBehavior.Strict);
        processingLogs
            .Setup(item => item.LogAsync(
                repository.Id,
                ProcessingStep.Workspace,
                It.Is<string>(message => message.Contains("开始准备业务流程重建工作区")),
                false,
                null,
                detachedTokenMatcher))
            .Returns(Task.CompletedTask);
        processingLogs
            .Setup(item => item.LogAsync(
                repository.Id,
                ProcessingStep.Workspace,
                It.Is<string>(message => message.Contains("业务流程重建工作区准备完成")),
                false,
                null,
                detachedTokenMatcher))
            .Returns(Task.CompletedTask);

        var service = new AdminRepositoryService(
            context,
            Mock.Of<IGitPlatformService>(),
            analyzer.Object,
            wikiGenerator.Object,
            processingLogs.Object);

        using var requestCts = new CancellationTokenSource();

        var result = await service.RegenerateWorkflowDocumentsAsync(
            repository.Id,
            new RegenerateRepositoryWorkflowRequest
            {
                BranchId = branch.Id,
                LanguageCode = "zh"
            },
            requestCts.Token);

        Assert.True(result.Success);
        analyzer.VerifyAll();
        wikiGenerator.VerifyAll();
        processingLogs.VerifyAll();
    }
}
