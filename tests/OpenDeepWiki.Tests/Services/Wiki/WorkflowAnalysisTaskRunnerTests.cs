using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowAnalysisTaskRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldKeepPlannerDeterministic_WhenAiClientIsRegistered()
    {
        var aiClient = new Mock<IWorkflowAnalysisTaskAiClient>(MockBehavior.Strict);
        var runner = new WorkflowAnalysisTaskRunner(
            aiClient.Object,
            NullLogger<WorkflowAnalysisTaskRunner>.Instance);

        var request = new WorkflowAnalysisTaskExecutionRequest
        {
            TaskType = "planner",
            Title = "主线分析规划",
            Summary = "先收敛主线，再并行展开章节与分支。"
        };

        var result = await runner.ExecuteAsync(request);

        Assert.Equal(request.Summary, result.Summary);
        Assert.Single(result.LogMessages);
        Assert.Equal(request.Summary, result.LogMessages[0]);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseAiResultForChapterAnalysis_WhenAiSucceeds()
    {
        var request = CreateChapterAnalysisRequest();
        var aiClient = new Mock<IWorkflowAnalysisTaskAiClient>(MockBehavior.Strict);
        aiClient
            .Setup(item => item.GenerateAsync(
                It.Is<WorkflowAnalysisTaskAiRequest>(payload =>
                    payload.AnalysisSessionId == request.AnalysisSessionId &&
                    payload.TaskId == request.TaskId &&
                    payload.TaskType == request.TaskType &&
                    payload.Title == request.Title),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowAnalysisTaskAiResult
            {
                Summary = "AI 已补齐货位分配章节的业务说明。",
                LogMessage = "AI 已生成章节草稿并回填 chapter-brief。",
                ArtifactTitle = "AI 货位分配章节摘要",
                MarkdownDraft = "# 货位分配\n\n- AI 章节内容"
            });

        var runner = new WorkflowAnalysisTaskRunner(
            aiClient.Object,
            NullLogger<WorkflowAnalysisTaskRunner>.Instance);

        var result = await runner.ExecuteAsync(request);

        Assert.Equal("AI 已补齐货位分配章节的业务说明。", result.Summary);
        Assert.Contains("AI 已生成章节草稿并回填 chapter-brief。", result.LogMessages);
        Assert.Equal(2, result.Artifacts.Count);

        var chapterBrief = Assert.Single(result.Artifacts, item => item.ArtifactType == "chapter-brief");
        Assert.Equal("AI 货位分配章节摘要", chapterBrief.Title);
        Assert.Equal("markdown", chapterBrief.ContentFormat);
        Assert.Equal("# 货位分配\n\n- AI 章节内容", chapterBrief.Content);
        Assert.Equal("allocation", chapterBrief.Metadata["chapterKey"]);
        Assert.Equal("task-runner-ai", chapterBrief.Metadata["generatedBy"]);

        var flowchart = Assert.Single(result.Artifacts, item => item.ArtifactType == "flowchart");
        Assert.Equal("flowchart TD\nA[分配] --> B[下发]", flowchart.Content);
        Assert.Equal("allocation", flowchart.Metadata["chapterKey"]);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToDeterministicArtifacts_WhenAiFails()
    {
        var request = CreateChapterAnalysisRequest();
        var aiClient = new Mock<IWorkflowAnalysisTaskAiClient>(MockBehavior.Strict);
        aiClient
            .Setup(item => item.GenerateAsync(It.IsAny<WorkflowAnalysisTaskAiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI 未配置"));

        var runner = new WorkflowAnalysisTaskRunner(
            aiClient.Object,
            NullLogger<WorkflowAnalysisTaskRunner>.Instance);

        var result = await runner.ExecuteAsync(request);

        Assert.Equal(request.Summary, result.Summary);
        Assert.Equal(2, result.Artifacts.Count);
        Assert.Contains(result.LogMessages, item => item.Contains("AI 章节生成失败", StringComparison.Ordinal));
        Assert.Contains(result.LogMessages, item => item.Contains("已输出 2 个章节产物", StringComparison.Ordinal));

        var chapterBrief = Assert.Single(result.Artifacts, item => item.ArtifactType == "chapter-brief");
        Assert.Equal("货位分配章节摘要", chapterBrief.Title);
        Assert.Equal("# 货位分配", chapterBrief.Content);
        Assert.DoesNotContain("generatedBy", chapterBrief.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseAiResultForBranchDrilldown_WhenAiSucceeds()
    {
        var request = new WorkflowAnalysisTaskExecutionRequest
        {
            AnalysisSessionId = "session-1",
            TaskId = "task-branch-1",
            TaskType = "branch-drilldown",
            Title = "分支钻取：双深货位",
            Depth = 2,
            Summary = "深入分析双深货位分配条件。",
            FocusSymbols = ["AllocateDoubleDeep", "ValidateDoubleDeep"],
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["chapterKey"] = "allocation",
                ["branchRoot"] = "AllocateDoubleDeep"
            }
        };

        var aiClient = new Mock<IWorkflowAnalysisTaskAiClient>(MockBehavior.Strict);
        aiClient
            .Setup(item => item.GenerateAsync(
                It.Is<WorkflowAnalysisTaskAiRequest>(payload => payload.TaskType == "branch-drilldown"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowAnalysisTaskAiResult
            {
                Summary = "AI 已补齐双深货位分支钻取说明。",
                LogMessage = "AI 已生成分支摘要。",
                ArtifactTitle = "双深货位分支摘要",
                MarkdownDraft = "# 双深货位\n\n- AI 分支内容"
            });

        var runner = new WorkflowAnalysisTaskRunner(
            aiClient.Object,
            NullLogger<WorkflowAnalysisTaskRunner>.Instance);

        var result = await runner.ExecuteAsync(request);

        Assert.Equal("AI 已补齐双深货位分支钻取说明。", result.Summary);
        Assert.Contains("AI 已生成分支摘要。", result.LogMessages);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("branch-summary", artifact.ArtifactType);
        Assert.Equal("双深货位分支摘要", artifact.Title);
        Assert.Equal("# 双深货位\n\n- AI 分支内容", artifact.Content);
        Assert.Equal("allocation", artifact.Metadata["chapterKey"]);
        Assert.Equal("AllocateDoubleDeep", artifact.Metadata["branchRoot"]);
        Assert.Equal("task-runner-ai", artifact.Metadata["generatedBy"]);
    }

    private static WorkflowAnalysisTaskExecutionRequest CreateChapterAnalysisRequest()
    {
        return new WorkflowAnalysisTaskExecutionRequest
        {
            AnalysisSessionId = "session-1",
            TaskId = "task-chapter-1",
            TaskType = "chapter-analysis",
            Title = "章节深挖：货位分配",
            Depth = 1,
            Summary = "输出货位分配章节摘要与图种子。",
            FocusSymbols = ["AllocateMultiStationStacker"],
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["chapterKey"] = "allocation"
            },
            PlannedArtifacts =
            [
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
                }
            ]
        };
    }
}
