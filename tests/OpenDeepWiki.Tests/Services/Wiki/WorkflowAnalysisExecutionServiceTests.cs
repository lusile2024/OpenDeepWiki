using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;
using SessionTestDbContext = OpenDeepWiki.Tests.Chat.Sessions.TestDbContext;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowAnalysisExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldRunChapterAndBranchTasksInParallel()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock();
        var workflowConfigService = CreateWorkflowConfigServiceMock();
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner();
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        Assert.Equal(2, taskRunner.MaxObservedConcurrency);

        var session = Assert.Single(context.WorkflowAnalysisSessions.Where(item => item.Id == seed.AnalysisSession.Id));
        Assert.Equal("Completed", session.Status);
        Assert.Equal(5, session.TotalTasks);
        Assert.Equal(5, session.CompletedTasks);
        Assert.Equal(0, session.FailedTasks);
        Assert.Equal(0, session.PendingTaskCount);
        Assert.Equal(0, session.RunningTaskCount);

        var tasks = context.WorkflowAnalysisTasks
            .Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id)
            .OrderBy(item => item.SequenceNumber)
            .ToList();
        Assert.Equal(5, tasks.Count);
        Assert.All(tasks, task => Assert.Equal("Completed", task.Status));

        var plannerTask = Assert.Single(tasks, task => task.TaskType == "planner");
        var chapterTasks = tasks.Where(task => task.TaskType == "chapter-analysis").ToList();
        var branchTasks = tasks.Where(task => task.TaskType == "branch-drilldown").ToList();
        Assert.All(chapterTasks, task => Assert.Equal(plannerTask.Id, task.ParentTaskId));
        Assert.All(branchTasks, task => Assert.Contains(task.ParentTaskId, chapterTasks.Select(item => item.Id)));

        var artifacts = context.WorkflowAnalysisArtifacts
            .Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id)
            .ToList();
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "analysis-overview" && artifact.TaskId == null);
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "chapter-brief" && artifact.TaskId is not null);
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "branch-summary" && artifact.TaskId is not null);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepCompletedArtifacts_WhenOneParallelTaskFails()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock();
        var workflowConfigService = CreateWorkflowConfigServiceMock();
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner(["分支钻取：库存回写"]);
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        var session = Assert.Single(context.WorkflowAnalysisSessions.Where(item => item.Id == seed.AnalysisSession.Id));
        Assert.Equal("CompletedWithErrors", session.Status);
        Assert.Equal(4, session.CompletedTasks);
        Assert.Equal(1, session.FailedTasks);

        var failedTask = Assert.Single(
            context.WorkflowAnalysisTasks.Where(item =>
                item.AnalysisSessionId == seed.AnalysisSession.Id &&
                item.Status == "Failed"));
        Assert.Equal("分支钻取：库存回写", failedTask.Title);
        Assert.Equal("模拟任务失败", failedTask.ErrorMessage);

        var artifacts = context.WorkflowAnalysisArtifacts
            .Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id)
            .ToList();
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "analysis-overview");
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "chapter-brief");
        Assert.DoesNotContain(artifacts, artifact => artifact.TaskId == failedTask.Id && artifact.ArtifactType == "branch-summary");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRegenerateWorkflowDocument_WhenFormalProfileExists()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock();
        var workflowConfigService = CreateWorkflowConfigServiceMock(seed.Profile);
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner();
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        wikiGenerator.Verify(item => item.RegenerateWorkflowDocumentsAsync(
            It.Is<RepositoryWorkspace>(value => value.RepositoryId == seed.Repository.Id),
            It.Is<BranchLanguage>(value => value.RepositoryBranchId == seed.Branch.Id && value.LanguageCode == "zh"),
            seed.Profile.Key,
            It.IsAny<CancellationToken>()), Times.Once);

        var session = Assert.Single(context.WorkflowAnalysisSessions.Where(item => item.Id == seed.AnalysisSession.Id));
        Assert.Equal("Completed", session.Status);
        Assert.Contains("已自动回填业务流文档", session.ProgressMessage);
        Assert.Contains(context.WorkflowAnalysisLogs.Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id),
            log => log.Message.Contains("开始回填业务流文档", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMergeAiPlannerHintTasks_WhenSuggestionsAccepted()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock(new WorkflowAnalysisPlannerHintResult
        {
            Summary = "补充了双深校验分支",
            SuggestedBranchTasks =
            [
                new WorkflowAnalysisPlannerHintSuggestion
                {
                    ChapterKey = "allocation",
                    BranchRoot = "ValidateDoubleDeep",
                    FocusSymbols = ["ValidateDoubleDeep", "LoadCandidateBins"],
                    Summary = "需要补充双深货位校验与候选货位筛选逻辑。",
                    Reason = "must-explain hotspot"
                },
                new WorkflowAnalysisPlannerHintSuggestion
                {
                    ChapterKey = "allocation",
                    BranchRoot = "AllocateDoubleDeep",
                    FocusSymbols = ["AllocateDoubleDeep"],
                    Summary = "重复的现有分支，不应再次加入。",
                    Reason = "duplicate"
                }
            ]
        });
        var workflowConfigService = CreateWorkflowConfigServiceMock();
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner();
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        var tasks = context.WorkflowAnalysisTasks
            .Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id)
            .OrderBy(item => item.SequenceNumber)
            .ToList();
        Assert.Equal(6, tasks.Count);

        var aiHintTask = Assert.Single(tasks, item =>
            item.TaskType == "branch-drilldown" &&
            item.Title == "分支钻取：ValidateDoubleDeep");
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(aiHintTask.MetadataJson!);
        Assert.NotNull(metadata);
        Assert.Equal("ai-hint", metadata!["plannerSource"]);
        Assert.Equal("ai-hint", metadata["branchReason"]);
        Assert.Equal("must-explain hotspot", metadata["plannerHintReason"]);
        Assert.Equal(1, tasks.Count(item =>
            item.TaskType == "branch-drilldown" &&
            (item.MetadataJson ?? string.Empty).Contains("AllocateDoubleDeep", StringComparison.Ordinal)));

        Assert.Contains(context.WorkflowAnalysisLogs.Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id),
            log => log.Message.Contains("AI planner hint 返回 2 个建议，采纳 1 个", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipAiPlannerHint_WhenBranchCapacityIsExhausted()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2, maxBranchTasks: 1);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock(new WorkflowAnalysisPlannerHintResult
        {
            Summary = "即使有建议，也不该被调用",
            SuggestedBranchTasks =
            [
                new WorkflowAnalysisPlannerHintSuggestion
                {
                    ChapterKey = "allocation",
                    BranchRoot = "ValidateDoubleDeep",
                    FocusSymbols = ["ValidateDoubleDeep"],
                    Summary = "不会被执行。",
                    Reason = "capacity"
                }
            ]
        });
        var workflowConfigService = CreateWorkflowConfigServiceMock();
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner();
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        plannerHintService.Verify(item => item.GenerateHintsAsync(It.IsAny<WorkflowAnalysisPlannerHintRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(5, context.WorkflowAnalysisTasks.Count(item => item.AnalysisSessionId == seed.AnalysisSession.Id));
        Assert.Contains(context.WorkflowAnalysisLogs.Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id),
            log => log.Message.Contains("当前章节分支容量已满", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToDeterministicPlanner_WhenPlannerHintFails()
    {
        using var context = SessionTestDbContext.Create();
        var seed = await SeedAnalysisSessionAsync(context, maxParallelTasks: 2);
        var workspace = CreateWorkspace(seed.Repository.Id, seed.Branch.BranchName, seed.Branch.LastCommitId!);

        var analyzer = CreateAnalyzerMock(seed.Repository, seed.Branch, workspace);
        var discoveryService = CreateDiscoveryServiceMock();
        var deepAnalysisService = CreateDeepAnalysisServiceMock();
        var plannerHintService = CreatePlannerHintServiceMock(exception: new InvalidOperationException("planner ai unavailable"));
        var workflowConfigService = CreateWorkflowConfigServiceMock();
        var wikiGenerator = CreateWikiGeneratorMock();
        var taskRunner = new TrackingTaskRunner();
        var service = new WorkflowAnalysisExecutionService(
            context,
            analyzer.Object,
            discoveryService.Object,
            deepAnalysisService.Object,
            plannerHintService.Object,
            taskRunner,
            workflowConfigService.Object,
            wikiGenerator.Object,
            NullLogger<WorkflowAnalysisExecutionService>.Instance);

        await service.ExecuteAsync(seed.AnalysisSession.Id);

        var tasks = context.WorkflowAnalysisTasks
            .Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id)
            .ToList();
        Assert.Equal(5, tasks.Count);
        Assert.DoesNotContain(tasks, item => (item.MetadataJson ?? string.Empty).Contains("\"plannerSource\": \"ai-hint\"", StringComparison.Ordinal));
        Assert.Contains(context.WorkflowAnalysisLogs.Where(item => item.AnalysisSessionId == seed.AnalysisSession.Id),
            log => log.Message.Contains("AI planner hint 失败，已回退到 deterministic planner", StringComparison.Ordinal));
    }

    private static Mock<IRepositoryAnalyzer> CreateAnalyzerMock(
        Repository repository,
        RepositoryBranch branch,
        RepositoryWorkspace workspace)
    {
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
        return analyzer;
    }

    private static Mock<IWorkflowDiscoveryService> CreateDiscoveryServiceMock()
    {
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
        return discoveryService;
    }

    private static Mock<IWorkflowDeepAnalysisService> CreateDeepAnalysisServiceMock(
        WorkflowDeepAnalysisResult? result = null)
    {
        var deepAnalysisService = new Mock<IWorkflowDeepAnalysisService>(MockBehavior.Strict);
        deepAnalysisService
            .Setup(item => item.Analyze(It.IsAny<WorkflowDeepAnalysisInput>()))
            .Returns(result ?? CreateDeepAnalysisResult());
        return deepAnalysisService;
    }

    private static Mock<IWorkflowAnalysisPlannerHintService> CreatePlannerHintServiceMock(
        WorkflowAnalysisPlannerHintResult? result = null,
        Exception? exception = null)
    {
        var plannerHintService = new Mock<IWorkflowAnalysisPlannerHintService>(MockBehavior.Strict);

        if (exception is not null)
        {
            plannerHintService
                .Setup(item => item.GenerateHintsAsync(
                    It.IsAny<WorkflowAnalysisPlannerHintRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }
        else
        {
            plannerHintService
                .Setup(item => item.GenerateHintsAsync(
                    It.IsAny<WorkflowAnalysisPlannerHintRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result ?? new WorkflowAnalysisPlannerHintResult());
        }

        return plannerHintService;
    }

    private static Mock<IRepositoryWorkflowConfigService> CreateWorkflowConfigServiceMock(
        RepositoryWorkflowProfile? profile = null)
    {
        var workflowConfigService = new Mock<IRepositoryWorkflowConfigService>(MockBehavior.Strict);
        workflowConfigService
            .Setup(item => item.GetProfileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return workflowConfigService;
    }

    private static Mock<IWikiGenerator> CreateWikiGeneratorMock()
    {
        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(item => item.RegenerateWorkflowDocumentsAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.IsAny<BranchLanguage>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return wikiGenerator;
    }

    private static WorkflowDeepAnalysisResult CreateDeepAnalysisResult()
    {
        return new WorkflowDeepAnalysisResult
        {
            Summary = "并行深挖已拆成主线规划、章节深挖和分支钻取。",
            ChapterSlices =
            [
                new WorkflowChapterSlice
                {
                    ChapterKey = "allocation",
                    ChapterTitle = "货位分配",
                    RootSymbolNames = ["AllocateMultiStationStacker"],
                    DecisionPoints =
                    [
                        new WorkflowChapterDecisionPoint
                        {
                            SymbolName = "AllocateDoubleDeep",
                            OutgoingSymbols = ["ValidateDoubleDeep"],
                            Summary = "按货位深度进入双深分支。"
                        }
                    ]
                },
                new WorkflowChapterSlice
                {
                    ChapterKey = "closing",
                    ChapterTitle = "任务收尾",
                    RootSymbolNames = ["FinishMoveInTaskAsync"],
                    StateChanges =
                    [
                        new WorkflowChapterStateChange
                        {
                            FromSymbol = "FinishMoveInTaskAsync",
                            ToSymbol = "UpdateInventoryAsync",
                            ChangeType = "UpdatesStatus"
                        }
                    ]
                }
            ],
            Tasks =
            [
                new WorkflowDeepAnalysisTaskResult
                {
                    TaskType = "planner",
                    Title = "主线分析规划",
                    Depth = 0,
                    FocusSymbols = ["WcsRequestWmsExecutorJob.Execute", "WcsStnMoveInExecutor.ExecuteAsync"],
                    Summary = "先收敛主线，再并行展开章节与分支。",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                },
                new WorkflowDeepAnalysisTaskResult
                {
                    TaskType = "chapter-analysis",
                    Title = "章节深挖：货位分配",
                    Depth = 1,
                    FocusSymbols = ["AllocateMultiStationStacker"],
                    Summary = "输出货位分配章节摘要与图种子。",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "allocation"
                    }
                },
                new WorkflowDeepAnalysisTaskResult
                {
                    TaskType = "chapter-analysis",
                    Title = "章节深挖：任务收尾",
                    Depth = 1,
                    FocusSymbols = ["FinishMoveInTaskAsync"],
                    Summary = "输出任务收尾章节摘要与图种子。",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "closing"
                    }
                },
                new WorkflowDeepAnalysisTaskResult
                {
                    TaskType = "branch-drilldown",
                    Title = "分支钻取：双深货位",
                    Depth = 2,
                    FocusSymbols = ["AllocateDoubleDeep", "ValidateDoubleDeep"],
                    Summary = "深入分析双深货位分配条件。",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "allocation",
                        ["branchRoot"] = "AllocateDoubleDeep"
                    }
                },
                new WorkflowDeepAnalysisTaskResult
                {
                    TaskType = "branch-drilldown",
                    Title = "分支钻取：库存回写",
                    Depth = 2,
                    FocusSymbols = ["FinishMoveInTaskAsync", "UpdateInventoryAsync"],
                    Summary = "深入分析任务回调后的库存回写逻辑。",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "closing",
                        ["branchRoot"] = "UpdateInventoryAsync"
                    }
                }
            ],
            Artifacts =
            [
                new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "analysis-overview",
                    Title = "ACP 深挖总览",
                    ContentFormat = "markdown",
                    Content = "# ACP 深挖总览",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                },
                new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "chapter-brief",
                    Title = "货位分配章节摘要",
                    ContentFormat = "markdown",
                    Content = "# 货位分配",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "allocation"
                    }
                },
                new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "flowchart",
                    Title = "货位分配流程图种子",
                    ContentFormat = "mermaid",
                    Content = "flowchart TD\nA[分配] --> B[下发]",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "allocation"
                    }
                },
                new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "chapter-brief",
                    Title = "任务收尾章节摘要",
                    ContentFormat = "markdown",
                    Content = "# 任务收尾",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "closing"
                    }
                },
                new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "mindmap",
                    Title = "任务收尾脑图种子",
                    ContentFormat = "markdown",
                    Content = "- 任务收尾",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chapterKey"] = "closing"
                    }
                }
            ]
        };
    }

    private static RepositoryWorkspace CreateWorkspace(string repositoryId, string branchName, string previousCommitId)
    {
        return new RepositoryWorkspace
        {
            RepositoryId = repositoryId,
            WorkingDirectory = @"D:\data\local\OpenDeepWiki\tree",
            Organization = "local",
            RepositoryName = "OpenDeepWiki",
            BranchName = branchName,
            CommitId = "snapshot-2",
            PreviousCommitId = previousCommitId
        };
    }

    private static async Task<SeedResult> SeedAnalysisSessionAsync(
        SessionTestDbContext context,
        int maxParallelTasks,
        int maxBranchTasks = 4)
    {
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "OpenDeepWiki",
            GitUrl = @"local/D:\VSWorkshop\OpenDeepWiki",
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
        var templateSession = new WorkflowTemplateSession
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            BranchName = branch.BranchName,
            LanguageCode = "zh",
            CurrentDraftKey = "wcs-stn-move-in",
            CurrentDraftName = "处理站台入库申请",
            CurrentVersionNumber = 1,
            LastActivityAt = DateTime.UtcNow
        };
        var profile = new RepositoryWorkflowProfile
        {
            Key = "wcs-stn-move-in",
            Name = "处理站台入库申请",
            EntryRoots = ["WcsRequestWmsExecutorJob.Execute", "WcsStnMoveInExecutor.ExecuteAsync"],
            Acp = new WorkflowAcpOptions
            {
                Objective = "并行深挖站台入库主线与分支",
                MaxParallelTasks = maxParallelTasks,
                MaxBranchTasks = maxBranchTasks
            },
            ChapterProfiles =
            [
                new WorkflowChapterProfile
                {
                    Key = "allocation",
                    Title = "货位分配",
                    AnalysisMode = WorkflowChapterAnalysisMode.Deep
                },
                new WorkflowChapterProfile
                {
                    Key = "closing",
                    Title = "任务收尾",
                    AnalysisMode = WorkflowChapterAnalysisMode.Deep
                }
            ]
        };
        var branchLanguage = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true
        };
        var draftVersion = new WorkflowTemplateDraftVersion
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = templateSession.Id,
            VersionNumber = 1,
            SourceType = "assistant",
            DraftJson = JsonSerializer.Serialize(profile)
        };
        var analysisSession = new WorkflowAnalysisSession
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            WorkflowTemplateSessionId = templateSession.Id,
            BranchId = branch.Id,
            BranchName = branch.BranchName,
            LanguageCode = "zh",
            ProfileKey = profile.Key,
            DraftVersionNumber = 1,
            Status = "Running",
            Objective = profile.Acp.Objective,
            Summary = "等待执行",
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(branchLanguage);
        context.WorkflowTemplateSessions.Add(templateSession);
        context.WorkflowTemplateDraftVersions.Add(draftVersion);
        context.WorkflowAnalysisSessions.Add(analysisSession);
        await context.SaveChangesAsync();

        return new SeedResult(repository, branch, branchLanguage, templateSession, analysisSession, profile);
    }

    private sealed record SeedResult(
        Repository Repository,
        RepositoryBranch Branch,
        BranchLanguage BranchLanguage,
        WorkflowTemplateSession TemplateSession,
        WorkflowAnalysisSession AnalysisSession,
        RepositoryWorkflowProfile Profile);

    private sealed class TrackingTaskRunner : IWorkflowAnalysisTaskRunner
    {
        private readonly HashSet<string> _failingTaskTitles;
        private readonly IWorkflowAnalysisTaskRunner _inner = new WorkflowAnalysisTaskRunner();
        private int _activeCount;
        private int _maxObservedConcurrency;

        public TrackingTaskRunner(IEnumerable<string>? failingTaskTitles = null)
        {
            _failingTaskTitles = failingTaskTitles is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(failingTaskTitles, StringComparer.OrdinalIgnoreCase);
        }

        public int MaxObservedConcurrency => _maxObservedConcurrency;

        public async Task<WorkflowAnalysisTaskExecutionResult> ExecuteAsync(
            WorkflowAnalysisTaskExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _activeCount);
            UpdateMaxObservedConcurrency(current);

            try
            {
                await Task.Delay(60, cancellationToken);

                if (_failingTaskTitles.Contains(request.Title))
                {
                    throw new InvalidOperationException("模拟任务失败");
                }

                return await _inner.ExecuteAsync(request, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }

        private void UpdateMaxObservedConcurrency(int current)
        {
            while (true)
            {
                var observed = _maxObservedConcurrency;
                if (observed >= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxObservedConcurrency, current, observed) == observed)
                {
                    return;
                }
            }
        }
    }
}
