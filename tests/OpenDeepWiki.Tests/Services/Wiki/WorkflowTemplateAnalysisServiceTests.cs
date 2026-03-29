using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using OpenDeepWiki.Tests.Chat.Sessions;
using Xunit;
using SessionTestDbContext = OpenDeepWiki.Tests.Chat.Sessions.TestDbContext;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowTemplateAnalysisServiceTests
{
    [Fact]
    public async Task AugmentCurrentDraftAsync_ShouldReplacePreviousSuggestedAnalysisSymbols_WhenDraftCameFromLspAugment()
    {
        using var context = SessionTestDbContext.Create();
        var repository = await SeedRepositoryAsync(context);
        var session = await SeedSessionAsync(context, repository.Id, CreateExistingLspDraft());
        var augment = CreateAugmentResult();
        var service = CreateService(context, repository.Id, session.Id, augment);

        var result = await service.AugmentCurrentDraftAsync(
            repository.Id,
            session.Id,
            new WorkflowTemplateAugmentRequest
            {
                ApplyToDraftVersion = true
            });

        Assert.Equal(2, result.CreatedVersionNumber);
        Assert.NotNull(result.Session.CurrentDraft);

        var draft = result.Session.CurrentDraft!;
        Assert.Equal(
            ["Manual.Custom.Root", "New.Executor.Root"],
            draft.Analysis.RootSymbolNames);
        Assert.Equal(
            ["Manual.Custom.MustExplain", "New.Executor.MustExplain"],
            draft.Analysis.MustExplainSymbols);
        Assert.Equal(
            ["New.Executor.Root"],
            draft.LspAssist.SuggestedRootSymbolNames);
        Assert.Equal(
            ["New.Executor.MustExplain"],
            draft.LspAssist.SuggestedMustExplainSymbols);
    }

    [Fact]
    public async Task AugmentCurrentDraftAsync_ShouldRefreshSuggestedChapterProfiles_InsteadOfAccumulatingOldNoise()
    {
        using var context = SessionTestDbContext.Create();
        var repository = await SeedRepositoryAsync(context);
        var session = await SeedSessionAsync(context, repository.Id, CreateExistingLspDraft());
        var augment = CreateAugmentResult();
        var service = CreateService(context, repository.Id, session.Id, augment);

        var result = await service.AugmentCurrentDraftAsync(
            repository.Id,
            session.Id,
            new WorkflowTemplateAugmentRequest
            {
                ApplyToDraftVersion = true
            });

        Assert.NotNull(result.Session.CurrentDraft);
        var draft = result.Session.CurrentDraft!;
        var branchChapter = Assert.Single(
            draft.ChapterProfiles,
            chapter => string.Equals(chapter.Key, "branch-decisions", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["New.Executor.Root", "New.Plugin.Root"],
            branchChapter.RootSymbolNames);
        Assert.Equal(
            ["New.Executor.MustExplain"],
            branchChapter.MustExplainSymbols);
        Assert.Contains("手工补充章节要求", branchChapter.RequiredSections);
        Assert.Contains("各场景入口条件与分流结果", branchChapter.RequiredSections);
        Assert.DoesNotContain("string", branchChapter.RootSymbolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Linq.Enumerable", branchChapter.MustExplainSymbols, StringComparer.OrdinalIgnoreCase);
    }

    private static WorkflowTemplateAnalysisService CreateService(
        SessionTestDbContext context,
        string repositoryId,
        string sessionId,
        WorkflowLspAugmentResult augment)
    {
        var repositoryAnalyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        repositoryAnalyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                It.Is<Repository>(repo => repo.Id == repositoryId),
                "main",
                "snapshot-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                RepositoryId = repositoryId,
                WorkingDirectory = @"D:\repo",
                Organization = "local",
                RepositoryName = "OpenDeepWiki",
                BranchName = "main",
                CommitId = "snapshot-2",
                PreviousCommitId = "snapshot-1"
            });
        repositoryAnalyzer
            .Setup(item => item.CleanupWorkspaceAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var discoveryService = new Mock<IWorkflowDiscoveryService>(MockBehavior.Strict);
        discoveryService
            .Setup(item => item.DiscoverAsync(
                It.IsAny<RepositoryWorkspace>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDiscoveryResult
            {
                Graph = new WorkflowSemanticGraph()
            });

        var augmentService = new Mock<IWorkflowLspAugmentService>(MockBehavior.Strict);
        augmentService
            .Setup(item => item.AugmentAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.IsAny<RepositoryWorkflowProfile>(),
                It.IsAny<WorkflowSemanticGraph>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(augment);

        var configService = new RepositoryWorkflowConfigService(
            context,
            NullLogger<RepositoryWorkflowConfigService>.Instance);
        var workbenchService = new WorkflowTemplateWorkbenchService(
            context,
            configService,
            new StubContextCollector(),
            new StubAiClient(),
            new StubUserContext());

        return new WorkflowTemplateAnalysisService(
            context,
            repositoryAnalyzer.Object,
            discoveryService.Object,
            augmentService.Object,
            new Mock<IWorkflowDeepAnalysisService>(MockBehavior.Strict).Object,
            new Mock<IWorkflowAnalysisQueueService>(MockBehavior.Strict).Object,
            workbenchService,
            new StubUserContext(),
            NullLogger<WorkflowTemplateAnalysisService>.Instance);
    }

    private static async Task<Repository> SeedRepositoryAsync(SessionTestDbContext context)
    {
        var repository = new Repository
        {
            Id = "repo-analysis-1",
            OwnerUserId = "user-1",
            OrgName = "local",
            RepoName = "OpenDeepWiki",
            GitUrl = @"local/D:\VSWorkshop\OpenDeepWiki",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = "branch-main",
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = "snapshot-1",
            CreatedAt = DateTime.UtcNow
        };
        var language = new BranchLanguage
        {
            Id = "lang-zh",
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        await context.SaveChangesAsync();
        return repository;
    }

    private static async Task<WorkflowTemplateSession> SeedSessionAsync(
        SessionTestDbContext context,
        string repositoryId,
        RepositoryWorkflowProfile draft)
    {
        var session = new WorkflowTemplateSession
        {
            Id = "workflow-session-1",
            RepositoryId = repositoryId,
            BranchId = "branch-main",
            BranchName = "main",
            LanguageCode = "zh",
            Status = "Active",
            Title = "站台入库工作台",
            CurrentDraftKey = draft.Key,
            CurrentDraftName = draft.Name,
            CurrentVersionNumber = 1,
            MessageCount = 0,
            CreatedByUserId = "admin-1",
            CreatedByUserName = "token帅比",
            LastActivityAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var version = new WorkflowTemplateDraftVersion
        {
            Id = "workflow-draft-version-1",
            SessionId = session.Id,
            VersionNumber = 1,
            SourceType = "lsp-augment",
            ChangeSummary = "旧版 LSP 增强",
            DraftJson = JsonSerializer.Serialize(draft),
            ValidationIssuesJson = "[]"
        };

        context.WorkflowTemplateSessions.Add(session);
        context.WorkflowTemplateDraftVersions.Add(version);
        await context.SaveChangesAsync();
        return session;
    }

    private static RepositoryWorkflowProfile CreateExistingLspDraft()
    {
        return new RepositoryWorkflowProfile
        {
            Key = "wcs-stn-move-in",
            Name = "处理站台入库申请",
            Description = "旧版增强草稿",
            Enabled = false,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            EntryRoots = ["Old.Entry.Root"],
            Source = new RepositoryWorkflowProfileSource
            {
                Type = "lsp-augment",
                SessionId = "workflow-session-1",
                VersionNumber = 1
            },
            Analysis = new WorkflowProfileAnalysisOptions
            {
                RootSymbolNames = ["Old.Executor.Root", "Manual.Custom.Root"],
                MustExplainSymbols = ["string", "Old.Executor.MustExplain", "Manual.Custom.MustExplain"]
            },
            ChapterProfiles =
            [
                new WorkflowChapterProfile
                {
                    Key = "branch-decisions",
                    Title = "分支判断与场景差异",
                    Description = "旧版章节",
                    AnalysisMode = WorkflowChapterAnalysisMode.Deep,
                    RootSymbolNames = ["string", "Old.Executor.Root"],
                    MustExplainSymbols = ["System.Linq.Enumerable", "Old.Executor.MustExplain"],
                    RequiredSections = ["手工补充章节要求"],
                    OutputArtifacts = ["markdown"],
                    DepthBudget = 2,
                    MaxNodes = 12,
                    IncludeFlowchart = false,
                    IncludeMindmap = false
                }
            ],
            LspAssist = new WorkflowLspAssistOptions
            {
                Enabled = true,
                SuggestedRootSymbolNames = ["Old.Executor.Root"],
                SuggestedMustExplainSymbols = ["string", "Old.Executor.MustExplain"]
            }
        };
    }

    private static WorkflowLspAugmentResult CreateAugmentResult()
    {
        return new WorkflowLspAugmentResult
        {
            ProfileKey = "wcs-stn-move-in",
            Summary = "已刷新 LSP 增强结果。",
            Strategy = "external-lsp",
            SuggestedEntryDirectories = ["src/Application/Wcs"],
            SuggestedRootSymbolNames = ["New.Executor.Root"],
            SuggestedMustExplainSymbols = ["New.Executor.MustExplain"],
            SuggestedChapterProfiles =
            [
                new WorkflowChapterProfile
                {
                    Key = "branch-decisions",
                    Title = "分支判断与场景差异",
                    Description = "新版章节",
                    AnalysisMode = WorkflowChapterAnalysisMode.Deep,
                    RootSymbolNames = ["New.Executor.Root", "New.Plugin.Root"],
                    MustExplainSymbols = ["New.Executor.MustExplain"],
                    RequiredSections = ["各场景入口条件与分流结果"],
                    OutputArtifacts = ["markdown", "flowchart"],
                    DepthBudget = 3,
                    MaxNodes = 24,
                    IncludeFlowchart = true,
                    IncludeMindmap = false
                }
            ],
            CallHierarchyEdges =
            [
                new WorkflowCallHierarchyEdge
                {
                    FromSymbol = "New.Executor.Root",
                    ToSymbol = "New.Executor.MustExplain",
                    Kind = "Invokes"
                }
            ]
        };
    }

    private sealed class StubContextCollector : IWorkflowTemplateContextCollector
    {
        public Task<WorkflowTemplateSessionContextDto> CollectAsync(
            Repository repository,
            RepositoryBranch? branch,
            string? languageCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowTemplateSessionContextDto
            {
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BranchName = branch?.BranchName,
                LanguageCode = languageCode,
                DirectoryPreview = string.Empty
            });
        }
    }

    private sealed class StubAiClient : IWorkflowTemplateWorkbenchAiClient
    {
        public Task<WorkflowTemplateWorkbenchAiResult> GenerateDraftAsync(
            WorkflowTemplateWorkbenchAiRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test should not invoke AI draft generation.");
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public string? UserId => "admin-1";

        public string? UserName => "token帅比";

        public string? Email => "admin@example.com";

        public bool IsAuthenticated => true;

        public System.Security.Claims.ClaimsPrincipal? User => null;
    }
}
