using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisExecutionService : IWorkflowAnalysisExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IContext _context;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IWorkflowDiscoveryService _workflowDiscoveryService;
    private readonly IWorkflowDeepAnalysisService _workflowDeepAnalysisService;
    private readonly IWorkflowAnalysisPlannerHintService _plannerHintService;
    private readonly IWorkflowAnalysisTaskRunner _taskRunner;
    private readonly IRepositoryWorkflowConfigService _workflowConfigService;
    private readonly IWikiGenerator _wikiGenerator;
    private readonly ILogger<WorkflowAnalysisExecutionService> _logger;

    public WorkflowAnalysisExecutionService(
        IContext context,
        IRepositoryAnalyzer repositoryAnalyzer,
        IWorkflowDiscoveryService workflowDiscoveryService,
        IWorkflowDeepAnalysisService workflowDeepAnalysisService,
        IWorkflowAnalysisPlannerHintService plannerHintService,
        IWorkflowAnalysisTaskRunner taskRunner,
        IRepositoryWorkflowConfigService workflowConfigService,
        IWikiGenerator wikiGenerator,
        ILogger<WorkflowAnalysisExecutionService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositoryAnalyzer = repositoryAnalyzer ?? throw new ArgumentNullException(nameof(repositoryAnalyzer));
        _workflowDiscoveryService = workflowDiscoveryService ?? throw new ArgumentNullException(nameof(workflowDiscoveryService));
        _workflowDeepAnalysisService = workflowDeepAnalysisService ?? throw new ArgumentNullException(nameof(workflowDeepAnalysisService));
        _plannerHintService = plannerHintService ?? throw new ArgumentNullException(nameof(plannerHintService));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _workflowConfigService = workflowConfigService ?? throw new ArgumentNullException(nameof(workflowConfigService));
        _wikiGenerator = wikiGenerator ?? throw new ArgumentNullException(nameof(wikiGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(string analysisSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisSessionId);

        var session = await _context.WorkflowAnalysisSessions
            .FirstOrDefaultAsync(item => item.Id == analysisSessionId && !item.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("分析会话不存在。");

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(item => item.Id == session.RepositoryId && !item.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("仓库不存在。");
        var templateSession = await _context.WorkflowTemplateSessions
            .FirstOrDefaultAsync(item => item.Id == session.WorkflowTemplateSessionId && !item.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("模板会话不存在。");
        var branch = string.IsNullOrWhiteSpace(templateSession.BranchId)
            ? await _context.RepositoryBranches
                .AsNoTracking()
                .Where(item => item.RepositoryId == repository.Id && !item.IsDeleted)
                .OrderBy(item => item.BranchName == "main" ? 0 : 1)
                .ThenBy(item => item.BranchName)
                .FirstOrDefaultAsync(cancellationToken)
            : await _context.RepositoryBranches
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.RepositoryId == repository.Id &&
                            item.Id == templateSession.BranchId &&
                            !item.IsDeleted,
                    cancellationToken);
        var draft = await LoadDraftAsync(templateSession.Id, session.DraftVersionNumber, cancellationToken)
                    ?? CreateDefaultDraft(session.ProfileKey);
        var chapter = string.IsNullOrWhiteSpace(session.ChapterKey)
            ? null
            : draft.ChapterProfiles.FirstOrDefault(item =>
                string.Equals(item.Key, session.ChapterKey, StringComparison.OrdinalIgnoreCase));
        var branchName = branch?.BranchName ?? templateSession.BranchName ?? "main";

        RepositoryWorkspace? workspace = null;
        try
        {
            session.StartedAt ??= DateTime.UtcNow;
            AppendLog(session, null, "info", "开始准备仓库工作区。");
            UpdateSessionProgress(session, null, "准备工作区");
            await _context.SaveChangesAsync(cancellationToken);

            workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
                repository,
                branchName,
                branch?.LastCommitId,
                cancellationToken);

            AppendLog(session, null, "info", "工作区准备完成，开始构建语义图。");
            UpdateSessionProgress(session, null, "构建 Roslyn 语义图");
            await _context.SaveChangesAsync(cancellationToken);

            var discovery = await _workflowDiscoveryService.DiscoverAsync(workspace, cancellationToken: cancellationToken);
            AppendLog(session, null, "info", "语义图构建完成，开始生成章节切片与任务拆分。");
            UpdateSessionProgress(session, null, "生成 ACP 任务拆分");
            await _context.SaveChangesAsync(cancellationToken);

            var analysis = _workflowDeepAnalysisService.Analyze(new WorkflowDeepAnalysisInput
            {
                Profile = draft,
                Graph = discovery.Graph,
                ChapterProfile = chapter,
                Objective = session.Objective
            });
            await TryApplyPlannerHintsAsync(session, draft, analysis, cancellationToken);

            var maxParallelism = Math.Clamp(draft.Acp.MaxParallelTasks, 1, 8);
            var overviewArtifact = analysis.Artifacts
                .FirstOrDefault(item => string.Equals(item.ArtifactType, "analysis-overview", StringComparison.OrdinalIgnoreCase));
            var plannedTasks = BuildPlannedTasks(session.Id, analysis);

            session.TotalTasks = plannedTasks.Count;
            session.CompletedTasks = 0;
            session.FailedTasks = 0;
            session.PendingTaskCount = plannedTasks.Count;
            session.RunningTaskCount = 0;
            session.CurrentTaskId = null;
            session.Summary = analysis.Summary;
            session.ProgressMessage = plannedTasks.Count == 0 ? "没有可执行的分析任务" : "等待任务执行";
            session.LastActivityAt = DateTime.UtcNow;

            await RemoveExistingSessionDataAsync(session.Id, cancellationToken);

            foreach (var plannedTask in plannedTasks)
            {
                _context.WorkflowAnalysisTasks.Add(plannedTask.Entity);
            }

            AppendLog(session, null, "info", $"已生成 {plannedTasks.Count} 个 ACP 任务，最大并行度 {maxParallelism}。");
            await _context.SaveChangesAsync(cancellationToken);

            await ExecuteTaskPhaseAsync(
                session,
                plannedTasks.Where(item => string.Equals(item.Plan.TaskType, "planner", StringComparison.OrdinalIgnoreCase)).ToList(),
                "主线规划",
                1,
                cancellationToken);

            await ExecuteTaskPhaseAsync(
                session,
                plannedTasks.Where(item => string.Equals(item.Plan.TaskType, "chapter-analysis", StringComparison.OrdinalIgnoreCase)).ToList(),
                "章节深挖",
                maxParallelism,
                cancellationToken);

            await ExecuteTaskPhaseAsync(
                session,
                plannedTasks.Where(item => string.Equals(item.Plan.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase)).ToList(),
                "分支钻取",
                maxParallelism,
                cancellationToken);

            await ExecuteTaskPhaseAsync(
                session,
                plannedTasks
                    .Where(item =>
                        !string.Equals(item.Plan.TaskType, "planner", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(item.Plan.TaskType, "chapter-analysis", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(item.Plan.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                "补充任务",
                maxParallelism,
                cancellationToken);

            if (overviewArtifact is not null)
            {
                _context.WorkflowAnalysisArtifacts.Add(new WorkflowAnalysisArtifact
                {
                    Id = Guid.NewGuid().ToString(),
                    AnalysisSessionId = session.Id,
                    ArtifactType = overviewArtifact.ArtifactType,
                    Title = overviewArtifact.Title,
                    ContentFormat = overviewArtifact.ContentFormat,
                    Content = overviewArtifact.Content,
                    MetadataJson = JsonSerializer.Serialize(overviewArtifact.Metadata, JsonOptions)
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            var documentRefreshOutcome = await TryRegenerateWorkflowDocumentAsync(
                session,
                repository,
                workspace,
                cancellationToken);

            session.Status = session.FailedTasks > 0 || documentRefreshOutcome.ShouldTreatAsError
                ? "CompletedWithErrors"
                : "Completed";
            session.ProgressMessage = BuildCompletionProgressMessage(session, documentRefreshOutcome);
            session.CurrentTaskId = null;
            session.PendingTaskCount = 0;
            session.RunningTaskCount = 0;
            session.CompletedAt = DateTime.UtcNow;
            session.LastActivityAt = session.CompletedAt.Value;
            AppendLog(
                session,
                null,
                session.FailedTasks > 0 || documentRefreshOutcome.ShouldTreatAsError ? "warning" : "info",
                BuildCompletionLogMessage(session, documentRefreshOutcome));
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow analysis execution failed for session {AnalysisSessionId}", analysisSessionId);
            session.Status = "Failed";
            session.ProgressMessage = ex.Message;
            session.CurrentTaskId = null;
            session.RunningTaskCount = 0;
            session.LastActivityAt = DateTime.UtcNow;
            session.CompletedAt = DateTime.UtcNow;
            AppendLog(session, null, "error", $"分析失败：{ex.Message}");
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (workspace is not null)
            {
                await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
            }
        }
    }

    private async Task ExecuteTaskPhaseAsync(
        WorkflowAnalysisSession session,
        IReadOnlyList<PlannedTaskState> phaseTasks,
        string phaseName,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        if (phaseTasks.Count == 0)
        {
            return;
        }

        AppendLog(
            session,
            null,
            "info",
            $"{phaseName}阶段开始，任务数 {phaseTasks.Count}，最大并行度 {maxParallelism}。");
        session.ProgressMessage = BuildPhaseProgressMessage(phaseName, session);
        session.LastActivityAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var pending = new Queue<PlannedTaskState>(phaseTasks.OrderBy(item => item.Entity.SequenceNumber));
        var running = new List<RunningTaskState>();

        while (pending.Count > 0 || running.Count > 0)
        {
            while (pending.Count > 0 && running.Count < maxParallelism)
            {
                var plannedTask = pending.Dequeue();
                MarkTaskRunning(session, plannedTask.Entity, running.Count + 1, phaseName);
                AppendLog(session, plannedTask.Entity.Id, "info", $"开始任务：{plannedTask.Entity.Title}");
                await _context.SaveChangesAsync(cancellationToken);

                var request = CreateExecutionRequest(session, plannedTask);
                running.Add(new RunningTaskState(
                    plannedTask,
                    _taskRunner.ExecuteAsync(request, cancellationToken)));
            }

            var completedExecution = await Task.WhenAny(running.Select(item => item.ExecutionTask));
            var completedTask = running.First(item => item.ExecutionTask == completedExecution);
            running.Remove(completedTask);

            try
            {
                var result = await completedTask.ExecutionTask;
                MarkTaskCompleted(session, completedTask.PlannedTask.Entity, running.Count, phaseName, result);
                PersistTaskArtifacts(session.Id, completedTask.PlannedTask.Entity.Id, result.Artifacts);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                MarkTaskFailed(session, completedTask.PlannedTask.Entity, running.Count, phaseName, ex);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        AppendLog(
            session,
            null,
            "info",
            $"{phaseName}阶段结束，累计成功 {session.CompletedTasks} 个任务，失败 {session.FailedTasks} 个任务。");
        session.ProgressMessage = BuildPhaseProgressMessage(phaseName, session);
        session.LastActivityAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void MarkTaskRunning(
        WorkflowAnalysisSession session,
        WorkflowAnalysisTask task,
        int runningCount,
        string phaseName)
    {
        var now = DateTime.UtcNow;
        task.Status = "Running";
        task.StartedAt ??= now;
        task.LastActivityAt = now;

        session.PendingTaskCount = Math.Max(0, session.PendingTaskCount - 1);
        session.RunningTaskCount = runningCount;
        session.CurrentTaskId = session.RunningTaskCount == 1 ? task.Id : null;
        session.ProgressMessage = BuildPhaseProgressMessage(phaseName, session);
        session.LastActivityAt = now;
    }

    private void MarkTaskCompleted(
        WorkflowAnalysisSession session,
        WorkflowAnalysisTask task,
        int remainingRunningCount,
        string phaseName,
        WorkflowAnalysisTaskExecutionResult result)
    {
        var now = DateTime.UtcNow;
        task.Status = "Completed";
        task.CompletedAt = now;
        task.LastActivityAt = now;
        task.Summary = string.IsNullOrWhiteSpace(result.Summary) ? task.Summary : result.Summary;

        session.CompletedTasks += 1;
        session.RunningTaskCount = Math.Max(0, remainingRunningCount);
        session.CurrentTaskId = null;
        session.ProgressMessage = BuildPhaseProgressMessage(phaseName, session);
        session.LastActivityAt = now;

        foreach (var message in result.LogMessages)
        {
            AppendLog(session, task.Id, "info", message);
        }

        AppendLog(session, task.Id, "info", $"完成任务：{task.Title}");
    }

    private void MarkTaskFailed(
        WorkflowAnalysisSession session,
        WorkflowAnalysisTask task,
        int remainingRunningCount,
        string phaseName,
        Exception exception)
    {
        var now = DateTime.UtcNow;
        task.Status = "Failed";
        task.ErrorMessage = exception.Message;
        task.CompletedAt = now;
        task.LastActivityAt = now;

        session.FailedTasks += 1;
        session.RunningTaskCount = Math.Max(0, remainingRunningCount);
        session.CurrentTaskId = null;
        session.ProgressMessage = BuildPhaseProgressMessage(phaseName, session);
        session.LastActivityAt = now;

        AppendLog(session, task.Id, "error", $"任务失败：{task.Title}，原因：{exception.Message}");
    }

    private void PersistTaskArtifacts(
        string analysisSessionId,
        string taskId,
        IReadOnlyCollection<WorkflowDeepAnalysisArtifactResult> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            _context.WorkflowAnalysisArtifacts.Add(new WorkflowAnalysisArtifact
            {
                Id = Guid.NewGuid().ToString(),
                AnalysisSessionId = analysisSessionId,
                TaskId = taskId,
                ArtifactType = artifact.ArtifactType,
                Title = artifact.Title,
                ContentFormat = artifact.ContentFormat,
                Content = artifact.Content,
                MetadataJson = JsonSerializer.Serialize(artifact.Metadata, JsonOptions)
            });
        }
    }

    private static WorkflowAnalysisTaskExecutionRequest CreateExecutionRequest(
        WorkflowAnalysisSession session,
        PlannedTaskState plannedTask)
    {
        return new WorkflowAnalysisTaskExecutionRequest
        {
            AnalysisSessionId = session.Id,
            TaskId = plannedTask.Entity.Id,
            TaskType = plannedTask.Plan.TaskType,
            Title = plannedTask.Plan.Title,
            Depth = plannedTask.Plan.Depth,
            Summary = plannedTask.Plan.Summary,
            FocusSymbols = plannedTask.Plan.FocusSymbols.ToList(),
            Metadata = plannedTask.Plan.Metadata.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase),
            PlannedArtifacts = plannedTask.PlannedArtifacts
                .Select(CloneArtifact)
                .ToList()
        };
    }

    private static List<PlannedTaskState> BuildPlannedTasks(
        string analysisSessionId,
        WorkflowDeepAnalysisResult analysis)
    {
        var chapterArtifacts = analysis.Artifacts
            .Where(item => !string.Equals(item.ArtifactType, "analysis-overview", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Metadata.TryGetValue("chapterKey", out var chapterKey) ? chapterKey : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(CloneArtifact).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var plannedTasks = analysis.Tasks
            .Select((task, index) =>
            {
                var chapterKey = task.Metadata.TryGetValue("chapterKey", out var value) ? value : string.Empty;
                var plannedArtifacts =
                    string.IsNullOrWhiteSpace(chapterKey) ||
                    !string.Equals(task.TaskType, "chapter-analysis", StringComparison.OrdinalIgnoreCase)
                        ? new List<WorkflowDeepAnalysisArtifactResult>()
                        : chapterArtifacts.GetValueOrDefault(
                            chapterKey,
                            new List<WorkflowDeepAnalysisArtifactResult>());
                return new PlannedTaskState(
                    task,
                    new WorkflowAnalysisTask
                    {
                        Id = Guid.NewGuid().ToString(),
                        AnalysisSessionId = analysisSessionId,
                        SequenceNumber = index + 1,
                        Depth = task.Depth,
                        TaskType = task.TaskType,
                        Title = task.Title,
                        Status = "Pending",
                        Summary = task.Summary,
                        FocusSymbolsJson = JsonSerializer.Serialize(task.FocusSymbols, JsonOptions),
                        FocusFilesJson = JsonSerializer.Serialize(new List<string>(), JsonOptions),
                        MetadataJson = JsonSerializer.Serialize(task.Metadata, JsonOptions)
                    },
                    plannedArtifacts);
            })
            .ToList();

        var planner = plannedTasks
            .FirstOrDefault(item => string.Equals(item.Plan.TaskType, "planner", StringComparison.OrdinalIgnoreCase));
        var chapterTaskByKey = plannedTasks
            .Where(item => string.Equals(item.Plan.TaskType, "chapter-analysis", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Plan.Metadata.TryGetValue("chapterKey", out var chapterKey) && !string.IsNullOrWhiteSpace(chapterKey))
            .ToDictionary(
                item => item.Plan.Metadata["chapterKey"],
                item => item,
                StringComparer.OrdinalIgnoreCase);

        foreach (var plannedTask in plannedTasks)
        {
            if (string.Equals(plannedTask.Plan.TaskType, "chapter-analysis", StringComparison.OrdinalIgnoreCase))
            {
                plannedTask.Entity.ParentTaskId = planner?.Entity.Id;
                continue;
            }

            if (string.Equals(plannedTask.Plan.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase))
            {
                if (plannedTask.Plan.Metadata.TryGetValue("chapterKey", out var chapterKey) &&
                    chapterTaskByKey.TryGetValue(chapterKey, out var parentTask))
                {
                    plannedTask.Entity.ParentTaskId = parentTask.Entity.Id;
                }
                else
                {
                    plannedTask.Entity.ParentTaskId = planner?.Entity.Id;
                }
            }
        }

        return plannedTasks;
    }

    private async Task TryApplyPlannerHintsAsync(
        WorkflowAnalysisSession session,
        RepositoryWorkflowProfile draft,
        WorkflowDeepAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        EnsurePlannerSourceMetadata(analysis.Tasks);

        if (!draft.Acp.Enabled || analysis.ChapterSlices.Count == 0)
        {
            return;
        }

        var remainingBranchCapacity = CalculateRemainingBranchCapacityByChapter(draft, analysis);
        var remainingCapacityTotal = remainingBranchCapacity.Values.Sum();
        if (remainingCapacityTotal <= 0)
        {
            AppendLog(session, null, "info", "跳过 AI planner hint：当前章节分支容量已满，继续使用 deterministic planner。");
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        AppendLog(
            session,
            null,
            "ai",
            $"开始请求 AI planner hint：{remainingBranchCapacity.Count} 个章节可补充，剩余分支容量 {remainingCapacityTotal}。");
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var hintResult = await _plannerHintService.GenerateHintsAsync(
                new WorkflowAnalysisPlannerHintRequest
                {
                    AnalysisSessionId = session.Id,
                    ProfileKey = draft.Key,
                    LanguageCode = string.IsNullOrWhiteSpace(session.LanguageCode) ? "zh" : session.LanguageCode,
                    Objective = session.Objective,
                    Profile = draft,
                    ChapterSlices = analysis.ChapterSlices,
                    ExistingTasks = analysis.Tasks,
                    RemainingBranchCapacityByChapter = remainingBranchCapacity
                },
                cancellationToken);

            var adoptedTasks = MergePlannerHintSuggestions(analysis, hintResult, remainingBranchCapacity);
            UpdateOverviewArtifactWithPlannerHints(analysis, hintResult, adoptedTasks);

            AppendLog(
                session,
                null,
                "ai",
                string.IsNullOrWhiteSpace(hintResult.Summary)
                    ? $"AI planner hint 返回 {hintResult.SuggestedBranchTasks.Count} 个建议，采纳 {adoptedTasks.Count} 个。"
                    : $"AI planner hint 返回 {hintResult.SuggestedBranchTasks.Count} 个建议，采纳 {adoptedTasks.Count} 个。摘要：{hintResult.Summary}");
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow planner hint failed for session {AnalysisSessionId}", session.Id);
            AppendLog(
                session,
                null,
                "warning",
                $"AI planner hint 失败，已回退到 deterministic planner：{ex.Message}");
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsurePlannerSourceMetadata(IEnumerable<WorkflowDeepAnalysisTaskResult> tasks)
    {
        foreach (var task in tasks)
        {
            if (!task.Metadata.ContainsKey("plannerSource"))
            {
                task.Metadata["plannerSource"] = "deterministic";
            }
        }
    }

    private static Dictionary<string, int> CalculateRemainingBranchCapacityByChapter(
        RepositoryWorkflowProfile draft,
        WorkflowDeepAnalysisResult analysis)
    {
        var maxBranchTasks = Math.Max(0, draft.Acp.MaxBranchTasks);
        var branchCountsByChapter = analysis.Tasks
            .Where(task => string.Equals(task.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                task => task.Metadata.TryGetValue("chapterKey", out var chapterKey) ? chapterKey : string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        return analysis.ChapterSlices
            .Select(slice => slice.ChapterKey)
            .Where(chapterKey => !string.IsNullOrWhiteSpace(chapterKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                chapterKey => chapterKey,
                chapterKey => Math.Max(0, maxBranchTasks - branchCountsByChapter.GetValueOrDefault(chapterKey)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<WorkflowDeepAnalysisTaskResult> MergePlannerHintSuggestions(
        WorkflowDeepAnalysisResult analysis,
        WorkflowAnalysisPlannerHintResult hintResult,
        IDictionary<string, int> remainingBranchCapacityByChapter)
    {
        var validChapterKeys = analysis.ChapterSlices
            .Select(slice => slice.ChapterKey)
            .Where(chapterKey => !string.IsNullOrWhiteSpace(chapterKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var branchRootsByChapter = analysis.Tasks
            .Where(task => string.Equals(task.TaskType, "branch-drilldown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                task => task.Metadata.TryGetValue("chapterKey", out var chapterKey) ? chapterKey : string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(task => task.Metadata.TryGetValue("branchRoot", out var branchRoot) ? branchRoot : string.Empty)
                    .Where(branchRoot => !string.IsNullOrWhiteSpace(branchRoot))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var adoptedTasks = new List<WorkflowDeepAnalysisTaskResult>();

        foreach (var suggestion in hintResult.SuggestedBranchTasks)
        {
            if (!validChapterKeys.Contains(suggestion.ChapterKey) ||
                string.IsNullOrWhiteSpace(suggestion.BranchRoot) ||
                !remainingBranchCapacityByChapter.TryGetValue(suggestion.ChapterKey, out var remainingCapacity) ||
                remainingCapacity <= 0)
            {
                continue;
            }

            if (!branchRootsByChapter.TryGetValue(suggestion.ChapterKey, out var existingBranchRoots))
            {
                existingBranchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                branchRootsByChapter[suggestion.ChapterKey] = existingBranchRoots;
            }

            if (!existingBranchRoots.Add(suggestion.BranchRoot))
            {
                continue;
            }

            var focusSymbols = suggestion.FocusSymbols
                .Prepend(suggestion.BranchRoot)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
            var task = new WorkflowDeepAnalysisTaskResult
            {
                TaskType = "branch-drilldown",
                Title = $"分支钻取：{suggestion.BranchRoot}",
                Depth = 2,
                FocusSymbols = focusSymbols,
                Summary = string.IsNullOrWhiteSpace(suggestion.Summary)
                    ? $"AI planner hint 建议补充说明符号 {suggestion.BranchRoot} 的调用链、分支条件和业务差异。"
                    : suggestion.Summary,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plannerSource"] = "ai-hint",
                    ["chapterKey"] = suggestion.ChapterKey,
                    ["branchRoot"] = suggestion.BranchRoot,
                    ["branchReason"] = "ai-hint"
                }
            };
            if (!string.IsNullOrWhiteSpace(suggestion.Reason))
            {
                task.Metadata["plannerHintReason"] = suggestion.Reason;
            }

            analysis.Tasks.Add(task);
            adoptedTasks.Add(task);
            remainingBranchCapacityByChapter[suggestion.ChapterKey] = remainingCapacity - 1;
        }

        return adoptedTasks;
    }

    private static void UpdateOverviewArtifactWithPlannerHints(
        WorkflowDeepAnalysisResult analysis,
        WorkflowAnalysisPlannerHintResult hintResult,
        IReadOnlyCollection<WorkflowDeepAnalysisTaskResult> adoptedTasks)
    {
        var overviewArtifact = analysis.Artifacts
            .FirstOrDefault(item => string.Equals(item.ArtifactType, "analysis-overview", StringComparison.OrdinalIgnoreCase));
        if (overviewArtifact is null)
        {
            return;
        }

        overviewArtifact.Metadata["taskCount"] = analysis.Tasks.Count.ToString();
        if (adoptedTasks.Count == 0)
        {
            return;
        }

        overviewArtifact.Metadata["aiHintAdoptedCount"] = adoptedTasks.Count.ToString();
        if (!string.IsNullOrWhiteSpace(hintResult.Summary))
        {
            overviewArtifact.Metadata["aiHintSummary"] = hintResult.Summary;
        }

        var lines = new List<string> { overviewArtifact.Content.TrimEnd() };
        lines.Add(string.Empty);
        lines.Add("## AI Planner Hint 补充");
        if (!string.IsNullOrWhiteSpace(hintResult.Summary))
        {
            lines.Add($"摘要：{hintResult.Summary}");
        }

        foreach (var task in adoptedTasks)
        {
            var chapterKey = task.Metadata.TryGetValue("chapterKey", out var value) ? value : string.Empty;
            lines.Add($"- [{chapterKey}] {task.Title}: {task.Summary}");
        }

        overviewArtifact.Content = string.Join(Environment.NewLine, lines);
    }

    private async Task RemoveExistingSessionDataAsync(string sessionId, CancellationToken cancellationToken)
    {
        var existingTasks = await _context.WorkflowAnalysisTasks
            .Where(item => item.AnalysisSessionId == sessionId && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        if (existingTasks.Count > 0)
        {
            _context.WorkflowAnalysisTasks.RemoveRange(existingTasks);
        }

        var existingArtifacts = await _context.WorkflowAnalysisArtifacts
            .Where(item => item.AnalysisSessionId == sessionId && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        if (existingArtifacts.Count > 0)
        {
            _context.WorkflowAnalysisArtifacts.RemoveRange(existingArtifacts);
        }
    }

    private void AppendLog(
        WorkflowAnalysisSession session,
        string? taskId,
        string level,
        string message)
    {
        _context.WorkflowAnalysisLogs.Add(new WorkflowAnalysisLog
        {
            Id = Guid.NewGuid().ToString(),
            AnalysisSessionId = session.Id,
            TaskId = taskId,
            Level = level,
            Message = message
        });
        session.LastActivityAt = DateTime.UtcNow;
    }

    private async Task<DocumentRefreshOutcome> TryRegenerateWorkflowDocumentAsync(
        WorkflowAnalysisSession session,
        Repository repository,
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ProfileKey))
        {
            AppendLog(session, null, "warning", "跳过文档回填：当前分析会话没有 profileKey。");
            return DocumentRefreshOutcome.Skip("未自动回填文档：缺少 profileKey。");
        }

        var formalProfile = await _workflowConfigService.GetProfileAsync(
            repository.Id,
            session.ProfileKey,
            cancellationToken);
        if (formalProfile is null)
        {
            AppendLog(
                session,
                null,
                "warning",
                $"跳过文档回填：profile `{session.ProfileKey}` 还未采用到正式 Workflow 配置，ACP 结果已写入分析产物。");
            return DocumentRefreshOutcome.Skip("未自动回填文档：当前草稿尚未采用到正式 Workflow 配置。");
        }

        if (string.IsNullOrWhiteSpace(session.BranchId) || string.IsNullOrWhiteSpace(session.LanguageCode))
        {
            AppendLog(session, null, "warning", "跳过文档回填：缺少分支或语言上下文。");
            return DocumentRefreshOutcome.Skip("未自动回填文档：缺少分支或语言上下文。");
        }

        var normalizedLanguage = session.LanguageCode.Trim();
        var branchLanguage = await _context.BranchLanguages
            .FirstOrDefaultAsync(item =>
                item.RepositoryBranchId == session.BranchId &&
                !item.IsDeleted &&
                item.LanguageCode.ToLower() == normalizedLanguage.ToLower(),
                cancellationToken);
        if (branchLanguage is null)
        {
            AppendLog(
                session,
                null,
                "warning",
                $"跳过文档回填：分支 `{session.BranchId}` 上找不到语言 `{session.LanguageCode}` 对应的 Wiki 上下文。");
            return DocumentRefreshOutcome.Skip("未自动回填文档：找不到对应的 BranchLanguage。");
        }

        session.Status = "Composing";
        session.ProgressMessage = $"基于 ACP 深挖结果回填业务流文档：{session.ProfileKey}";
        session.CurrentTaskId = null;
        session.LastActivityAt = DateTime.UtcNow;
        AppendLog(session, null, "info", $"ACP 任务产物已写入，开始回填业务流文档：{session.ProfileKey}");
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            if (_wikiGenerator is WikiGenerator generator)
            {
                generator.SetCurrentRepository(repository.Id);
            }

            await _wikiGenerator.RegenerateWorkflowDocumentsAsync(
                workspace,
                branchLanguage,
                session.ProfileKey,
                cancellationToken);

            AppendLog(session, null, "info", $"业务流文档已按最新 ACP 深挖结果完成重建：{session.ProfileKey}");
            await _context.SaveChangesAsync(cancellationToken);
            return DocumentRefreshOutcome.Success("已自动回填业务流文档。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Workflow document refresh failed after ACP analysis for session {AnalysisSessionId}",
                session.Id);
            AppendLog(session, null, "error", $"ACP 产物已写入，但业务流文档回填失败：{ex.Message}");
            await _context.SaveChangesAsync(cancellationToken);
            return DocumentRefreshOutcome.Failure($"文档回填失败：{ex.Message}");
        }
    }

    private static string BuildPhaseProgressMessage(string phaseName, WorkflowAnalysisSession session)
    {
        return $"{phaseName}：成功 {session.CompletedTasks} / 失败 {session.FailedTasks} / 运行 {session.RunningTaskCount} / 待处理 {session.PendingTaskCount}";
    }

    private static string BuildCompletionProgressMessage(
        WorkflowAnalysisSession session,
        DocumentRefreshOutcome documentRefreshOutcome)
    {
        var baseMessage = session.FailedTasks > 0
            ? $"分析完成，成功 {session.CompletedTasks} 个任务，失败 {session.FailedTasks} 个任务。"
            : "分析完成";

        return string.IsNullOrWhiteSpace(documentRefreshOutcome.Message)
            ? baseMessage
            : $"{baseMessage} {documentRefreshOutcome.Message}";
    }

    private static string BuildCompletionLogMessage(
        WorkflowAnalysisSession session,
        DocumentRefreshOutcome documentRefreshOutcome)
    {
        if (session.FailedTasks > 0)
        {
            return documentRefreshOutcome.Succeeded
                ? "分析完成，部分任务失败，但已保留已完成产物并完成文档回填。"
                : "分析完成，但存在失败任务；已保留已完成任务产物。";
        }

        if (documentRefreshOutcome.Succeeded)
        {
            return "分析完成，artifact 已写入并完成业务流文档回填。";
        }

        return string.IsNullOrWhiteSpace(documentRefreshOutcome.Message)
            ? "分析完成，artifact 已写入。"
            : $"分析完成，artifact 已写入。{documentRefreshOutcome.Message}";
    }

    private static void UpdateSessionProgress(
        WorkflowAnalysisSession session,
        string? currentTaskId,
        string progressMessage)
    {
        session.CurrentTaskId = currentTaskId;
        session.ProgressMessage = progressMessage;
        session.LastActivityAt = DateTime.UtcNow;
    }

    private async Task<RepositoryWorkflowProfile?> LoadDraftAsync(
        string sessionId,
        int? versionNumber,
        CancellationToken cancellationToken)
    {
        if (!versionNumber.HasValue || versionNumber.Value <= 0)
        {
            return null;
        }

        var draftJson = await _context.WorkflowTemplateDraftVersions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId &&
                           item.VersionNumber == versionNumber.Value &&
                           !item.IsDeleted)
            .Select(item => item.DraftJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return null;
        }

        try
        {
            return RepositoryWorkflowConfigRules.SanitizeProfile(
                JsonSerializer.Deserialize<RepositoryWorkflowProfile>(draftJson, JsonOptions) ?? new RepositoryWorkflowProfile());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RepositoryWorkflowProfile CreateDefaultDraft(string? profileKey)
    {
        return RepositoryWorkflowConfigRules.SanitizeProfile(new RepositoryWorkflowProfile
        {
            Key = profileKey ?? "workflow-profile",
            Enabled = false,
            Source = new RepositoryWorkflowProfileSource
            {
                Type = "analysis-default"
            }
        });
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

    private sealed class PlannedTaskState(
        WorkflowDeepAnalysisTaskResult plan,
        WorkflowAnalysisTask entity,
        IReadOnlyList<WorkflowDeepAnalysisArtifactResult> plannedArtifacts)
    {
        public WorkflowDeepAnalysisTaskResult Plan { get; } = plan;

        public WorkflowAnalysisTask Entity { get; } = entity;

        public IReadOnlyList<WorkflowDeepAnalysisArtifactResult> PlannedArtifacts { get; } = plannedArtifacts;
    }

    private sealed class RunningTaskState(
        PlannedTaskState plannedTask,
        Task<WorkflowAnalysisTaskExecutionResult> executionTask)
    {
        public PlannedTaskState PlannedTask { get; } = plannedTask;

        public Task<WorkflowAnalysisTaskExecutionResult> ExecutionTask { get; } = executionTask;
    }

    private sealed record DocumentRefreshOutcome(
        bool Succeeded,
        bool ShouldTreatAsError,
        string? Message)
    {
        public static DocumentRefreshOutcome Success(string message)
            => new(true, false, message);

        public static DocumentRefreshOutcome Skip(string message)
            => new(false, false, message);

        public static DocumentRefreshOutcome Failure(string message)
            => new(false, true, message);
    }
}
