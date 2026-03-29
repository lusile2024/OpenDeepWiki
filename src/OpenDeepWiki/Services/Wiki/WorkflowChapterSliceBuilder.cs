namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowChapterSliceBuilder : IWorkflowChapterSliceBuilder
{
    public WorkflowChapterSlice Build(
        WorkflowSemanticGraph graph,
        RepositoryWorkflowProfile profile,
        WorkflowChapterProfile? chapterProfile = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(profile);

        chapterProfile ??= profile.ChapterProfiles.FirstOrDefault() ?? new WorkflowChapterProfile
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
        };

        var nodeById = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var nodeBySymbol = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.SymbolName))
            .GroupBy(node => node.SymbolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var outgoingEdges = graph.Edges
            .GroupBy(edge => edge.FromId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var rootSymbols = chapterProfile.RootSymbolNames.Count > 0
            ? chapterProfile.RootSymbolNames
            : profile.EntryRoots.Count > 0
                ? profile.EntryRoots
                : profile.Analysis.RootSymbolNames;
        var rootNodes = rootSymbols
            .Select(symbol => nodeBySymbol.GetValueOrDefault(symbol))
            .Where(node => node is not null)
            .Cast<WorkflowGraphNode>()
            .ToList();
        if (rootNodes.Count == 0)
        {
            rootNodes = graph.Nodes
                .Where(node => node.Kind is WorkflowNodeKind.Controller
                    or WorkflowNodeKind.Endpoint
                    or WorkflowNodeKind.BackgroundService
                    or WorkflowNodeKind.ExecutorFactory
                    or WorkflowNodeKind.Executor
                    or WorkflowNodeKind.Handler)
                .Take(4)
                .ToList();
        }

        var includedIds = Traverse(rootNodes, outgoingEdges, chapterProfile.DepthBudget, chapterProfile.MaxNodes);
        var sliceNodes = graph.Nodes.Where(node => includedIds.Contains(node.Id)).ToList();
        var sliceEdges = graph.Edges
            .Where(edge => includedIds.Contains(edge.FromId) && includedIds.Contains(edge.ToId))
            .ToList();

        var decisionPoints = sliceEdges
            .GroupBy(edge => edge.FromId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && nodeById.ContainsKey(group.Key))
            .Select(group =>
            {
                var source = nodeById[group.Key];
                return new WorkflowChapterDecisionPoint
                {
                    SymbolName = source.SymbolName,
                    OutgoingSymbols = group
                        .Select(edge => nodeById.GetValueOrDefault(edge.ToId)?.SymbolName)
                        .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToList()!,
                    Summary = $"该节点向 {group.Count()} 条后续路径分发，适合单独写业务分支判断。"
                };
            })
            .Where(point => !string.IsNullOrWhiteSpace(point.SymbolName))
            .Take(12)
            .ToList();

        var stateChanges = sliceEdges
            .Where(edge => edge.Kind is WorkflowEdgeKind.UpdatesStatus
                or WorkflowEdgeKind.Writes
                or WorkflowEdgeKind.ConsumesEntity
                or WorkflowEdgeKind.ProducesEntity)
            .Select(edge => new WorkflowChapterStateChange
            {
                FromSymbol = nodeById.GetValueOrDefault(edge.FromId)?.SymbolName ?? edge.FromId,
                ToSymbol = nodeById.GetValueOrDefault(edge.ToId)?.SymbolName ?? edge.ToId,
                ChangeType = edge.Kind.ToString()
            })
            .Take(16)
            .ToList();

        var extensionPoints = sliceNodes
            .Where(node => node.Kind is WorkflowNodeKind.Handler
                or WorkflowNodeKind.Service
                or WorkflowNodeKind.Repository
                or WorkflowNodeKind.ExternalClient)
            .Select(node => new WorkflowChapterExtensionPoint
            {
                SymbolName = node.SymbolName,
                ExtensionType = node.Kind.ToString(),
                Summary = $"可围绕 {node.Kind} 节点继续下钻实现、插件或策略分支。"
            })
            .Take(16)
            .ToList();

        var includedSymbols = sliceNodes
            .Select(node => node.SymbolName)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mustExplain = chapterProfile.MustExplainSymbols.Count > 0
            ? chapterProfile.MustExplainSymbols
            : profile.Analysis.MustExplainSymbols;
        var missingMustExplain = mustExplain
            .Where(symbol => !includedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkflowChapterSlice
        {
            ChapterKey = chapterProfile.Key,
            ChapterTitle = chapterProfile.Title,
            RootSymbolNames = rootNodes.Select(node => node.SymbolName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            NodeCount = sliceNodes.Count,
            EdgeCount = sliceEdges.Count,
            DecisionPoints = decisionPoints,
            StateChanges = stateChanges,
            ExtensionPoints = extensionPoints,
            MissingMustExplainSymbols = missingMustExplain,
            IncludedSymbols = includedSymbols,
            RequiredSections = chapterProfile.RequiredSections.ToList(),
            MindMapSeedMarkdown = chapterProfile.IncludeMindmap
                ? BuildMindMapMarkdown(chapterProfile.Title, rootNodes, decisionPoints, stateChanges)
                : string.Empty,
            FlowchartSeedMermaid = chapterProfile.IncludeFlowchart
                ? BuildFlowchartMermaid(rootNodes, sliceEdges, nodeById)
                : string.Empty
        };
    }

    private static HashSet<string> Traverse(
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyDictionary<string, List<WorkflowGraphEdge>> outgoingEdges,
        int depthBudget,
        int maxNodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string NodeId, int Depth)>();

        foreach (var node in rootNodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

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

    private static string BuildMindMapMarkdown(
        string title,
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyCollection<WorkflowChapterDecisionPoint> decisionPoints,
        IReadOnlyCollection<WorkflowChapterStateChange> stateChanges)
    {
        var lines = new List<string>
        {
            $"# {title}",
            "- 主线入口"
        };

        foreach (var root in rootNodes.Take(6))
        {
            lines.Add($"  - {root.SymbolName}");
        }

        if (decisionPoints.Count > 0)
        {
            lines.Add("- 关键分支");
            foreach (var decision in decisionPoints.Take(8))
            {
                lines.Add($"  - {decision.SymbolName}: {string.Join(" / ", decision.OutgoingSymbols.Take(4))}");
            }
        }

        if (stateChanges.Count > 0)
        {
            lines.Add("- 状态与持久化");
            foreach (var change in stateChanges.Take(8))
            {
                lines.Add($"  - {change.FromSymbol} -> {change.ToSymbol} ({change.ChangeType})");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildFlowchartMermaid(
        IReadOnlyCollection<WorkflowGraphNode> rootNodes,
        IReadOnlyCollection<WorkflowGraphEdge> edges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeById)
    {
        var lines = new List<string> { "flowchart TD" };
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges.Take(40))
        {
            if (!nodeById.TryGetValue(edge.FromId, out var fromNode) ||
                !nodeById.TryGetValue(edge.ToId, out var toNode) ||
                string.IsNullOrWhiteSpace(fromNode.SymbolName) ||
                string.IsNullOrWhiteSpace(toNode.SymbolName))
            {
                continue;
            }

            var fromId = NormalizeMermaidId(fromNode.SymbolName);
            var toId = NormalizeMermaidId(toNode.SymbolName);

            if (emitted.Add(fromId))
            {
                lines.Add($"    {fromId}[\"{EscapeMermaidLabel(fromNode.SymbolName)}\"]");
            }

            if (emitted.Add(toId))
            {
                lines.Add($"    {toId}[\"{EscapeMermaidLabel(toNode.SymbolName)}\"]");
            }

            lines.Add($"    {fromId} -->|{edge.Kind}| {toId}");
        }

        if (lines.Count == 1)
        {
            foreach (var root in rootNodes)
            {
                lines.Add($"    {NormalizeMermaidId(root.SymbolName)}[\"{EscapeMermaidLabel(root.SymbolName)}\"]");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeMermaidId(string symbolName)
    {
        var chars = symbolName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var value = new string(chars);
        return string.IsNullOrWhiteSpace(value) ? "node" : value;
    }

    private static string EscapeMermaidLabel(string value)
    {
        return value.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
