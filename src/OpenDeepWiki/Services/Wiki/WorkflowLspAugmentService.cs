using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki.Lsp;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowLspAugmentService : IWorkflowLspAugmentService
{
    private readonly IWorkflowExternalLspClient _externalLspClient;
    private readonly ILogger<WorkflowLspAugmentService> _logger;

    public WorkflowLspAugmentService(
        IWorkflowExternalLspClient externalLspClient,
        ILogger<WorkflowLspAugmentService> logger)
    {
        _externalLspClient = externalLspClient ?? throw new ArgumentNullException(nameof(externalLspClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowLspAugmentResult> AugmentAsync(
        RepositoryWorkspace workspace,
        RepositoryWorkflowProfile profile,
        WorkflowSemanticGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(graph);

        cancellationToken.ThrowIfCancellationRequested();

        var nodeById = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var outgoingEdges = graph.Edges
            .GroupBy(edge => edge.FromId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var incomingCount = graph.Edges
            .GroupBy(edge => edge.ToId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var nameHints = BuildNameHints(profile);
        var directoryHints = BuildDirectoryHints(profile);
        var rootNodes = ResolveRootNodes(workspace, graph.Nodes, nameHints, directoryHints);
        var includedNodeIds = TraverseReachableNodes(rootNodes, outgoingEdges, profile.Analysis.DepthBudget, profile.Analysis.MaxNodes);
        var includedNodes = graph.Nodes
            .Where(node => includedNodeIds.Contains(node.Id))
            .ToList();
        var visitedEdges = graph.Edges
            .Where(edge => includedNodeIds.Contains(edge.FromId) && includedNodeIds.Contains(edge.ToId))
            .ToList();

        var fallback = BuildRoslynFallbackAugment(
            workspace,
            profile,
            rootNodes,
            includedNodes,
            visitedEdges,
            nodeById,
            outgoingEdges,
            incomingCount);

        if (!profile.LspAssist.Enabled)
        {
            fallback.Strategy = "disabled";
            fallback.FallbackReason = "profile 已关闭 LSP 增强。";
            fallback.Diagnostics.Add(new WorkflowLspDiagnostic
            {
                Level = "info",
                Message = fallback.FallbackReason
            });
            return fallback;
        }

        var external = await TryExternalAugmentAsync(workspace, profile, rootNodes, cancellationToken);
        if (!external.Success)
        {
            fallback.FallbackReason = external.FailureReason;
            fallback.LspServerName = external.ServerName;
            fallback.ResolvedDefinitions = external.Definitions;
            fallback.ResolvedReferences = external.References;
            fallback.Diagnostics = external.Diagnostics.Count > 0
                ? external.Diagnostics
                : fallback.Diagnostics;
            fallback.Summary =
                $"{fallback.Summary} 当前 external LSP 未命中，已回退 Roslyn fallback。";
            return fallback;
        }

        var merged = MergeExternalAugment(workspace, fallback, external);
        _logger.LogInformation(
            "Workflow LSP augment completed for profile {ProfileKey}. Strategy: {Strategy}, Roots: {RootCount}, MustExplain: {ExplainCount}, Chapters: {ChapterCount}",
            profile.Key,
            merged.Strategy,
            merged.SuggestedRootSymbolNames.Count,
            merged.SuggestedMustExplainSymbols.Count,
            merged.SuggestedChapterProfiles.Count);

        return merged;
    }

    private static WorkflowLspAugmentResult BuildRoslynFallbackAugment(
        RepositoryWorkspace workspace,
        RepositoryWorkflowProfile profile,
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyCollection<WorkflowGraphNode> includedNodes,
        IReadOnlyCollection<WorkflowGraphEdge> visitedEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeById,
        IReadOnlyDictionary<string, List<WorkflowGraphEdge>> outgoingEdges,
        IReadOnlyDictionary<string, int> incomingCount)
    {
        var suggestedRootSymbols = rootNodes
            .Select(node => node.SymbolName)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var suggestedMustExplain = PickMustExplainSymbols(includedNodes, rootNodes, outgoingEdges, incomingCount);
        var suggestedDirectories = rootNodes
            .Select(node => GetRelativeDirectory(workspace.WorkingDirectory, node.FilePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var callHierarchyEdges = visitedEdges
            .Select(edge => MapCallHierarchyEdge(edge, nodeById))
            .Where(static edge => edge is not null)
            .Take(80)
            .Cast<WorkflowCallHierarchyEdge>()
            .ToList();
        var evidenceFiles = includedNodes
            .Select(node => GetRelativeFilePath(workspace.WorkingDirectory, node.FilePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();

        return SanitizeAugmentResult(new WorkflowLspAugmentResult
        {
            ProfileKey = profile.Key,
            Strategy = "roslyn-fallback",
            Summary =
                $"基于 Roslyn 语义图补充了 {suggestedRootSymbols.Count} 个 root symbol、{suggestedMustExplain.Count} 个 must-explain symbol、{callHierarchyEdges.Count} 条调用层级边。",
            SuggestedEntryDirectories = suggestedDirectories,
            SuggestedRootSymbolNames = suggestedRootSymbols,
            SuggestedMustExplainSymbols = suggestedMustExplain,
            SuggestedChapterProfiles = BuildSuggestedChapters(profile, rootNodes, includedNodes, suggestedMustExplain, visitedEdges, nodeById),
            CallHierarchyEdges = callHierarchyEdges,
            EvidenceFiles = evidenceFiles
        });
    }

    private async Task<WorkflowExternalLspSymbolResult> TryExternalAugmentAsync(
        RepositoryWorkspace workspace,
        RepositoryWorkflowProfile profile,
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        CancellationToken cancellationToken)
    {
        var attempts = new List<WorkflowExternalLspSymbolResult>();
        foreach (var rootNode in rootNodes
                     .Where(node => node.LineNumber.HasValue && node.ColumnNumber.HasValue && !string.IsNullOrWhiteSpace(node.FilePath))
                     .Take(4))
        {
            var absoluteFilePath = Path.IsPathRooted(rootNode.FilePath)
                ? rootNode.FilePath
                : Path.GetFullPath(Path.Combine(workspace.WorkingDirectory, rootNode.FilePath));
            var result = await _externalLspClient.AnalyzeSymbolAsync(
                new WorkflowExternalLspSymbolRequest
                {
                    WorkspacePath = workspace.WorkingDirectory,
                    FilePath = absoluteFilePath,
                    SymbolName = rootNode.SymbolName,
                    LineNumber = rootNode.LineNumber ?? 1,
                    ColumnNumber = rootNode.ColumnNumber ?? 1,
                    PreferredServer = profile.LspAssist.PreferredServer,
                    AssistOptions = profile.LspAssist
                },
                cancellationToken);
            attempts.Add(result);

            if (!result.Attempted && string.Equals(result.Strategy, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }
        }

        if (attempts.Count == 0)
        {
            return new WorkflowExternalLspSymbolResult
            {
                Attempted = true,
                Success = false,
                Strategy = "external-lsp",
                FailureReason = "未找到可用于 external LSP 请求的符号定位信息。"
            };
        }

        var successfulAttempts = attempts.Where(item => item.Success).ToList();
        if (successfulAttempts.Count == 0)
        {
            var failureReason = string.Join("；", attempts
                .Select(item => item.FailureReason)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return new WorkflowExternalLspSymbolResult
            {
                Attempted = true,
                Success = false,
                Strategy = "external-lsp",
                ServerName = attempts.Select(item => item.ServerName).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)),
                FailureReason = string.IsNullOrWhiteSpace(failureReason)
                    ? "external LSP 未返回可用结果。"
                    : failureReason,
                Diagnostics = attempts.SelectMany(item => item.Diagnostics).ToList(),
                Definitions = attempts.SelectMany(item => item.Definitions).ToList(),
                References = attempts.SelectMany(item => item.References).ToList()
            };
        }

        return new WorkflowExternalLspSymbolResult
        {
            Attempted = true,
            Success = true,
            Strategy = "external-lsp",
            ServerName = successfulAttempts.Select(item => item.ServerName).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)),
            Diagnostics = attempts.SelectMany(item => item.Diagnostics)
                .GroupBy(item => $"{item.Level}|{item.Message}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            Definitions = successfulAttempts.SelectMany(item => item.Definitions)
                .GroupBy(item => $"{item.FilePath}|{item.LineNumber}|{item.ColumnNumber}|{item.Source}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            References = successfulAttempts.SelectMany(item => item.References)
                .GroupBy(item => $"{item.FilePath}|{item.LineNumber}|{item.ColumnNumber}|{item.Source}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            CallHierarchyEdges = successfulAttempts.SelectMany(item => item.CallHierarchyEdges)
                .GroupBy(item => $"{item.FromSymbol}|{item.ToSymbol}|{item.Kind}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            SuggestedRootSymbolNames = successfulAttempts.SelectMany(item => item.SuggestedRootSymbolNames)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SuggestedMustExplainSymbols = successfulAttempts.SelectMany(item => item.SuggestedMustExplainSymbols)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static WorkflowLspAugmentResult MergeExternalAugment(
        RepositoryWorkspace workspace,
        WorkflowLspAugmentResult fallback,
        WorkflowExternalLspSymbolResult external)
    {
        var directoriesFromExternal = external.Definitions
            .Concat(external.References)
            .Select(item => GetRelativeDirectory(workspace.WorkingDirectory, item.FilePath))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return SanitizeAugmentResult(new WorkflowLspAugmentResult
        {
            ProfileKey = fallback.ProfileKey,
            Strategy = "external-lsp",
            Summary =
                $"external LSP 成功补充了 {external.Definitions.Count} 个定义命中、{external.References.Count} 个引用命中、{external.CallHierarchyEdges.Count} 条调用层级边，并保留 Roslyn 图作为基线。",
            FallbackReason = null,
            LspServerName = external.ServerName,
            SuggestedEntryDirectories = fallback.SuggestedEntryDirectories
                .Concat(directoriesFromExternal)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SuggestedRootSymbolNames = fallback.SuggestedRootSymbolNames
                .Concat(external.SuggestedRootSymbolNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SuggestedMustExplainSymbols = fallback.SuggestedMustExplainSymbols
                .Concat(external.SuggestedMustExplainSymbols)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SuggestedChapterProfiles = MergeSuggestedChapterProfiles(fallback.SuggestedChapterProfiles, external),
            CallHierarchyEdges = fallback.CallHierarchyEdges
                .Concat(external.CallHierarchyEdges)
                .GroupBy(item => $"{item.FromSymbol}|{item.ToSymbol}|{item.Kind}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            EvidenceFiles = fallback.EvidenceFiles
                .Concat(external.Definitions.Select(item => GetRelativeFilePath(workspace.WorkingDirectory, item.FilePath)))
                .Concat(external.References.Select(item => GetRelativeFilePath(workspace.WorkingDirectory, item.FilePath)))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToList(),
            Diagnostics = external.Diagnostics,
            ResolvedDefinitions = external.Definitions
                .Select(item => new WorkflowLspResolvedLocation
                {
                    SymbolName = item.SymbolName,
                    FilePath = GetRelativeFilePath(workspace.WorkingDirectory, item.FilePath),
                    LineNumber = item.LineNumber,
                    ColumnNumber = item.ColumnNumber,
                    Source = item.Source
                })
                .ToList(),
            ResolvedReferences = external.References
                .Select(item => new WorkflowLspResolvedLocation
                {
                    SymbolName = item.SymbolName,
                    FilePath = GetRelativeFilePath(workspace.WorkingDirectory, item.FilePath),
                    LineNumber = item.LineNumber,
                    ColumnNumber = item.ColumnNumber,
                    Source = item.Source
                })
                .ToList()
        });
    }

    private static List<WorkflowChapterProfile> MergeSuggestedChapterProfiles(
        IReadOnlyCollection<WorkflowChapterProfile> chapters,
        WorkflowExternalLspSymbolResult external)
    {
        if (chapters.Count == 0)
        {
            return [];
        }

        var merged = chapters.Select(chapter => new WorkflowChapterProfile
        {
            Key = chapter.Key,
            Title = chapter.Title,
            Description = chapter.Description,
            AnalysisMode = chapter.AnalysisMode,
            RootSymbolNames = chapter.RootSymbolNames.ToList(),
            MustExplainSymbols = chapter.MustExplainSymbols.ToList(),
            RequiredSections = chapter.RequiredSections.ToList(),
            OutputArtifacts = chapter.OutputArtifacts.ToList(),
            DepthBudget = chapter.DepthBudget,
            MaxNodes = chapter.MaxNodes,
            IncludeFlowchart = chapter.IncludeFlowchart,
            IncludeMindmap = chapter.IncludeMindmap
        }).ToList();

        var firstChapter = merged[0];
        firstChapter.RootSymbolNames = firstChapter.RootSymbolNames
            .Concat(external.SuggestedRootSymbolNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        firstChapter.MustExplainSymbols = firstChapter.MustExplainSymbols
            .Concat(external.SuggestedMustExplainSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (firstChapter.AnalysisMode == WorkflowChapterAnalysisMode.Standard &&
            external.CallHierarchyEdges.Count > 0)
        {
            firstChapter.AnalysisMode = WorkflowChapterAnalysisMode.Deep;
        }

        return merged;
    }

    private static List<string> BuildNameHints(RepositoryWorkflowProfile profile)
    {
        return profile.EntryRoots
            .Concat(profile.AnchorNames)
            .Concat(profile.PrimaryTriggerNames)
            .Concat(profile.CompensationTriggerNames)
            .Concat(profile.SchedulerNames)
            .Concat(profile.RequestEntityNames)
            .Concat(profile.RequestServiceNames)
            .Concat(profile.RequestRepositoryNames)
            .Concat(profile.Analysis.RootSymbolNames)
            .Concat(profile.Analysis.MustExplainSymbols)
            .Concat(profile.LspAssist.AdditionalEntrySymbolHints)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildDirectoryHints(RepositoryWorkflowProfile profile)
    {
        return profile.AnchorDirectories
            .Concat(profile.PrimaryTriggerDirectories)
            .Concat(profile.CompensationTriggerDirectories)
            .Concat(profile.SchedulerDirectories)
            .Concat(profile.ServiceDirectories)
            .Concat(profile.RepositoryDirectories)
            .Concat(profile.Analysis.EntryDirectories)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<WorkflowGraphNode> ResolveRootNodes(
        RepositoryWorkspace workspace,
        IEnumerable<WorkflowGraphNode> nodes,
        IReadOnlyCollection<string> nameHints,
        IReadOnlyCollection<string> directoryHints)
    {
        var byName = nodes
            .Where(node => MatchesNameHints(node, nameHints))
            .OrderBy(node => GetNodePriority(node.Kind))
            .ThenBy(node => node.SymbolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byName.Count > 0)
        {
            return byName.Take(8).ToList();
        }

        var byDirectory = nodes
            .Where(node => MatchesDirectoryHints(workspace, node, directoryHints))
            .OrderBy(node => GetNodePriority(node.Kind))
            .ThenBy(node => node.SymbolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byDirectory.Count > 0)
        {
            return byDirectory.Take(8).ToList();
        }

        return nodes
            .Where(node => node.Kind is WorkflowNodeKind.Controller
                or WorkflowNodeKind.Endpoint
                or WorkflowNodeKind.BackgroundService
                or WorkflowNodeKind.HostedService
                or WorkflowNodeKind.ExecutorFactory
                or WorkflowNodeKind.Executor
                or WorkflowNodeKind.Handler)
            .OrderBy(node => GetNodePriority(node.Kind))
            .ThenBy(node => node.SymbolName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static HashSet<string> TraverseReachableNodes(
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyDictionary<string, List<WorkflowGraphEdge>> outgoingEdges,
        int depthBudget,
        int maxNodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string NodeId, int Depth)>();

        foreach (var node in rootNodes)
        {
            if (result.Add(node.Id))
            {
                queue.Enqueue((node.Id, 0));
            }
        }

        while (queue.Count > 0 && result.Count < maxNodes)
        {
            var (nodeId, depth) = queue.Dequeue();
            if (depth >= depthBudget || !outgoingEdges.TryGetValue(nodeId, out var nextEdges))
            {
                continue;
            }

            foreach (var edge in nextEdges)
            {
                if (result.Count >= maxNodes)
                {
                    break;
                }

                if (result.Add(edge.ToId))
                {
                    queue.Enqueue((edge.ToId, depth + 1));
                }
            }
        }

        return result;
    }

    private static List<string> PickMustExplainSymbols(
        IReadOnlyCollection<WorkflowGraphNode> includedNodes,
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyDictionary<string, List<WorkflowGraphEdge>> outgoingEdges,
        IReadOnlyDictionary<string, int> incomingCount)
    {
        var rootIds = rootNodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return includedNodes
            .Where(node => !rootIds.Contains(node.Id))
            .Where(ShouldKeepMustExplainNode)
            .OrderByDescending(node => (outgoingEdges.TryGetValue(node.Id, out var next) ? next.Count : 0) +
                                       incomingCount.GetValueOrDefault(node.Id, 0))
            .ThenBy(node => GetNodePriority(node.Kind))
            .ThenBy(node => node.SymbolName, StringComparer.OrdinalIgnoreCase)
            .Select(node => node.SymbolName)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static List<WorkflowChapterProfile> BuildSuggestedChapters(
        RepositoryWorkflowProfile profile,
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyCollection<WorkflowGraphNode> includedNodes,
        IReadOnlyCollection<string> suggestedMustExplain,
        IReadOnlyCollection<WorkflowGraphEdge> visitedEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeById)
    {
        var decisionRoots = visitedEdges
            .GroupBy(edge => edge.FromId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && nodeById.ContainsKey(group.Key))
            .Select(group => nodeById[group.Key])
            .ToList();
        var serviceNodes = includedNodes
            .Where(node => node.Kind is WorkflowNodeKind.Executor
                or WorkflowNodeKind.ExecutorFactory
                or WorkflowNodeKind.Handler
                or WorkflowNodeKind.Service)
            .ToList();
        var persistenceNodes = visitedEdges
            .Where(edge => edge.Kind is WorkflowEdgeKind.Reads
                or WorkflowEdgeKind.Writes
                or WorkflowEdgeKind.Queries
                or WorkflowEdgeKind.UpdatesStatus)
            .Select(edge => nodeById.GetValueOrDefault(edge.FromId))
            .Where(node => node is not null)
            .Cast<WorkflowGraphNode>()
            .Where(ShouldKeepPersistenceChapterRootNode)
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var completionNodes = includedNodes
            .Where(node => node.Kind is WorkflowNodeKind.ExternalClient
                or WorkflowNodeKind.StatusEnum
                or WorkflowNodeKind.Controller
                or WorkflowNodeKind.Endpoint)
            .ToList();

        var chapters = new List<WorkflowChapterProfile>();
        AddChapter(
            chapters,
            "entry-and-dispatch",
            "入口与调度主线",
            "围绕入口触发、请求落库、调度扫描和执行器分发建立主链路。",
            rootNodes,
            suggestedMustExplain.Take(8).ToList(),
            ["入口触发链路", "请求落库与调度扫描", "执行器分发主线"],
            ["markdown", "flowchart", "mindmap"]);

        AddChapter(
            chapters,
            "branch-decisions",
            "分支判断与场景差异",
            "收敛关键 if/switch/策略分发节点，适合给 ACP 继续往下钻。",
            decisionRoots,
            suggestedMustExplain.Take(12).ToList(),
            ["业务分支判断逻辑总览", "各场景入口条件与分流结果"],
            ["markdown", "flowchart"]);

        AddChapter(
            chapters,
            "service-orchestration",
            "核心服务编排",
            "聚焦服务层、执行器和处理器之间的调用编排。",
            serviceNodes,
            suggestedMustExplain.Take(16).ToList(),
            ["核心服务编排", "关键方法调用链"],
            ["markdown", "mindmap"]);

        AddChapter(
            chapters,
            "persistence-and-status",
            "持久化与状态变更",
            "聚焦请求表、仓储查询、状态变更、实体写入与完成收尾。",
            persistenceNodes.Count > 0 ? persistenceNodes : completionNodes,
            suggestedMustExplain.Skip(6).Take(12).ToList(),
            ["持久化写入与查询路径", "状态变更规则", "收尾与补偿链路"],
            ["markdown", "flowchart"]);

        if (profile.DocumentPreferences.RequiredSections.Count > 0 && chapters.Count > 0)
        {
            chapters[0].RequiredSections = chapters[0].RequiredSections
                .Concat(profile.DocumentPreferences.RequiredSections)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return chapters
            .Where(chapter => chapter.RootSymbolNames.Count > 0 || chapter.MustExplainSymbols.Count > 0)
            .Take(6)
            .ToList();
    }

    private static void AddChapter(
        ICollection<WorkflowChapterProfile> chapters,
        string key,
        string title,
        string description,
        IReadOnlyCollection<WorkflowGraphNode> nodes,
        IReadOnlyCollection<string> mustExplainSymbols,
        IReadOnlyCollection<string> requiredSections,
        IReadOnlyCollection<string> outputArtifacts)
    {
        var rootSymbols = nodes
            .Where(ShouldKeepChapterRootNode)
            .Select(node => node.SymbolName)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var mustExplain = NormalizeSymbols(mustExplainSymbols, 16);

        if (rootSymbols.Count == 0 && mustExplain.Count == 0)
        {
            return;
        }

        chapters.Add(new WorkflowChapterProfile
        {
            Key = key,
            Title = title,
            Description = description,
            AnalysisMode = string.Equals(key, "branch-decisions", StringComparison.OrdinalIgnoreCase)
                ? WorkflowChapterAnalysisMode.Deep
                : WorkflowChapterAnalysisMode.Standard,
            RootSymbolNames = rootSymbols,
            MustExplainSymbols = mustExplain,
            RequiredSections = requiredSections.ToList(),
            OutputArtifacts = outputArtifacts.ToList(),
            DepthBudget = 3,
            MaxNodes = 28,
            IncludeFlowchart = outputArtifacts.Contains("flowchart", StringComparer.OrdinalIgnoreCase),
            IncludeMindmap = outputArtifacts.Contains("mindmap", StringComparer.OrdinalIgnoreCase)
        });
    }

    private static WorkflowLspAugmentResult SanitizeAugmentResult(WorkflowLspAugmentResult result)
    {
        var rootSymbols = NormalizeSymbols(result.SuggestedRootSymbolNames, 16);
        var mustExplainSymbols = NormalizeSymbols(result.SuggestedMustExplainSymbols, 24)
            .Except(rootSymbols, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkflowLspAugmentResult
        {
            ProfileKey = result.ProfileKey,
            Summary = result.Summary,
            Strategy = result.Strategy,
            FallbackReason = result.FallbackReason,
            LspServerName = result.LspServerName,
            SuggestedEntryDirectories = result.SuggestedEntryDirectories
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SuggestedRootSymbolNames = rootSymbols,
            SuggestedMustExplainSymbols = mustExplainSymbols,
            SuggestedChapterProfiles = result.SuggestedChapterProfiles
                .Select(SanitizeChapterProfile)
                .Where(chapter => chapter.RootSymbolNames.Count > 0 || chapter.MustExplainSymbols.Count > 0)
                .Take(6)
                .ToList(),
            CallHierarchyEdges = result.CallHierarchyEdges
                .Where(ShouldKeepCallHierarchyEdge)
                .GroupBy(item => $"{item.FromSymbol}|{item.ToSymbol}|{item.Kind}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            EvidenceFiles = result.EvidenceFiles
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Diagnostics = result.Diagnostics,
            ResolvedDefinitions = result.ResolvedDefinitions,
            ResolvedReferences = result.ResolvedReferences
        };
    }

    private static WorkflowChapterProfile SanitizeChapterProfile(WorkflowChapterProfile chapter)
    {
        var rootSymbols = NormalizeSymbols(chapter.RootSymbolNames, 8);
        var mustExplainSymbols = NormalizeSymbols(chapter.MustExplainSymbols, 16)
            .Except(rootSymbols, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkflowChapterProfile
        {
            Key = chapter.Key,
            Title = chapter.Title,
            Description = chapter.Description,
            AnalysisMode = chapter.AnalysisMode,
            RootSymbolNames = rootSymbols,
            MustExplainSymbols = mustExplainSymbols,
            RequiredSections = chapter.RequiredSections
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OutputArtifacts = chapter.OutputArtifacts
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DepthBudget = chapter.DepthBudget,
            MaxNodes = chapter.MaxNodes,
            IncludeFlowchart = chapter.IncludeFlowchart,
            IncludeMindmap = chapter.IncludeMindmap
        };
    }

    private static List<string> NormalizeSymbols(IEnumerable<string> symbols, int take)
    {
        return symbols
            .Where(ShouldKeepLooseSymbol)
            .Select(symbol => symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static bool ShouldKeepMustExplainNode(WorkflowGraphNode node)
    {
        if (IsEntityLikeNode(node))
        {
            return false;
        }

        return node.Kind is WorkflowNodeKind.Executor
            or WorkflowNodeKind.ExecutorFactory
            or WorkflowNodeKind.Handler
            or WorkflowNodeKind.Service
            or WorkflowNodeKind.ExternalClient
            || node.Kind == WorkflowNodeKind.Repository && !IsInfrastructureRepositorySymbol(node.SymbolName);
    }

    private static bool ShouldKeepChapterRootNode(WorkflowGraphNode node)
    {
        if (IsEntityLikeNode(node))
        {
            return false;
        }

        return node.Kind is WorkflowNodeKind.Controller
            or WorkflowNodeKind.Endpoint
            or WorkflowNodeKind.BackgroundService
            or WorkflowNodeKind.HostedService
            or WorkflowNodeKind.ExecutorFactory
            or WorkflowNodeKind.Executor
            or WorkflowNodeKind.Handler
            or WorkflowNodeKind.Service
            or WorkflowNodeKind.ExternalClient
            || node.Kind == WorkflowNodeKind.Repository && !IsInfrastructureRepositorySymbol(node.SymbolName);
    }

    private static bool ShouldKeepPersistenceChapterRootNode(WorkflowGraphNode node)
    {
        if (IsEntityLikeNode(node))
        {
            return false;
        }

        return node.Kind is WorkflowNodeKind.Executor
            or WorkflowNodeKind.ExecutorFactory
            or WorkflowNodeKind.Handler
            or WorkflowNodeKind.Service
            or WorkflowNodeKind.ExternalClient
            or WorkflowNodeKind.BackgroundService
            or WorkflowNodeKind.HostedService
            || node.Kind == WorkflowNodeKind.Repository && !IsInfrastructureRepositorySymbol(node.SymbolName);
    }

    private static bool ShouldKeepCallHierarchyNode(WorkflowGraphNode node)
    {
        return ShouldKeepChapterRootNode(node) || ShouldKeepMustExplainNode(node);
    }

    private static bool ShouldKeepCallHierarchyEdge(WorkflowCallHierarchyEdge edge)
    {
        return ShouldKeepLooseSymbol(edge.FromSymbol) &&
               ShouldKeepLooseSymbol(edge.ToSymbol);
    }

    private static bool ShouldKeepLooseSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var trimmed = symbol.Trim();
        var lastSegment = GetLastSymbolSegment(trimmed);
        if (string.IsNullOrWhiteSpace(lastSegment))
        {
            return false;
        }

        if (IsPrimitiveLikeSymbol(trimmed) ||
            IsInfrastructureNamespaceSymbol(trimmed) ||
            IsCollectionOrGenericSymbol(trimmed) ||
            IsUtilityLikeSymbol(trimmed, lastSegment) ||
            IsEntityLikeSymbol(trimmed, lastSegment))
        {
            return false;
        }

        return true;
    }

    private static bool IsEntityLikeNode(WorkflowGraphNode node)
    {
        return node.Kind is WorkflowNodeKind.Entity
            or WorkflowNodeKind.RequestEntity
            or WorkflowNodeKind.DbContext
            or WorkflowNodeKind.StatusEnum
            || IsEntityLikeSymbol(node.SymbolName, GetLastSymbolSegment(node.SymbolName));
    }

    private static bool IsPrimitiveLikeSymbol(string symbol)
    {
        return symbol switch
        {
            "string" or "int" or "long" or "short" or "byte" or "bool" or "decimal" or "double" or "float" or
            "Guid" or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Task" or "ValueTask" or "void" => true,
            _ => false
        };
    }

    private static bool IsInfrastructureNamespaceSymbol(string symbol)
    {
        return symbol.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               symbol.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               symbol.StartsWith("FreeSql.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCollectionOrGenericSymbol(string symbol)
    {
        if (symbol.Contains('<', StringComparison.Ordinal) || symbol.Contains('>', StringComparison.Ordinal))
        {
            return true;
        }

        var lastSegment = GetLastSymbolSegment(symbol);
        return lastSegment.StartsWith("List", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("IEnumerable", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("ICollection", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("IReadOnlyCollection", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("IQueryable", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("ISelect", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUtilityLikeSymbol(string symbol, string lastSegment)
    {
        return symbol.Contains(".Extensions.", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith("Extensions", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith("Extension", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith("Helper", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith("Mapper", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntityLikeSymbol(string symbol, string? lastSegment = null)
    {
        var name = string.IsNullOrWhiteSpace(lastSegment)
            ? GetLastSymbolSegment(symbol)
            : lastSegment;

        return symbol.Contains(".Entities.", StringComparison.OrdinalIgnoreCase) ||
               symbol.Contains(".Domain.External.", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Body", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Record", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInfrastructureRepositorySymbol(string symbol)
    {
        var lastSegment = GetLastSymbolSegment(symbol);
        return IsInfrastructureNamespaceSymbol(symbol) ||
               IsCollectionOrGenericSymbol(symbol) ||
               lastSegment.StartsWith("IBaseRepository", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.StartsWith("ISelect", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLastSymbolSegment(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var trimmed = symbol.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 && lastDot < trimmed.Length - 1
            ? trimmed[(lastDot + 1)..]
            : trimmed;
    }

    private static WorkflowCallHierarchyEdge? MapCallHierarchyEdge(
        WorkflowGraphEdge edge,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeById)
    {
        if (!nodeById.TryGetValue(edge.FromId, out var fromNode) ||
            !nodeById.TryGetValue(edge.ToId, out var toNode) ||
            string.IsNullOrWhiteSpace(fromNode.SymbolName) ||
            string.IsNullOrWhiteSpace(toNode.SymbolName) ||
            !ShouldKeepCallHierarchyNode(fromNode) ||
            !ShouldKeepCallHierarchyNode(toNode))
        {
            return null;
        }

        return new WorkflowCallHierarchyEdge
        {
            FromSymbol = fromNode.SymbolName,
            ToSymbol = toNode.SymbolName,
            Kind = edge.Kind.ToString(),
            Reason = GetRelativeFilePath(null, fromNode.FilePath)
        };
    }

    private static bool MatchesNameHints(WorkflowGraphNode node, IReadOnlyCollection<string> nameHints)
    {
        if (nameHints.Count == 0)
        {
            return false;
        }

        return nameHints.Any(hint =>
            MatchesName(node.SymbolName, hint) ||
            MatchesName(node.DisplayName, hint));
    }

    private static bool MatchesName(string? candidate, string? hint)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        return string.Equals(candidate, hint, StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith($".{hint}", StringComparison.OrdinalIgnoreCase) ||
               candidate.Contains(hint, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDirectoryHints(
        RepositoryWorkspace workspace,
        WorkflowGraphNode node,
        IReadOnlyCollection<string> directoryHints)
    {
        if (directoryHints.Count == 0 || string.IsNullOrWhiteSpace(node.FilePath))
        {
            return false;
        }

        var relativePath = GetRelativeFilePath(workspace.WorkingDirectory, node.FilePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        return directoryHints.Any(hint =>
            relativePath.StartsWith(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetNodePriority(WorkflowNodeKind kind)
    {
        return kind switch
        {
            WorkflowNodeKind.Controller => 0,
            WorkflowNodeKind.Endpoint => 1,
            WorkflowNodeKind.BackgroundService => 2,
            WorkflowNodeKind.HostedService => 3,
            WorkflowNodeKind.ExecutorFactory => 4,
            WorkflowNodeKind.Executor => 5,
            WorkflowNodeKind.Handler => 6,
            WorkflowNodeKind.Service => 7,
            WorkflowNodeKind.Repository => 8,
            WorkflowNodeKind.RequestEntity => 9,
            _ => 20
        };
    }

    private static string GetRelativeDirectory(string rootPath, string? filePath)
    {
        var relativeFilePath = GetRelativeFilePath(rootPath, filePath);
        if (string.IsNullOrWhiteSpace(relativeFilePath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(relativeFilePath)?.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(directory) ? string.Empty : directory;
    }

    private static string GetRelativeFilePath(string? rootPath, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return filePath.Replace('\\', '/');
        }

        try
        {
            return Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        }
        catch
        {
            return filePath.Replace('\\', '/');
        }
    }
}
