using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki.Lsp;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowTemplateAnalysisService
{
    Task<WorkflowTemplateAugmentResultDto> AugmentCurrentDraftAsync(
        string repositoryId,
        string sessionId,
        WorkflowTemplateAugmentRequest request,
        CancellationToken cancellationToken = default);

    Task<List<WorkflowAnalysisSessionSummaryDto>> GetAnalysisSessionsAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<WorkflowAnalysisSessionDetailDto> CreateAnalysisSessionAsync(
        string repositoryId,
        string sessionId,
        CreateWorkflowAnalysisSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowAnalysisSessionDetailDto> GetAnalysisSessionAsync(
        string repositoryId,
        string sessionId,
        string analysisSessionId,
        CancellationToken cancellationToken = default);

    Task<List<WorkflowAnalysisLogDto>> GetAnalysisSessionLogsAsync(
        string repositoryId,
        string sessionId,
        string analysisSessionId,
        DateTime? since = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowTemplateAnalysisService : IWorkflowTemplateAnalysisService
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
    private readonly IWorkflowLspAugmentService _workflowLspAugmentService;
    private readonly IWorkflowDeepAnalysisService _workflowDeepAnalysisService;
    private readonly IWorkflowAnalysisQueueService _workflowAnalysisQueueService;
    private readonly IWorkflowTemplateWorkbenchService _workbenchService;
    private readonly IUserContext _userContext;
    private readonly ILogger<WorkflowTemplateAnalysisService> _logger;

    public WorkflowTemplateAnalysisService(
        IContext context,
        IRepositoryAnalyzer repositoryAnalyzer,
        IWorkflowDiscoveryService workflowDiscoveryService,
        IWorkflowLspAugmentService workflowLspAugmentService,
        IWorkflowDeepAnalysisService workflowDeepAnalysisService,
        IWorkflowAnalysisQueueService workflowAnalysisQueueService,
        IWorkflowTemplateWorkbenchService workbenchService,
        IUserContext userContext,
        ILogger<WorkflowTemplateAnalysisService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositoryAnalyzer = repositoryAnalyzer ?? throw new ArgumentNullException(nameof(repositoryAnalyzer));
        _workflowDiscoveryService = workflowDiscoveryService ?? throw new ArgumentNullException(nameof(workflowDiscoveryService));
        _workflowLspAugmentService = workflowLspAugmentService ?? throw new ArgumentNullException(nameof(workflowLspAugmentService));
        _workflowDeepAnalysisService = workflowDeepAnalysisService ?? throw new ArgumentNullException(nameof(workflowDeepAnalysisService));
        _workflowAnalysisQueueService = workflowAnalysisQueueService ?? throw new ArgumentNullException(nameof(workflowAnalysisQueueService));
        _workbenchService = workbenchService ?? throw new ArgumentNullException(nameof(workbenchService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowTemplateAugmentResultDto> AugmentCurrentDraftAsync(
        string repositoryId,
        string sessionId,
        WorkflowTemplateAugmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var repository = await GetRepositoryAsync(repositoryId, cancellationToken);
        var session = await GetTemplateSessionAsync(repositoryId, sessionId, cancellationToken);
        var currentDraft = await GetCurrentDraftAsync(session.Id, session.CurrentVersionNumber, cancellationToken)
                           ?? CreateDefaultDraft();
        var branch = await ResolveBranchAsync(repositoryId, session.BranchId, cancellationToken);
        var branchName = branch?.BranchName ?? session.BranchName ?? "main";
        var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            branchName,
            branch?.LastCommitId,
            cancellationToken);

        try
        {
            var discovery = await _workflowDiscoveryService.DiscoverAsync(workspace, cancellationToken: cancellationToken);
            var augment = await _workflowLspAugmentService.AugmentAsync(
                workspace,
                currentDraft,
                discovery.Graph,
                cancellationToken);

            int? createdVersionNumber = null;
            if (request.ApplyToDraftVersion)
            {
                var nextVersionNumber = session.CurrentVersionNumber + 1;
                var mergedDraft = ApplyAugmentToDraft(currentDraft, augment, session.Id, nextVersionNumber);

                _context.WorkflowTemplateDraftVersions.Add(new WorkflowTemplateDraftVersion
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    VersionNumber = nextVersionNumber,
                    BasedOnVersionNumber = session.CurrentVersionNumber > 0 ? session.CurrentVersionNumber : null,
                    SourceType = "lsp-augment",
                    ChangeSummary = "基于 Roslyn/LSP 增强当前草稿的 root symbols、mustExplainSymbols 与章节切片。",
                    DraftJson = JsonSerializer.Serialize(mergedDraft, JsonOptions),
                    RiskNotesJson = JsonSerializer.Serialize(
                        new List<string>
                        {
                            "当前为 Roslyn fallback 结果，真实 LSP server 接入后可进一步提升局部调用链精度。"
                        },
                        JsonOptions),
                    EvidenceFilesJson = JsonSerializer.Serialize(augment.EvidenceFiles, JsonOptions),
                    ValidationIssuesJson = JsonSerializer.Serialize(
                        RepositoryWorkflowConfigRules.GetDraftValidationIssues(mergedDraft),
                        JsonOptions)
                });

                var sequenceNumber = await GetNextMessageSequenceAsync(session.Id, cancellationToken);
                _context.WorkflowTemplateMessages.Add(new WorkflowTemplateMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    SequenceNumber = sequenceNumber,
                    Role = "System",
                    Content = "已将 LSP/Roslyn 增强结果写入新的草稿版本，可继续在多轮对话中基于这些章节和符号讨论。",
                    VersionNumber = nextVersionNumber,
                    ChangeSummary = "增强当前草稿",
                    MessageTimestamp = DateTime.UtcNow
                });

                session.CurrentVersionNumber = nextVersionNumber;
                session.CurrentDraftKey = mergedDraft.Key;
                session.CurrentDraftName = mergedDraft.Name;
                session.LastActivityAt = DateTime.UtcNow;
                session.MessageCount += 1;
                createdVersionNumber = nextVersionNumber;

                await _context.SaveChangesAsync(cancellationToken);
            }

            return new WorkflowTemplateAugmentResultDto
            {
                Augment = MapAugment(augment),
                Session = await _workbenchService.GetSessionAsync(repositoryId, sessionId, cancellationToken),
                CreatedVersionNumber = createdVersionNumber
            };
        }
        finally
        {
            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
    }

    public async Task<List<WorkflowAnalysisSessionSummaryDto>> GetAnalysisSessionsAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await GetTemplateSessionAsync(repositoryId, sessionId, cancellationToken);

        return await _context.WorkflowAnalysisSessions
            .AsNoTracking()
            .Where(session => session.RepositoryId == repositoryId &&
                              session.WorkflowTemplateSessionId == sessionId &&
                              !session.IsDeleted)
            .OrderByDescending(session => session.CreatedAt)
            .Select(MapAnalysisSessionSummaryExpression())
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowAnalysisSessionDetailDto> CreateAnalysisSessionAsync(
        string repositoryId,
        string sessionId,
        CreateWorkflowAnalysisSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var templateSession = await GetTemplateSessionAsync(repositoryId, sessionId, cancellationToken);
        var currentDraft = await GetCurrentDraftAsync(templateSession.Id, templateSession.CurrentVersionNumber, cancellationToken)
                           ?? CreateDefaultDraft();
        var chapter = string.IsNullOrWhiteSpace(request.ChapterKey)
            ? null
            : currentDraft.ChapterProfiles.FirstOrDefault(item =>
                string.Equals(item.Key, request.ChapterKey.Trim(), StringComparison.OrdinalIgnoreCase));
        var branch = await ResolveBranchAsync(repositoryId, templateSession.BranchId, cancellationToken);
        var now = DateTime.UtcNow;
        var analysisSession = new WorkflowAnalysisSession
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            WorkflowTemplateSessionId = templateSession.Id,
            BranchId = templateSession.BranchId,
            BranchName = branch?.BranchName ?? templateSession.BranchName ?? "main",
            LanguageCode = templateSession.LanguageCode,
            ProfileKey = currentDraft.Key,
            DraftVersionNumber = templateSession.CurrentVersionNumber > 0 ? templateSession.CurrentVersionNumber : null,
            ChapterKey = chapter?.Key,
            Status = "Queued",
            Objective = string.IsNullOrWhiteSpace(request.Objective)
                ? currentDraft.Acp.Objective
                : request.Objective.Trim(),
            Summary = chapter is null
                ? "已创建整条业务流 ACP 深挖会话，等待后台 worker 执行。"
                : $"已创建章节 ACP 深挖会话（{chapter.Title}），等待后台 worker 执行。",
            TotalTasks = 0,
            CompletedTasks = 0,
            FailedTasks = 0,
            PendingTaskCount = 0,
            RunningTaskCount = 0,
            CurrentTaskId = null,
            ProgressMessage = chapter is null
                ? "等待后台 worker 执行整条业务流分析"
                : $"等待后台 worker 执行章节分析：{chapter.Title}",
            CreatedByUserId = _userContext.UserId,
            CreatedByUserName = _userContext.UserName,
            QueuedAt = now,
            LastActivityAt = now,
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["profileKey"] = currentDraft.Key,
                ["chapterKey"] = chapter?.Key ?? string.Empty,
                ["scope"] = chapter is null ? "workflow" : "chapter",
                ["strategy"] = currentDraft.Acp.SplitStrategy
            }, JsonOptions)
        };

        _context.WorkflowAnalysisSessions.Add(analysisSession);
        _context.WorkflowAnalysisLogs.Add(new WorkflowAnalysisLog
        {
            Id = Guid.NewGuid().ToString(),
            AnalysisSessionId = analysisSession.Id,
            Level = "info",
            Message = chapter is null
                ? "已创建整条业务流 ACP 深挖会话，等待后台 worker 执行。"
                : $"已创建章节 ACP 深挖会话（{chapter.Title}），等待后台 worker 执行。"
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _workflowAnalysisQueueService.EnqueueAsync(analysisSession.Id, cancellationToken);
        return await GetAnalysisSessionAsync(repositoryId, sessionId, analysisSession.Id, cancellationToken);
    }

    public async Task<WorkflowAnalysisSessionDetailDto> GetAnalysisSessionAsync(
        string repositoryId,
        string sessionId,
        string analysisSessionId,
        CancellationToken cancellationToken = default)
    {
        await GetTemplateSessionAsync(repositoryId, sessionId, cancellationToken);

        var analysisSession = await _context.WorkflowAnalysisSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session =>
                session.RepositoryId == repositoryId &&
                session.WorkflowTemplateSessionId == sessionId &&
                session.Id == analysisSessionId &&
                !session.IsDeleted,
                cancellationToken)
            ?? throw new KeyNotFoundException("分析会话不存在。");

        var tasks = await _context.WorkflowAnalysisTasks
            .AsNoTracking()
            .Where(task => task.AnalysisSessionId == analysisSession.Id && !task.IsDeleted)
            .OrderBy(task => task.SequenceNumber)
            .ToListAsync(cancellationToken);
        var artifacts = await _context.WorkflowAnalysisArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.AnalysisSessionId == analysisSession.Id && !artifact.IsDeleted)
            .OrderBy(artifact => artifact.CreatedAt)
            .ToListAsync(cancellationToken);
        var recentLogs = await _context.WorkflowAnalysisLogs
            .AsNoTracking()
            .Where(log => log.AnalysisSessionId == analysisSession.Id && !log.IsDeleted)
            .OrderByDescending(log => log.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return new WorkflowAnalysisSessionDetailDto
        {
            AnalysisSessionId = analysisSession.Id,
            RepositoryId = analysisSession.RepositoryId,
            WorkflowTemplateSessionId = analysisSession.WorkflowTemplateSessionId,
            ProfileKey = analysisSession.ProfileKey,
            DraftVersionNumber = analysisSession.DraftVersionNumber,
            ChapterKey = analysisSession.ChapterKey,
            Status = analysisSession.Status,
            Objective = analysisSession.Objective,
            Summary = analysisSession.Summary,
            TotalTasks = analysisSession.TotalTasks,
            CompletedTasks = analysisSession.CompletedTasks,
            FailedTasks = analysisSession.FailedTasks,
            PendingTaskCount = analysisSession.PendingTaskCount,
            RunningTaskCount = analysisSession.RunningTaskCount,
            CurrentTaskId = analysisSession.CurrentTaskId,
            ProgressMessage = analysisSession.ProgressMessage,
            CreatedAt = analysisSession.CreatedAt,
            StartedAt = analysisSession.StartedAt,
            CompletedAt = analysisSession.CompletedAt,
            LastActivityAt = analysisSession.LastActivityAt,
            Tasks = tasks.Select(task => new WorkflowAnalysisTaskDto
            {
                Id = task.Id,
                ParentTaskId = task.ParentTaskId,
                SequenceNumber = task.SequenceNumber,
                Depth = task.Depth,
                TaskType = task.TaskType,
                Title = task.Title,
                Status = task.Status,
                Summary = task.Summary,
                FocusSymbols = DeserializeStringList(task.FocusSymbolsJson),
                FocusFiles = DeserializeStringList(task.FocusFilesJson),
                Metadata = DeserializeStringDictionary(task.MetadataJson),
                StartedAt = task.StartedAt,
                CompletedAt = task.CompletedAt,
                ErrorMessage = task.ErrorMessage
            }).ToList(),
            Artifacts = artifacts.Select(artifact => new WorkflowAnalysisArtifactDto
            {
                Id = artifact.Id,
                TaskId = artifact.TaskId,
                ArtifactType = artifact.ArtifactType,
                Title = artifact.Title,
                ContentFormat = artifact.ContentFormat,
                Content = artifact.Content,
                CreatedAt = artifact.CreatedAt,
                Metadata = DeserializeStringDictionary(artifact.MetadataJson)
            }).ToList(),
            RecentLogs = recentLogs
                .OrderBy(log => log.CreatedAt)
                .Select(log => new WorkflowAnalysisLogDto
                {
                    Id = log.Id,
                    TaskId = log.TaskId,
                    Level = log.Level,
                    Message = log.Message,
                    CreatedAt = log.CreatedAt
                })
                .ToList()
        };
    }

    public async Task<List<WorkflowAnalysisLogDto>> GetAnalysisSessionLogsAsync(
        string repositoryId,
        string sessionId,
        string analysisSessionId,
        DateTime? since = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await GetTemplateSessionAsync(repositoryId, sessionId, cancellationToken);
        limit = Math.Clamp(limit, 1, 200);
        var exists = await _context.WorkflowAnalysisSessions
            .AsNoTracking()
            .AnyAsync(session =>
                session.Id == analysisSessionId &&
                session.RepositoryId == repositoryId &&
                session.WorkflowTemplateSessionId == sessionId &&
                !session.IsDeleted,
                cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("分析会话不存在。");
        }

        var query = _context.WorkflowAnalysisLogs
            .AsNoTracking()
            .Where(log => log.AnalysisSessionId == analysisSessionId && !log.IsDeleted);
        if (since.HasValue)
        {
            query = query.Where(log => log.CreatedAt > since.Value);
        }

        return await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(limit)
            .Select(log => new WorkflowAnalysisLogDto
            {
                Id = log.Id,
                TaskId = log.TaskId,
                Level = log.Level,
                Message = log.Message,
                CreatedAt = log.CreatedAt
            })
            .OrderBy(log => log.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private RepositoryWorkflowProfile ApplyAugmentToDraft(
        RepositoryWorkflowProfile draft,
        WorkflowLspAugmentResult augment,
        string sessionId,
        int versionNumber)
    {
        var preservedAnalysisRootSymbols = RemovePreviousAugmentSymbols(
            draft.Analysis.RootSymbolNames,
            draft.LspAssist.SuggestedRootSymbolNames);
        var preservedMustExplainSymbols = RemovePreviousAugmentSymbols(
            draft.Analysis.MustExplainSymbols,
            draft.LspAssist.SuggestedMustExplainSymbols);

        var merged = RepositoryWorkflowConfigRules.SanitizeProfile(new RepositoryWorkflowProfile
        {
            Key = draft.Key,
            Name = draft.Name,
            Description = draft.Description,
            Enabled = false,
            Mode = draft.Mode,
            EntryRoots = draft.EntryRoots
                .Concat(augment.SuggestedRootSymbolNames.Take(4))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EntryKinds = draft.EntryKinds.ToList(),
            AnchorDirectories = draft.AnchorDirectories.ToList(),
            AnchorNames = draft.AnchorNames.ToList(),
            PrimaryTriggerDirectories = draft.PrimaryTriggerDirectories.ToList(),
            CompensationTriggerDirectories = draft.CompensationTriggerDirectories.ToList(),
            SchedulerDirectories = draft.SchedulerDirectories.ToList(),
            ServiceDirectories = draft.ServiceDirectories.ToList(),
            RepositoryDirectories = draft.RepositoryDirectories.ToList(),
            PrimaryTriggerNames = draft.PrimaryTriggerNames.ToList(),
            CompensationTriggerNames = draft.CompensationTriggerNames.ToList(),
            SchedulerNames = draft.SchedulerNames.ToList(),
            RequestEntityNames = draft.RequestEntityNames.ToList(),
            RequestServiceNames = draft.RequestServiceNames.ToList(),
            RequestRepositoryNames = draft.RequestRepositoryNames.ToList(),
            Source = new RepositoryWorkflowProfileSource
            {
                Type = "lsp-augment",
                SessionId = sessionId,
                VersionNumber = versionNumber,
                UpdatedByUserId = _userContext.UserId,
                UpdatedByUserName = _userContext.UserName,
                UpdatedAt = DateTime.UtcNow
            },
            DocumentPreferences = new WorkflowDocumentPreferences
            {
                WritingHint = draft.DocumentPreferences.WritingHint,
                PreferredTerms = draft.DocumentPreferences.PreferredTerms.ToList(),
                RequiredSections = draft.DocumentPreferences.RequiredSections.ToList(),
                AvoidPrimaryTriggerNames = draft.DocumentPreferences.AvoidPrimaryTriggerNames.ToList()
            },
            Analysis = new WorkflowProfileAnalysisOptions
            {
                Mode = draft.Analysis.Mode,
                EntryDirectories = draft.Analysis.EntryDirectories
                    .Concat(augment.SuggestedEntryDirectories)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RootSymbolNames = preservedAnalysisRootSymbols
                    .Concat(augment.SuggestedRootSymbolNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                MustExplainSymbols = preservedMustExplainSymbols
                    .Concat(augment.SuggestedMustExplainSymbols)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AllowedNamespaces = draft.Analysis.AllowedNamespaces.ToList(),
                StopNamespacePrefixes = draft.Analysis.StopNamespacePrefixes.ToList(),
                StopNamePatterns = draft.Analysis.StopNamePatterns.ToList(),
                DepthBudget = draft.Analysis.DepthBudget,
                MaxNodes = draft.Analysis.MaxNodes,
                EnableCoverageValidation = draft.Analysis.EnableCoverageValidation
            },
            ChapterProfiles = MergeChapterProfiles(draft.ChapterProfiles, augment.SuggestedChapterProfiles, draft.Source.Type),
            LspAssist = new WorkflowLspAssistOptions
            {
                Enabled = true,
                PreferredServer = draft.LspAssist.PreferredServer,
                IncludeCallHierarchy = true,
                RequestTimeoutMs = draft.LspAssist.RequestTimeoutMs,
                EnableDefinitionLookup = draft.LspAssist.EnableDefinitionLookup,
                EnableReferenceLookup = draft.LspAssist.EnableReferenceLookup,
                EnablePrepareCallHierarchy = draft.LspAssist.EnablePrepareCallHierarchy,
                AdditionalEntrySymbolHints = draft.LspAssist.AdditionalEntrySymbolHints.ToList(),
                SuggestedEntryDirectories = augment.SuggestedEntryDirectories.ToList(),
                SuggestedRootSymbolNames = augment.SuggestedRootSymbolNames.ToList(),
                SuggestedMustExplainSymbols = augment.SuggestedMustExplainSymbols.ToList(),
                CallHierarchyEdges = augment.CallHierarchyEdges.ToList(),
                LastAugmentedAt = DateTime.UtcNow
            },
            Acp = new WorkflowAcpOptions
            {
                Enabled = true,
                Objective = draft.Acp.Objective,
                MaxBranchTasks = draft.Acp.MaxBranchTasks,
                MaxParallelTasks = draft.Acp.MaxParallelTasks,
                SplitStrategy = draft.Acp.SplitStrategy,
                GenerateFlowchartSeed = draft.Acp.GenerateFlowchartSeed,
                GenerateMindMapSeed = draft.Acp.GenerateMindMapSeed
            }
        });

        return merged;
    }

    private static List<WorkflowChapterProfile> MergeChapterProfiles(
        IReadOnlyCollection<WorkflowChapterProfile> existing,
        IReadOnlyCollection<WorkflowChapterProfile> suggested,
        string? draftSourceType)
    {
        var merged = existing
            .Select(chapter => RepositoryWorkflowConfigRules.SanitizeProfile(new RepositoryWorkflowProfile
            {
                ChapterProfiles = [chapter]
            }).ChapterProfiles[0])
            .ToDictionary(chapter => chapter.Key, StringComparer.OrdinalIgnoreCase);
        var shouldRefreshExistingSuggestedChapters = string.Equals(
            draftSourceType,
            "lsp-augment",
            StringComparison.OrdinalIgnoreCase);

        foreach (var suggestion in suggested)
        {
            if (merged.TryGetValue(suggestion.Key, out var current))
            {
                if (shouldRefreshExistingSuggestedChapters)
                {
                    current.Title = suggestion.Title;
                    current.Description = suggestion.Description;
                    current.RootSymbolNames = suggestion.RootSymbolNames.ToList();
                    current.MustExplainSymbols = suggestion.MustExplainSymbols.ToList();
                    current.OutputArtifacts = suggestion.OutputArtifacts.ToList();
                    current.DepthBudget = suggestion.DepthBudget;
                    current.MaxNodes = suggestion.MaxNodes;
                    current.AnalysisMode = suggestion.AnalysisMode;
                    current.IncludeFlowchart = suggestion.IncludeFlowchart;
                    current.IncludeMindmap = suggestion.IncludeMindmap;
                }
                else
                {
                    current.Description ??= suggestion.Description;
                    current.RootSymbolNames = current.RootSymbolNames
                        .Concat(suggestion.RootSymbolNames)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    current.MustExplainSymbols = current.MustExplainSymbols
                        .Concat(suggestion.MustExplainSymbols)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    current.OutputArtifacts = current.OutputArtifacts
                        .Concat(suggestion.OutputArtifacts)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    current.DepthBudget = Math.Max(current.DepthBudget, suggestion.DepthBudget);
                    current.MaxNodes = Math.Max(current.MaxNodes, suggestion.MaxNodes);
                    current.AnalysisMode = current.AnalysisMode == WorkflowChapterAnalysisMode.Deep ||
                                           suggestion.AnalysisMode == WorkflowChapterAnalysisMode.Deep
                        ? WorkflowChapterAnalysisMode.Deep
                        : WorkflowChapterAnalysisMode.Standard;
                    current.IncludeFlowchart = current.IncludeFlowchart || suggestion.IncludeFlowchart;
                    current.IncludeMindmap = current.IncludeMindmap || suggestion.IncludeMindmap;
                }

                current.RequiredSections = current.RequiredSections
                    .Concat(suggestion.RequiredSections)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                merged[suggestion.Key] = suggestion;
            }
        }

        return merged.Values.OrderBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> RemovePreviousAugmentSymbols(
        IReadOnlyCollection<string> currentSymbols,
        IReadOnlyCollection<string> previousSuggestedSymbols)
    {
        if (currentSymbols.Count == 0)
        {
            return [];
        }

        if (previousSuggestedSymbols.Count == 0)
        {
            return currentSymbols.ToList();
        }

        var previous = previousSuggestedSymbols
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return currentSymbols
            .Where(symbol => !previous.Contains(symbol))
            .ToList();
    }

    private static WorkflowLspAugmentResultDto MapAugment(WorkflowLspAugmentResult augment)
    {
        return new WorkflowLspAugmentResultDto
        {
            ProfileKey = augment.ProfileKey,
            Summary = augment.Summary,
            Strategy = augment.Strategy,
            FallbackReason = augment.FallbackReason,
            LspServerName = augment.LspServerName,
            SuggestedEntryDirectories = augment.SuggestedEntryDirectories.ToList(),
            SuggestedRootSymbolNames = augment.SuggestedRootSymbolNames.ToList(),
            SuggestedMustExplainSymbols = augment.SuggestedMustExplainSymbols.ToList(),
            SuggestedChapterProfiles = augment.SuggestedChapterProfiles.ToList(),
            CallHierarchyEdges = augment.CallHierarchyEdges.ToList(),
            EvidenceFiles = augment.EvidenceFiles.ToList(),
            Diagnostics = augment.Diagnostics.ToList(),
            ResolvedDefinitions = augment.ResolvedDefinitions.ToList(),
            ResolvedReferences = augment.ResolvedReferences.ToList()
        };
    }

    private static Expression<Func<WorkflowAnalysisSession, WorkflowAnalysisSessionSummaryDto>> MapAnalysisSessionSummaryExpression()
    {
        return session => new WorkflowAnalysisSessionSummaryDto
        {
            AnalysisSessionId = session.Id,
            RepositoryId = session.RepositoryId,
            WorkflowTemplateSessionId = session.WorkflowTemplateSessionId,
            ProfileKey = session.ProfileKey,
            DraftVersionNumber = session.DraftVersionNumber,
            ChapterKey = session.ChapterKey,
            Status = session.Status,
            Objective = session.Objective,
            Summary = session.Summary,
            TotalTasks = session.TotalTasks,
            CompletedTasks = session.CompletedTasks,
            FailedTasks = session.FailedTasks,
            PendingTaskCount = session.PendingTaskCount,
            RunningTaskCount = session.RunningTaskCount,
            CurrentTaskId = session.CurrentTaskId,
            ProgressMessage = session.ProgressMessage,
            CreatedAt = session.CreatedAt,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            LastActivityAt = session.LastActivityAt
        };
    }

    private async Task<Repository> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        return await _context.Repositories
            .FirstOrDefaultAsync(repository => repository.Id == repositoryId && !repository.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("仓库不存在。");
    }

    private async Task<WorkflowTemplateSession> GetTemplateSessionAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        return await _context.WorkflowTemplateSessions
            .FirstOrDefaultAsync(session =>
                session.RepositoryId == repositoryId &&
                session.Id == sessionId &&
                !session.IsDeleted,
                cancellationToken)
            ?? throw new KeyNotFoundException("模板会话不存在。");
    }

    private async Task<RepositoryBranch?> ResolveBranchAsync(
        string repositoryId,
        string? branchId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return await _context.RepositoryBranches
                .AsNoTracking()
                .Where(branch => branch.RepositoryId == repositoryId && !branch.IsDeleted)
                .OrderBy(branch => branch.BranchName == "main" ? 0 : 1)
                .ThenBy(branch => branch.BranchName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.RepositoryBranches
            .AsNoTracking()
            .FirstOrDefaultAsync(branch =>
                branch.RepositoryId == repositoryId &&
                branch.Id == branchId &&
                !branch.IsDeleted,
                cancellationToken);
    }

    private async Task<RepositoryWorkflowProfile?> GetCurrentDraftAsync(
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        if (versionNumber <= 0)
        {
            return null;
        }

        var draftJson = await _context.WorkflowTemplateDraftVersions
            .AsNoTracking()
            .Where(version => version.SessionId == sessionId &&
                              version.VersionNumber == versionNumber &&
                              !version.IsDeleted)
            .Select(version => version.DraftJson)
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

    private async Task<int> GetNextMessageSequenceAsync(string sessionId, CancellationToken cancellationToken)
    {
        var currentMax = await _context.WorkflowTemplateMessages
            .Where(message => message.SessionId == sessionId && !message.IsDeleted)
            .Select(message => (int?)message.SequenceNumber)
            .MaxAsync(cancellationToken);

        return (currentMax ?? 0) + 1;
    }

    private static RepositoryWorkflowProfile CreateDefaultDraft()
    {
        return RepositoryWorkflowConfigRules.SanitizeProfile(new RepositoryWorkflowProfile
        {
            Enabled = false,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            Source = new RepositoryWorkflowProfileSource
            {
                Type = "analysis-default"
            },
            DocumentPreferences = new WorkflowDocumentPreferences(),
            Analysis = new WorkflowProfileAnalysisOptions(),
            LspAssist = new WorkflowLspAssistOptions(),
            Acp = new WorkflowAcpOptions()
        });
    }

    private static List<string> DeserializeStringList(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(rawJson, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
