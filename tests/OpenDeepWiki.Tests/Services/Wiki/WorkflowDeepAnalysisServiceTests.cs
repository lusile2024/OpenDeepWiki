using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowDeepAnalysisServiceTests
{
    [Fact]
    public void Analyze_ShouldIncludeAllChapterProfiles_WhenNoChapterRequested()
    {
        var chapterSlices = new Dictionary<string, WorkflowChapterSlice>(StringComparer.OrdinalIgnoreCase)
        {
            ["allocation"] = new()
            {
                ChapterKey = "allocation",
                ChapterTitle = "货位分配",
                RootSymbolNames = ["AllocateMultiStationStacker"],
                NodeCount = 8,
                EdgeCount = 10,
                DecisionPoints =
                [
                    new WorkflowChapterDecisionPoint
                    {
                        SymbolName = "AllocateMultiStationStacker",
                        OutgoingSymbols = ["AllocateDoubleDeep", "AllocateMultiDepth"],
                        Summary = "按货位深度拆分上架策略。"
                    }
                ],
                StateChanges =
                [
                    new WorkflowChapterStateChange
                    {
                        FromSymbol = "AllocateMultiStationStacker",
                        ToSymbol = "WcsTaskRepository.UpdateAsync",
                        ChangeType = "Writes"
                    }
                ],
                FlowchartSeedMermaid = "flowchart TD\nA[分配] --> B[任务下发]",
                MindMapSeedMarkdown = "- 货位分配\n  - 双深\n  - 多深"
            },
            ["closing"] = new()
            {
                ChapterKey = "closing",
                ChapterTitle = "任务收尾",
                RootSymbolNames = ["FinishMoveInTaskAsync"],
                NodeCount = 5,
                EdgeCount = 6,
                StateChanges =
                [
                    new WorkflowChapterStateChange
                    {
                        FromSymbol = "FinishMoveInTaskAsync",
                        ToSymbol = "InventoryRepository.UpdateAsync",
                        ChangeType = "UpdatesStatus"
                    }
                ],
                FlowchartSeedMermaid = "flowchart TD\nA[回调] --> B[库存更新]",
                MindMapSeedMarkdown = "- 任务收尾\n  - 库存更新"
            }
        };

        var builder = new RecordingChapterSliceBuilder(chapterSlices);
        var service = new WorkflowDeepAnalysisService(builder);
        var profile = CreateProfile();

        var result = service.Analyze(new WorkflowDeepAnalysisInput
        {
            Profile = profile,
            Graph = new WorkflowSemanticGraph(),
            Objective = "深挖站台入库主线与分支"
        });

        Assert.Equal(["allocation", "closing"], builder.RequestedChapterKeys);
        Assert.Contains("已完成整条业务流的 2 个章节切片", result.Summary);
        Assert.Equal(2, result.ChapterSlices.Count);
        Assert.Equal(2, result.Tasks.Count(task => task.TaskType == "chapter-analysis"));
        Assert.Contains(result.Tasks, task => task.TaskType == "branch-drilldown" && task.Metadata["chapterKey"] == "allocation");
        Assert.All(result.Tasks, task => Assert.Equal("deterministic", task.Metadata["plannerSource"]));

        var overview = Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "analysis-overview");
        Assert.Contains("范围：整条业务流", overview.Content);
        Assert.Contains("货位分配 (allocation)", overview.Content);
        Assert.Contains("任务收尾 (closing)", overview.Content);
    }

    [Fact]
    public void Analyze_ShouldOnlyUseRequestedChapter_WhenChapterProvided()
    {
        var chapterSlices = new Dictionary<string, WorkflowChapterSlice>(StringComparer.OrdinalIgnoreCase)
        {
            ["allocation"] = new()
            {
                ChapterKey = "allocation",
                ChapterTitle = "货位分配",
                RootSymbolNames = ["AllocateMultiStationStacker"],
                NodeCount = 8,
                EdgeCount = 10
            },
            ["closing"] = new()
            {
                ChapterKey = "closing",
                ChapterTitle = "任务收尾",
                RootSymbolNames = ["FinishMoveInTaskAsync"],
                NodeCount = 5,
                EdgeCount = 6,
                DecisionPoints =
                [
                    new WorkflowChapterDecisionPoint
                    {
                        SymbolName = "FinishMoveInTaskAsync",
                        OutgoingSymbols = ["UpdateInventoryAsync"],
                        Summary = "按回调结果执行收尾逻辑。"
                    }
                ]
            }
        };

        var builder = new RecordingChapterSliceBuilder(chapterSlices);
        var service = new WorkflowDeepAnalysisService(builder);
        var profile = CreateProfile();
        var requestedChapter = profile.ChapterProfiles.Single(chapter => chapter.Key == "closing");

        var result = service.Analyze(new WorkflowDeepAnalysisInput
        {
            Profile = profile,
            Graph = new WorkflowSemanticGraph(),
            ChapterProfile = requestedChapter,
            Objective = "只补任务收尾章节"
        });

        Assert.Equal(["closing"], builder.RequestedChapterKeys);
        Assert.Contains("章节 任务收尾 的切片", result.Summary);
        Assert.Single(result.ChapterSlices);
        Assert.Equal(1, result.Tasks.Count(task => task.TaskType == "chapter-analysis"));
        Assert.DoesNotContain(result.Tasks, task => task.Metadata.GetValueOrDefault("chapterKey") == "allocation");

        var overview = Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "analysis-overview");
        Assert.Contains("范围：章节聚焦：任务收尾", overview.Content);
        Assert.DoesNotContain("货位分配 (allocation)", overview.Content);
        Assert.Contains("任务收尾 (closing)", overview.Content);
    }

    [Fact]
    public void Analyze_ShouldCreateBranchTasksFromMustExplainSymbols_WhenDecisionPointsAreMissing()
    {
        var chapterSlices = new Dictionary<string, WorkflowChapterSlice>(StringComparer.OrdinalIgnoreCase)
        {
            ["allocation"] = new()
            {
                ChapterKey = "allocation",
                ChapterTitle = "货位分配",
                RootSymbolNames = ["AllocateMultiStationStacker"],
                IncludedSymbols = ["AllocateMultiStationStacker"],
                MissingMustExplainSymbols = ["AllocateDoubleDeep"],
                NodeCount = 4,
                EdgeCount = 3
            },
            ["closing"] = new()
            {
                ChapterKey = "closing",
                ChapterTitle = "任务收尾",
                RootSymbolNames = ["FinishMoveInTaskAsync"],
                NodeCount = 2,
                EdgeCount = 1
            }
        };

        var builder = new RecordingChapterSliceBuilder(chapterSlices);
        var service = new WorkflowDeepAnalysisService(builder);
        var profile = CreateProfile();

        var result = service.Analyze(new WorkflowDeepAnalysisInput
        {
            Profile = profile,
            Graph = new WorkflowSemanticGraph(),
            Objective = "深挖 must explain 符号"
        });

        var branchTask = Assert.Single(result.Tasks, task =>
            task.TaskType == "branch-drilldown" &&
            task.Metadata.GetValueOrDefault("branchRoot") == "AllocateDoubleDeep");
        Assert.Equal("missing-must-explain", branchTask.Metadata["branchReason"]);
        Assert.Equal("deterministic", branchTask.Metadata["plannerSource"]);
        Assert.Contains("必须补充说明符号 AllocateDoubleDeep", branchTask.Summary);
        var mustExplainTask = Assert.Single(result.Tasks, task =>
            task.TaskType == "branch-drilldown" &&
            task.Metadata.GetValueOrDefault("branchRoot") == "AllocateMultiDepth");
        Assert.Equal("must-explain", mustExplainTask.Metadata["branchReason"]);
        Assert.Contains("2 个分支深挖任务", result.Summary);
    }

    private static RepositoryWorkflowProfile CreateProfile()
    {
        return new RepositoryWorkflowProfile
        {
            Key = "wcs-stn-move-in",
            Name = "处理站台入库申请",
            EntryRoots = ["WcsRequestWmsExecutorJob.Execute", "WcsStnMoveInExecutor.ExecuteAsync"],
            Analysis = new WorkflowProfileAnalysisOptions
            {
                RootSymbolNames = ["WcsStnMoveInExecutor.ExecuteAsync"],
                MustExplainSymbols = ["AllocateMultiStationStacker", "FinishMoveInTaskAsync"],
                DepthBudget = 6,
                MaxNodes = 64
            },
            DocumentPreferences = new WorkflowDocumentPreferences
            {
                RequiredSections = ["货位分配整体流程概览", "任务收尾处理逻辑详细分析"]
            },
            Acp = new WorkflowAcpOptions
            {
                Objective = "深挖业务流主线与分支",
                MaxBranchTasks = 4,
                GenerateFlowchartSeed = true,
                GenerateMindMapSeed = true
            },
            ChapterProfiles =
            [
                new WorkflowChapterProfile
                {
                    Key = "allocation",
                    Title = "货位分配",
                    AnalysisMode = WorkflowChapterAnalysisMode.Deep,
                    RootSymbolNames = ["AllocateMultiStationStacker"],
                    MustExplainSymbols = ["AllocateDoubleDeep", "AllocateMultiDepth"],
                    IncludeFlowchart = true,
                    IncludeMindmap = true
                },
                new WorkflowChapterProfile
                {
                    Key = "closing",
                    Title = "任务收尾",
                    AnalysisMode = WorkflowChapterAnalysisMode.Standard,
                    RootSymbolNames = ["FinishMoveInTaskAsync"],
                    MustExplainSymbols = ["UpdateInventoryAsync"],
                    IncludeFlowchart = true,
                    IncludeMindmap = true
                }
            ]
        };
    }

    private sealed class RecordingChapterSliceBuilder : IWorkflowChapterSliceBuilder
    {
        private readonly IReadOnlyDictionary<string, WorkflowChapterSlice> _slices;

        public RecordingChapterSliceBuilder(IReadOnlyDictionary<string, WorkflowChapterSlice> slices)
        {
            _slices = slices;
        }

        public List<string> RequestedChapterKeys { get; } = [];

        public WorkflowChapterSlice Build(
            WorkflowSemanticGraph graph,
            RepositoryWorkflowProfile profile,
            WorkflowChapterProfile? chapterProfile = null)
        {
            var chapterKey = chapterProfile?.Key ?? "main-flow";
            RequestedChapterKeys.Add(chapterKey);

            if (_slices.TryGetValue(chapterKey, out var slice))
            {
                return slice;
            }

            throw new KeyNotFoundException($"Missing slice for chapter {chapterKey}.");
        }
    }
}
