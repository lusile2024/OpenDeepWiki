using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisTaskRunner : IWorkflowAnalysisTaskRunner
{
    private const string AiGeneratedBy = "task-runner-ai";
    private const string PlannedGeneratedBy = "task-runner-plan";
    private readonly IWorkflowAnalysisTaskAiClient? _taskAiClient;
    private readonly IWorkflowAnalysisTaskRuntimeReporter? _runtimeReporter;
    private readonly ILogger<WorkflowAnalysisTaskRunner> _logger;

    public WorkflowAnalysisTaskRunner()
    {
        _logger = NullLogger<WorkflowAnalysisTaskRunner>.Instance;
    }

    public WorkflowAnalysisTaskRunner(
        IWorkflowAnalysisTaskAiClient taskAiClient,
        ILogger<WorkflowAnalysisTaskRunner> logger)
        : this(taskAiClient, logger, null)
    {
    }

    public WorkflowAnalysisTaskRunner(
        IWorkflowAnalysisTaskAiClient taskAiClient,
        ILogger<WorkflowAnalysisTaskRunner> logger,
        IWorkflowAnalysisTaskRuntimeReporter? runtimeReporter)
    {
        _taskAiClient = taskAiClient ?? throw new ArgumentNullException(nameof(taskAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runtimeReporter = runtimeReporter;
    }

    public async Task<WorkflowAnalysisTaskExecutionResult> ExecuteAsync(
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new WorkflowAnalysisTaskExecutionResult
        {
            Summary = request.Summary
        };

        switch (request.TaskType)
        {
            case "planner":
                result.LogMessages.Add(string.IsNullOrWhiteSpace(request.Summary)
                    ? "主线规划完成，已生成章节与分支执行计划。"
                    : request.Summary!);
                break;
            case "chapter-analysis":
                await ExecuteChapterAnalysisAsync(result, request, cancellationToken);
                break;
            case "branch-drilldown":
                await ExecuteBranchDrilldownAsync(result, request, cancellationToken);
                break;
            default:
                result.LogMessages.Add(
                    string.IsNullOrWhiteSpace(request.Summary)
                        ? $"任务 {request.Title} 已完成。"
                        : request.Summary!);
                result.Artifacts.AddRange(request.PlannedArtifacts.Select(CloneArtifact));
                break;
        }

        return result;
    }

    private static WorkflowDeepAnalysisArtifactResult CloneArtifact(WorkflowDeepAnalysisArtifactResult artifact)
    {
        return new WorkflowDeepAnalysisArtifactResult
        {
            ArtifactType = artifact.ArtifactType,
            Title = artifact.Title,
            ContentFormat = artifact.ContentFormat,
            Content = artifact.Content,
            Metadata = artifact.Metadata.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task ExecuteChapterAnalysisAsync(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (await TryApplyAiChapterResultAsync(result, request, cancellationToken))
        {
            return;
        }

        result.Artifacts.AddRange(request.PlannedArtifacts.Select(CloneArtifact));
        result.LogMessages.Add(
            $"章节任务完成，已输出 {request.PlannedArtifacts.Count} 个章节产物。");
    }

    private async Task ExecuteBranchDrilldownAsync(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (await TryApplyAiBranchResultAsync(result, request, cancellationToken))
        {
            return;
        }

        result.Artifacts.Add(BuildBranchSummaryArtifact(request));
        result.LogMessages.Add(
            $"分支钻取完成，聚焦符号：{FormatSymbols(request.FocusSymbols)}");
    }

    private async Task<bool> TryApplyAiChapterResultAsync(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (_taskAiClient is null)
        {
            return false;
        }

        try
        {
            await ReportAiLogAsync(
                request,
                "ai",
                $"task_runner 开始调用 AI 生成章节摘要：{request.Title}",
                cancellationToken);
            var aiResult = await _taskAiClient.GenerateAsync(
                CreateAiRequest(request),
                cancellationToken);
            await ReportAiLogAsync(
                request,
                "ai",
                $"task_runner 已收到 AI 返回，开始回填章节产物：{ResolveArtifactTitle(aiResult.ArtifactTitle, request.Title)}",
                cancellationToken);
            ApplyAiChapterResult(result, request, aiResult);
            await ReportAiLogAsync(
                request,
                "ai",
                BuildArtifactDiffLogMessage(
                    request,
                    result.Artifacts.Where(item => string.Equals(item.ArtifactType, "chapter-brief", StringComparison.OrdinalIgnoreCase)).ToList(),
                    "章节摘要"),
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI chapter analysis failed for task {TaskId}", request.TaskId);
            result.LogMessages.Add($"AI 章节生成失败，已回退到本地产物：{ex.Message}");
            await ReportAiLogAsync(
                request,
                "warning",
                $"task_runner AI 章节生成失败，已回退到本地产物：{ex.Message}",
                cancellationToken);
            return false;
        }
    }

    private async Task<bool> TryApplyAiBranchResultAsync(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (_taskAiClient is null)
        {
            return false;
        }

        try
        {
            await ReportAiLogAsync(
                request,
                "ai",
                $"task_runner 开始调用 AI 生成分支摘要：{request.Title}",
                cancellationToken);
            var aiResult = await _taskAiClient.GenerateAsync(
                CreateAiRequest(request),
                cancellationToken);
            await ReportAiLogAsync(
                request,
                "ai",
                $"task_runner 已收到 AI 返回，开始回填分支产物：{ResolveArtifactTitle(aiResult.ArtifactTitle, $"{request.Title} 摘要")}",
                cancellationToken);
            ApplyAiBranchResult(result, request, aiResult);
            await ReportAiLogAsync(
                request,
                "ai",
                BuildArtifactDiffLogMessage(request, result.Artifacts, "分支摘要"),
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI branch analysis failed for task {TaskId}", request.TaskId);
            result.LogMessages.Add($"AI 分支生成失败，已回退到本地产物：{ex.Message}");
            await ReportAiLogAsync(
                request,
                "warning",
                $"task_runner AI 分支生成失败，已回退到本地产物：{ex.Message}",
                cancellationToken);
            return false;
        }
    }

    private static WorkflowAnalysisTaskAiRequest CreateAiRequest(WorkflowAnalysisTaskExecutionRequest request)
    {
        return new WorkflowAnalysisTaskAiRequest
        {
            AnalysisSessionId = request.AnalysisSessionId,
            TaskId = request.TaskId,
            TaskType = request.TaskType,
            Title = request.Title,
            Depth = request.Depth,
            Summary = request.Summary,
            FocusSymbols = request.FocusSymbols.ToList(),
            Metadata = request.Metadata.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase),
            PlannedArtifacts = request.PlannedArtifacts
                .Select(CloneArtifact)
                .ToList()
        };
    }

    private static void ApplyAiChapterResult(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        WorkflowAnalysisTaskAiResult aiResult)
    {
        result.Summary = string.IsNullOrWhiteSpace(aiResult.Summary)
            ? request.Summary
            : aiResult.Summary;

        var artifacts = request.PlannedArtifacts.Select(CloneArtifact).ToList();
        AnnotatePlannedArtifacts(artifacts);
        var chapterBrief = artifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactType, "chapter-brief", StringComparison.OrdinalIgnoreCase));

        if (chapterBrief is null)
        {
            chapterBrief = new WorkflowDeepAnalysisArtifactResult
            {
                ArtifactType = "chapter-brief",
                Title = ResolveArtifactTitle(aiResult.ArtifactTitle, request.Title),
                ContentFormat = "markdown",
                Content = aiResult.MarkdownDraft,
                Metadata = CreateGeneratedMetadata(request.Metadata, "created")
            };
            artifacts.Insert(0, chapterBrief);
        }
        else
        {
            var baseline = CloneArtifact(chapterBrief);
            chapterBrief.Title = ResolveArtifactTitle(aiResult.ArtifactTitle, chapterBrief.Title, request.Title);
            chapterBrief.ContentFormat = "markdown";
            chapterBrief.Content = aiResult.MarkdownDraft;
            chapterBrief.Metadata = CreateGeneratedMetadata(chapterBrief.Metadata, "updated");
            AttachBaselineMetadata(chapterBrief.Metadata, baseline);
        }

        result.Artifacts.AddRange(artifacts);
        result.LogMessages.Add(
            string.IsNullOrWhiteSpace(aiResult.LogMessage)
                ? "AI 已生成章节草稿并回填章节摘要。"
                : aiResult.LogMessage);
    }

    private static void ApplyAiBranchResult(
        WorkflowAnalysisTaskExecutionResult result,
        WorkflowAnalysisTaskExecutionRequest request,
        WorkflowAnalysisTaskAiResult aiResult)
    {
        result.Summary = string.IsNullOrWhiteSpace(aiResult.Summary)
            ? request.Summary
            : aiResult.Summary;

        var artifacts = request.PlannedArtifacts.Select(CloneArtifact).ToList();
        AnnotatePlannedArtifacts(artifacts);
        var branchArtifact = artifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactType, "branch-summary", StringComparison.OrdinalIgnoreCase));

        if (branchArtifact is null)
        {
            branchArtifact = new WorkflowDeepAnalysisArtifactResult
            {
                ArtifactType = "branch-summary",
                Title = ResolveArtifactTitle(aiResult.ArtifactTitle, $"{request.Title} 摘要"),
                ContentFormat = "markdown",
                Content = aiResult.MarkdownDraft,
                Metadata = CreateGeneratedMetadata(request.Metadata, "created")
            };
            artifacts.Add(branchArtifact);
        }
        else
        {
            var baseline = CloneArtifact(branchArtifact);
            branchArtifact.Title = ResolveArtifactTitle(aiResult.ArtifactTitle, branchArtifact.Title, $"{request.Title} 摘要");
            branchArtifact.ContentFormat = "markdown";
            branchArtifact.Content = aiResult.MarkdownDraft;
            branchArtifact.Metadata = CreateGeneratedMetadata(branchArtifact.Metadata, "updated");
            AttachBaselineMetadata(branchArtifact.Metadata, baseline);
        }

        result.Artifacts.AddRange(artifacts);
        result.LogMessages.Add(
            string.IsNullOrWhiteSpace(aiResult.LogMessage)
                ? "AI 已生成分支钻取摘要。"
                : aiResult.LogMessage);
    }

    private async Task ReportAiLogAsync(
        WorkflowAnalysisTaskExecutionRequest request,
        string level,
        string message,
        CancellationToken cancellationToken)
    {
        if (_runtimeReporter is null)
        {
            return;
        }

        await _runtimeReporter.ReportLogAsync(
            request.AnalysisSessionId,
            request.TaskId,
            level,
            message,
            cancellationToken);
    }

    private static void AnnotatePlannedArtifacts(List<WorkflowDeepAnalysisArtifactResult> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            artifact.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!artifact.Metadata.ContainsKey("generatedBy"))
            {
                artifact.Metadata["generatedBy"] = PlannedGeneratedBy;
            }

            if (!artifact.Metadata.ContainsKey("diffKind"))
            {
                artifact.Metadata["diffKind"] = "unchanged";
            }
        }
    }

    private static Dictionary<string, string> CreateGeneratedMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string diffKind)
    {
        var cloned = metadata.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        cloned["generatedBy"] = AiGeneratedBy;
        cloned["diffKind"] = diffKind;
        return cloned;
    }

    private static void AttachBaselineMetadata(
        IDictionary<string, string> metadata,
        WorkflowDeepAnalysisArtifactResult baseline)
    {
        metadata["baselineArtifactType"] = baseline.ArtifactType;
        metadata["baselineTitle"] = baseline.Title;
        metadata["baselineContentFormat"] = baseline.ContentFormat;
        metadata["baselineContent"] = baseline.Content;
    }

    private static string BuildArtifactDiffLogMessage(
        WorkflowAnalysisTaskExecutionRequest request,
        IReadOnlyCollection<WorkflowDeepAnalysisArtifactResult> artifacts,
        string artifactLabel)
    {
        var changedArtifacts = artifacts
            .Where(item =>
                item.Metadata.TryGetValue("generatedBy", out var generatedBy) &&
                string.Equals(generatedBy, AiGeneratedBy, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var diffKind = item.Metadata.TryGetValue("diffKind", out var value) ? value : "updated";
                return $"{item.Title} ({diffKind})";
            })
            .ToList();

        return changedArtifacts.Count == 0
            ? $"task_runner 未识别到新的 {artifactLabel} 差异：{request.Title}"
            : $"task_runner 已生成 {artifactLabel} 差异：{string.Join(" / ", changedArtifacts)}";
    }

    private static string ResolveArtifactTitle(params string?[] candidates)
    {
        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
            ?? "分析摘要";
    }

    private static WorkflowDeepAnalysisArtifactResult BuildBranchSummaryArtifact(
        WorkflowAnalysisTaskExecutionRequest request)
    {
        var metadata = request.Metadata.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        metadata["generatedBy"] = "task-runner";
        metadata["diffKind"] = "created";

        return new WorkflowDeepAnalysisArtifactResult
        {
            ArtifactType = "branch-summary",
            Title = $"{request.Title} 摘要",
            ContentFormat = "markdown",
            Content = BuildBranchSummaryMarkdown(request),
            Metadata = metadata
        };
    }

    private static string BuildBranchSummaryMarkdown(WorkflowAnalysisTaskExecutionRequest request)
    {
        var lines = new List<string>
        {
            $"# {request.Title}"
        };

        if (request.Metadata.TryGetValue("chapterKey", out var chapterKey) &&
            !string.IsNullOrWhiteSpace(chapterKey))
        {
            lines.Add($"章节：{chapterKey}");
        }

        if (request.Metadata.TryGetValue("branchRoot", out var branchRoot) &&
            !string.IsNullOrWhiteSpace(branchRoot))
        {
            lines.Add($"分支根符号：{branchRoot}");
        }

        lines.Add(string.Empty);
        lines.Add("## 任务摘要");
        lines.Add(string.IsNullOrWhiteSpace(request.Summary) ? "- (无摘要)" : $"- {request.Summary}");
        lines.Add(string.Empty);
        lines.Add("## 聚焦符号");

        if (request.FocusSymbols.Count == 0)
        {
            lines.Add("- (无)");
        }
        else
        {
            foreach (var symbol in request.FocusSymbols)
            {
                lines.Add($"- {symbol}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatSymbols(IReadOnlyList<string> focusSymbols)
    {
        return focusSymbols.Count == 0 ? "(无)" : string.Join(" / ", focusSymbols);
    }
}
