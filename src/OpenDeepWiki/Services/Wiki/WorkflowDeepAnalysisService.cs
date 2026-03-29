namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowDeepAnalysisService : IWorkflowDeepAnalysisService
{
    private readonly IWorkflowChapterSliceBuilder _chapterSliceBuilder;

    public WorkflowDeepAnalysisService(IWorkflowChapterSliceBuilder chapterSliceBuilder)
    {
        _chapterSliceBuilder = chapterSliceBuilder ?? throw new ArgumentNullException(nameof(chapterSliceBuilder));
    }

    public WorkflowDeepAnalysisResult Analyze(WorkflowDeepAnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Profile);
        ArgumentNullException.ThrowIfNull(input.Graph);

        var acpOptions = input.Profile.Acp ?? new WorkflowAcpOptions();
        var chapters = ResolveChapters(input.Profile, input.ChapterProfile);
        var tasks = new List<WorkflowDeepAnalysisTaskResult>
        {
            new()
            {
                TaskType = "planner",
                Title = "主线分析规划",
                Depth = 0,
                FocusSymbols = input.Profile.EntryRoots
                    .Concat(input.Profile.Analysis.RootSymbolNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                Summary = string.IsNullOrWhiteSpace(input.Objective)
                    ? "基于 profile root symbols 和 chapterProfiles 拆分主线、分支和收尾任务。"
                    : input.Objective.Trim(),
                Metadata = new Dictionary<string, string>
                {
                    ["plannerSource"] = "deterministic",
                    ["mode"] = "acp-planner",
                    ["chapterCount"] = chapters.Count.ToString()
                }
            }
        };
        var artifacts = new List<WorkflowDeepAnalysisArtifactResult>();
        var chapterSlices = new List<WorkflowChapterSlice>();

        foreach (var chapter in chapters)
        {
            var slice = _chapterSliceBuilder.Build(input.Graph, input.Profile, chapter);
            chapterSlices.Add(slice);
            tasks.Add(new WorkflowDeepAnalysisTaskResult
            {
                TaskType = "chapter-analysis",
                Title = $"章节深挖：{slice.ChapterTitle}",
                Depth = 1,
                FocusSymbols = slice.RootSymbolNames.Take(8).ToList(),
                Summary = BuildChapterSummary(slice),
                Metadata = new Dictionary<string, string>
                {
                    ["plannerSource"] = "deterministic",
                    ["chapterKey"] = slice.ChapterKey,
                    ["nodeCount"] = slice.NodeCount.ToString(),
                    ["edgeCount"] = slice.EdgeCount.ToString()
                }
            });

            if (chapter.AnalysisMode == WorkflowChapterAnalysisMode.Deep)
            {
                foreach (var branchTask in BuildBranchDrilldownTasks(slice, chapter, acpOptions.MaxBranchTasks))
                {
                    tasks.Add(branchTask);
                }
            }

            artifacts.Add(new WorkflowDeepAnalysisArtifactResult
            {
                ArtifactType = "chapter-brief",
                Title = $"{slice.ChapterTitle} 章节摘要",
                ContentFormat = "markdown",
                Content = BuildChapterBriefMarkdown(slice),
                Metadata = new Dictionary<string, string>
                {
                    ["chapterKey"] = slice.ChapterKey
                }
            });

            if (acpOptions.GenerateFlowchartSeed && chapter.IncludeFlowchart && !string.IsNullOrWhiteSpace(slice.FlowchartSeedMermaid))
            {
                artifacts.Add(new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "flowchart",
                    Title = $"{slice.ChapterTitle} 流程图种子",
                    ContentFormat = "mermaid",
                    Content = slice.FlowchartSeedMermaid,
                    Metadata = new Dictionary<string, string>
                    {
                        ["chapterKey"] = slice.ChapterKey
                    }
                });
            }

            if (acpOptions.GenerateMindMapSeed && chapter.IncludeMindmap && !string.IsNullOrWhiteSpace(slice.MindMapSeedMarkdown))
            {
                artifacts.Add(new WorkflowDeepAnalysisArtifactResult
                {
                    ArtifactType = "mindmap",
                    Title = $"{slice.ChapterTitle} 脑图种子",
                    ContentFormat = "markdown",
                    Content = slice.MindMapSeedMarkdown,
                    Metadata = new Dictionary<string, string>
                    {
                        ["chapterKey"] = slice.ChapterKey
                    }
                });
            }
        }

        artifacts.Insert(0, new WorkflowDeepAnalysisArtifactResult
        {
            ArtifactType = "analysis-overview",
            Title = "ACP 深挖总览",
            ContentFormat = "markdown",
            Content = BuildOverviewMarkdown(input, tasks, chapters),
            Metadata = new Dictionary<string, string>
            {
                ["chapterCount"] = chapters.Count.ToString(),
                ["taskCount"] = tasks.Count.ToString()
            }
        });

        var branchTaskCount = tasks.Count(task =>
            string.Equals(task.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase));

        return new WorkflowDeepAnalysisResult
        {
            Status = "Completed",
            Summary = input.ChapterProfile is null
                ? $"已完成整条业务流的 {chapters.Count} 个章节切片、{branchTaskCount} 个分支深挖任务，接下来会进入真实执行与文档回填阶段。"
                : $"已完成章节 {input.ChapterProfile.Title} 的切片与 {branchTaskCount} 个分支深挖任务，接下来会进入真实执行与文档回填阶段。",
            ChapterSlices = chapterSlices,
            Tasks = tasks,
            Artifacts = artifacts
        };
    }

    private static List<WorkflowDeepAnalysisTaskResult> BuildBranchDrilldownTasks(
        WorkflowChapterSlice slice,
        WorkflowChapterProfile chapter,
        int maxBranchTasks)
    {
        var limit = Math.Max(0, maxBranchTasks);
        if (limit == 0)
        {
            return [];
        }

        var tasks = new List<WorkflowDeepAnalysisTaskResult>();
        var selectedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootSymbols = new HashSet<string>(slice.RootSymbolNames, StringComparer.OrdinalIgnoreCase);

        foreach (var decision in slice.DecisionPoints)
        {
            if (string.IsNullOrWhiteSpace(decision.SymbolName) || !selectedSymbols.Add(decision.SymbolName))
            {
                continue;
            }

            tasks.Add(new WorkflowDeepAnalysisTaskResult
            {
                TaskType = "branch-drilldown",
                Title = $"分支钻取：{decision.SymbolName}",
                Depth = 2,
                FocusSymbols = [decision.SymbolName, .. decision.OutgoingSymbols.Take(4)],
                Summary = $"{decision.Summary} 分支去向：{string.Join(" / ", decision.OutgoingSymbols.Take(4))}",
                Metadata = new Dictionary<string, string>
                {
                    ["plannerSource"] = "deterministic",
                    ["chapterKey"] = slice.ChapterKey,
                    ["branchRoot"] = decision.SymbolName,
                    ["branchReason"] = "decision-point"
                }
            });

            if (tasks.Count >= limit)
            {
                return tasks;
            }
        }

        foreach (var symbol in slice.MissingMustExplainSymbols)
        {
            if (string.IsNullOrWhiteSpace(symbol) ||
                rootSymbols.Contains(symbol) ||
                !selectedSymbols.Add(symbol))
            {
                continue;
            }

            tasks.Add(CreateMustExplainBranchTask(slice, symbol, "missing-must-explain"));
            if (tasks.Count >= limit)
            {
                return tasks;
            }
        }

        foreach (var symbol in chapter.MustExplainSymbols)
        {
            if (string.IsNullOrWhiteSpace(symbol) ||
                rootSymbols.Contains(symbol) ||
                !selectedSymbols.Add(symbol))
            {
                continue;
            }

            tasks.Add(CreateMustExplainBranchTask(slice, symbol, "must-explain"));
            if (tasks.Count >= limit)
            {
                return tasks;
            }
        }

        foreach (var extensionPoint in slice.ExtensionPoints)
        {
            if (string.IsNullOrWhiteSpace(extensionPoint.SymbolName) ||
                rootSymbols.Contains(extensionPoint.SymbolName) ||
                !selectedSymbols.Add(extensionPoint.SymbolName))
            {
                continue;
            }

            tasks.Add(new WorkflowDeepAnalysisTaskResult
            {
                TaskType = "branch-drilldown",
                Title = $"分支钻取：{extensionPoint.SymbolName}",
                Depth = 2,
                FocusSymbols = [extensionPoint.SymbolName],
                Summary = $"{extensionPoint.Summary} 需要继续确认该扩展点在业务流中的触发条件、上下游调用和状态影响。",
                Metadata = new Dictionary<string, string>
                {
                    ["plannerSource"] = "deterministic",
                    ["chapterKey"] = slice.ChapterKey,
                    ["branchRoot"] = extensionPoint.SymbolName,
                    ["branchReason"] = "extension-point"
                }
            });
            if (tasks.Count >= limit)
            {
                return tasks;
            }
        }

        return tasks;
    }

    private static WorkflowDeepAnalysisTaskResult CreateMustExplainBranchTask(
        WorkflowChapterSlice slice,
        string symbolName,
        string reason)
    {
        return new WorkflowDeepAnalysisTaskResult
        {
            TaskType = "branch-drilldown",
            Title = $"分支钻取：{symbolName}",
            Depth = 2,
            FocusSymbols = [symbolName],
            Summary = $"必须补充说明符号 {symbolName} 的实际实现、调用链、触发条件和业务差异。",
            Metadata = new Dictionary<string, string>
            {
                ["plannerSource"] = "deterministic",
                ["chapterKey"] = slice.ChapterKey,
                ["branchRoot"] = symbolName,
                ["branchReason"] = reason
            }
        };
    }

    private static List<WorkflowChapterProfile> ResolveChapters(
        RepositoryWorkflowProfile profile,
        WorkflowChapterProfile? requestedChapter)
    {
        if (requestedChapter is not null)
        {
            return [requestedChapter];
        }

        if (profile.ChapterProfiles.Count > 0)
        {
            return profile.ChapterProfiles
                .OrderByDescending(chapter => chapter.AnalysisMode == WorkflowChapterAnalysisMode.Deep)
                .ThenBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return
        [
            new WorkflowChapterProfile
            {
                Key = "main-flow",
                Title = "主线流程",
                AnalysisMode = WorkflowChapterAnalysisMode.Deep,
                RootSymbolNames = profile.EntryRoots.Concat(profile.Analysis.RootSymbolNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MustExplainSymbols = profile.Analysis.MustExplainSymbols.ToList(),
                RequiredSections = profile.DocumentPreferences.RequiredSections.ToList(),
                OutputArtifacts = ["markdown", "flowchart", "mindmap"],
                DepthBudget = profile.Analysis.DepthBudget,
                MaxNodes = Math.Min(profile.Analysis.MaxNodes, 32),
                IncludeFlowchart = true,
                IncludeMindmap = true
            }
        ];
    }

    private static string BuildOverviewMarkdown(
        WorkflowDeepAnalysisInput input,
        IReadOnlyCollection<WorkflowDeepAnalysisTaskResult> tasks,
        IReadOnlyCollection<WorkflowChapterProfile> chapters)
    {
        var scopeLabel = input.ChapterProfile is null
            ? "整条业务流"
            : $"章节聚焦：{input.ChapterProfile.Title}";
        var lines = new List<string>
        {
            "# ACP 深挖总览",
            $"范围：{scopeLabel}",
            $"目标：{(string.IsNullOrWhiteSpace(input.Objective) ? input.Profile.Acp.Objective : input.Objective)}",
            $"章节数：{chapters.Count}",
            $"任务数：{tasks.Count}",
            "",
            "## 章节清单"
        };

        foreach (var chapter in chapters)
        {
            lines.Add($"- {chapter.Title} ({chapter.Key})");
        }

        lines.Add(string.Empty);
        lines.Add("## 任务拆分");
        foreach (var task in tasks)
        {
            lines.Add($"- [{task.TaskType}] {task.Title}: {task.Summary}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildChapterSummary(WorkflowChapterSlice slice)
    {
        return $"包含 {slice.NodeCount} 个节点、{slice.EdgeCount} 条边，" +
               $"{slice.DecisionPoints.Count} 个关键分支点，" +
               $"{slice.StateChanges.Count} 条状态/落库变化。";
    }

    private static string BuildChapterBriefMarkdown(WorkflowChapterSlice slice)
    {
        var lines = new List<string>
        {
            $"# {slice.ChapterTitle}",
            $"节点数：{slice.NodeCount}",
            $"边数：{slice.EdgeCount}",
            "",
            "## Root Symbols",
            $"- {string.Join(" / ", slice.RootSymbolNames)}",
            "",
            "## 决策点"
        };

        if (slice.DecisionPoints.Count == 0)
        {
            lines.Add("- 暂未发现明显多分支节点。");
        }
        else
        {
            foreach (var decision in slice.DecisionPoints)
            {
                lines.Add($"- {decision.SymbolName}: {string.Join(" / ", decision.OutgoingSymbols.Take(6))}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("## 状态变更");
        if (slice.StateChanges.Count == 0)
        {
            lines.Add("- 暂未发现状态或持久化写入边。");
        }
        else
        {
            foreach (var change in slice.StateChanges.Take(12))
            {
                lines.Add($"- {change.FromSymbol} -> {change.ToSymbol} ({change.ChangeType})");
            }
        }

        lines.Add(string.Empty);
        lines.Add("## 覆盖缺口");
        if (slice.MissingMustExplainSymbols.Count == 0)
        {
            lines.Add("- 当前章节已经覆盖所要求的 must-explain symbols。");
        }
        else
        {
            foreach (var symbol in slice.MissingMustExplainSymbols)
            {
                lines.Add($"- {symbol}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
