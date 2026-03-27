using Moq;
using OpenDeepWiki.Cache.Abstractions;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Tests.Chat.Sessions;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryDocsServiceUnitTests
{
    [Fact]
    public async Task GetTreeAsync_ShouldReturnExistingCatalogs_EvenWhenRepositoryStatusIsFailed()
    {
        using var context = TestDbContext.Create();

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "1282/WmsServerV4",
            RepoName = "1282忠信WMS",
            GitUrl = "https://example.com/repo.git",
            Status = RepositoryStatus.Failed,
            CreatedAt = DateTime.UtcNow
        };

        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "overlay/1282zx",
            CreatedAt = DateTime.UtcNow
        };

        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            CreatedAt = DateTime.UtcNow
        };

        var docFile = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Content = "# 差异概览"
        };

        var rootCatalog = new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Title = "差异概览",
            Path = "overview",
            Order = 0,
            DocFileId = docFile.Id
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        context.DocFiles.Add(docFile);
        context.DocCatalogs.Add(rootCatalog);
        await context.SaveChangesAsync();

        var service = new RepositoryDocsService(
            context,
            Mock.Of<IGitPlatformService>(),
            Mock.Of<ICache>());

        var response = await service.GetTreeAsync(
            repository.OrgName,
            repository.RepoName,
            branch: branch.BranchName,
            lang: language.LanguageCode);

        Assert.True(response.Exists);
        Assert.Equal(RepositoryStatus.Failed, response.Status);
        Assert.Equal(branch.BranchName, response.CurrentBranch);
        Assert.Equal(language.LanguageCode, response.CurrentLanguage);
        Assert.Equal("overview", response.DefaultSlug);
        Assert.Single(response.Nodes);
        Assert.Equal("差异概览", response.Nodes[0].Title);
        Assert.Equal("overview", response.Nodes[0].Slug);
    }
}
