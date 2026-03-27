using System.Text.Json;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowCandidateExtractor
{
    public List<WorkflowTopicCandidate> ExtractCandidates(
        WorkflowSemanticGraph graph,
        RepositoryWorkflowProfile? activeProfile = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes.Count == 0)
        {
            return [];
        }

        var nodeMap = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var metadataMap = graph.Nodes.ToDictionary(node => node.Id, GetNodeMetadata, StringComparer.Ordinal);
        var adjacency = BuildAdjacency(graph);
        var candidatesByKey = new Dictionary<string, WorkflowTopicCandidate>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (!visited.Add(node.Id))
            {
                continue;
            }

            var componentIds = TraverseComponent(node.Id, adjacency, visited);
            var componentNodes = componentIds.Select(id => nodeMap[id]).ToList();
            var componentEdges = graph.Edges
                .Where(edge => componentIds.Contains(edge.FromId) && componentIds.Contains(edge.ToId))
                .ToList();

            foreach (var candidate in ExtractComponentCandidates(
                         componentIds,
                         componentNodes,
                         componentEdges,
                         nodeMap,
                         metadataMap,
                         adjacency,
                         activeProfile))
            {
                if (!candidatesByKey.TryGetValue(candidate.Key, out var existing) || candidate.Score > existing.Score)
                {
                    candidatesByKey[candidate.Key] = candidate;
                }
            }
        }

        return candidatesByKey.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<WorkflowTopicCandidate> ExtractComponentCandidates(
        IReadOnlySet<string> componentIds,
        List<WorkflowGraphNode> componentNodes,
        List<WorkflowGraphEdge> componentEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        RepositoryWorkflowProfile? activeProfile)
    {
        var anchors = componentNodes
            .Where(node => IsConcreteExecutionAnchor(node, metadataMap))
            .Where(node => activeProfile is null || IsProfileEligibleAnchor(node, metadataMap, activeProfile))
            .OrderBy(node => node.DisplayName, StringComparer.Ordinal)
            .ToList();

        var emittedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var anchor in anchors)
        {
            var candidate = TryBuildAnchoredCandidate(anchor, componentIds, componentEdges, nodeMap, metadataMap, adjacency, activeProfile);
            if (candidate is null || !emittedKeys.Add(candidate.Key))
            {
                continue;
            }

            yield return candidate;
        }

        if (activeProfile is null && emittedKeys.Count == 0 && IsWorkflowCandidate(componentNodes))
        {
            yield return BuildCandidate(componentNodes, componentEdges, metadataMap);
        }
    }

    private static WorkflowTopicCandidate? TryBuildAnchoredCandidate(
        WorkflowGraphNode anchor,
        IReadOnlySet<string> componentIds,
        IReadOnlyCollection<WorkflowGraphEdge> componentEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        RepositoryWorkflowProfile? activeProfile)
    {
        return IsWcsRequestExecutorAnchor(anchor, metadataMap, activeProfile)
            ? TryBuildWcsRequestExecutorCandidate(anchor, componentIds, componentEdges, nodeMap, metadataMap, adjacency, activeProfile)
            : TryBuildGenericAnchoredCandidate(anchor, componentIds, componentEdges, nodeMap, metadataMap, adjacency);
    }

    private static WorkflowTopicCandidate? TryBuildGenericAnchoredCandidate(
        WorkflowGraphNode anchor,
        IReadOnlySet<string> componentIds,
        IReadOnlyCollection<WorkflowGraphEdge> componentEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency)
    {
        var selectedIds = new HashSet<string>(StringComparer.Ordinal) { anchor.Id };

        AddDirectRequestNodes(anchor.Id, componentIds, nodeMap, adjacency, selectedIds);

        if (!selectedIds.Any(id => nodeMap[id].Kind == WorkflowNodeKind.RequestEntity))
        {
            AddBestPath(
                anchor.Id,
                componentIds,
                nodeMap,
                adjacency,
                selectedIds,
                node => node.Kind == WorkflowNodeKind.RequestEntity,
                RankRequestNode);
        }

        AddBestPath(
            anchor.Id,
            componentIds,
            nodeMap,
            adjacency,
            selectedIds,
            node => node.Kind is WorkflowNodeKind.BackgroundService or WorkflowNodeKind.HostedService,
            RankSupportNode);

        AddBestPath(
            anchor.Id,
            componentIds,
            nodeMap,
            adjacency,
            selectedIds,
            node => node.Kind is WorkflowNodeKind.Controller or WorkflowNodeKind.Endpoint or WorkflowNodeKind.ExternalClient,
            RankSupportNode);

        AddBestPath(
            anchor.Id,
            componentIds,
            nodeMap,
            adjacency,
            selectedIds,
            node => node.Kind == WorkflowNodeKind.ExternalClient,
            RankSupportNode);

        ExpandSupportingNeighbors(anchor.Id, componentIds, nodeMap, metadataMap, adjacency, selectedIds);

        var nodes = selectedIds.Select(id => nodeMap[id]).ToList();
        if (!IsWorkflowCandidate(nodes))
        {
            return null;
        }

        var edges = componentEdges
            .Where(edge => selectedIds.Contains(edge.FromId) && selectedIds.Contains(edge.ToId))
            .ToList();

        return BuildCandidate(nodes, edges, metadataMap, anchor);
    }

    private static WorkflowTopicCandidate? TryBuildWcsRequestExecutorCandidate(
        WorkflowGraphNode anchor,
        IReadOnlySet<string> componentIds,
        IReadOnlyCollection<WorkflowGraphEdge> componentEdges,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        RepositoryWorkflowProfile? activeProfile)
    {
        var selectedIds = new HashSet<string>(StringComparer.Ordinal) { anchor.Id };

        AddDirectRequestNodes(anchor.Id, componentIds, nodeMap, adjacency, selectedIds);
        AddBestPath(anchor.Id, componentIds, nodeMap, adjacency, selectedIds, node => IsWcsRequestRecordNode(node, activeProfile), RankRequestNode);
        AddBestPath(anchor.Id, componentIds, nodeMap, adjacency, selectedIds, node => IsWcsSchedulerNode(node, activeProfile), RankSupportNode);
        AddBestPath(anchor.Id, componentIds, nodeMap, adjacency, selectedIds, node => IsWcsRequestRepositoryNode(node, activeProfile), RankSupportNode);
        AddBestPath(anchor.Id, componentIds, nodeMap, adjacency, selectedIds, node => IsWcsRequestServiceNode(node, activeProfile), RankSupportNode);
        AddBestPath(anchor.Id, componentIds, nodeMap, adjacency, selectedIds, node => IsPrimaryWcsTriggerNode(node, activeProfile), RankSupportNode);

        var compensationTriggerNodes = componentIds
            .Select(id => nodeMap[id])
            .Where(node => IsCompensationWcsTriggerNode(node, activeProfile))
            .OrderBy(node => node.DisplayName, StringComparer.Ordinal)
            .ToList();

        foreach (var compensationTrigger in compensationTriggerNodes)
        {
            selectedIds.Add(compensationTrigger.Id);
        }

        var nodes = selectedIds.Select(id => nodeMap[id]).ToList();
        var primaryTriggerNodes = nodes.Where(node => IsPrimaryWcsTriggerNode(node, activeProfile)).ToList();
        var requestNodes = nodes.Where(node => IsWcsRequestRecordNode(node, activeProfile) || node.Kind == WorkflowNodeKind.RequestEntity).ToList();
        var schedulerNodes = nodes.Where(node => IsWcsSchedulerNode(node, activeProfile)).ToList();

        if (primaryTriggerNodes.Count == 0 || requestNodes.Count == 0 || schedulerNodes.Count == 0)
        {
            return null;
        }

        var edges = componentEdges
            .Where(edge => selectedIds.Contains(edge.FromId) && selectedIds.Contains(edge.ToId))
            .ToList();
        var serviceNodes = nodes.Where(node => node.Kind is WorkflowNodeKind.Service or WorkflowNodeKind.Repository or WorkflowNodeKind.DbContext).ToList();
        var externalNodes = nodes.Where(node => node.Kind == WorkflowNodeKind.ExternalClient).ToList();
        var anchorMetadata = metadataMap.TryGetValue(anchor.Id, out var metadata)
            ? metadata
            : NodeMetadata.Empty;
        var useProfileIdentity = activeProfile is { AnchorNames.Count: > 0 };
        var key = useProfileIdentity ? activeProfile!.Key : ChooseWcsCandidateKey(anchor, anchorMetadata);
        var name = useProfileIdentity ? activeProfile!.Name : ChooseWcsCandidateName(anchor, anchorMetadata);
        var summary = useProfileIdentity && !string.IsNullOrWhiteSpace(activeProfile!.Description)
            ? activeProfile.Description!
            : $"{name}相关业务流程";

        return new WorkflowTopicCandidate
        {
            Key = key,
            Name = name,
            Summary = summary,
            Score = CalculateScore(primaryTriggerNodes, requestNodes, schedulerNodes, [anchor], externalNodes, edges)
                    + compensationTriggerNodes.Count * 0.25d,
            Actors = primaryTriggerNodes
                .Concat(compensationTriggerNodes)
                .Concat(schedulerNodes)
                .Append(anchor)
                .Select(node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            TriggerPoints = primaryTriggerNodes
                .Select(node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CompensationTriggerPoints = compensationTriggerNodes
                .Select(node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            RequestEntities = requestNodes
                .Select(node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            SchedulerFiles = schedulerNodes
                .Select(node => node.FilePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ExecutorFiles =
            [
                anchor.FilePath
            ],
            ServiceFiles = serviceNodes
                .Select(node => node.FilePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            EvidenceFiles = nodes
                .Select(node => node.FilePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            SeedQueries = BuildWcsSeedQueries(
                requestNodes,
                schedulerNodes,
                serviceNodes,
                anchor,
                anchorMetadata,
                primaryTriggerNodes,
                compensationTriggerNodes),
            ExternalSystems = externalNodes
                .Select(node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            StateFields = ExtractStateFields(edges),
            DocumentPreferences = useProfileIdentity
                ? CloneDocumentPreferences(activeProfile!.DocumentPreferences)
                : new WorkflowDocumentPreferences()
        };
    }

    private static void AddDirectRequestNodes(
        string anchorId,
        IReadOnlySet<string> componentIds,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        ISet<string> selectedIds)
    {
        if (!adjacency.TryGetValue(anchorId, out var neighbors))
        {
            return;
        }

        foreach (var requestNode in neighbors
                     .Where(neighbor => componentIds.Contains(neighbor.NodeId) &&
                                        nodeMap.TryGetValue(neighbor.NodeId, out var node) &&
                                        node.Kind == WorkflowNodeKind.RequestEntity)
                     .Select(neighbor => nodeMap[neighbor.NodeId])
                     .OrderByDescending(RankRequestNode)
                     .ThenBy(node => node.DisplayName, StringComparer.Ordinal)
                     .Take(3))
        {
            selectedIds.Add(requestNode.Id);
        }
    }

    private static void AddBestPath(
        string startId,
        IReadOnlySet<string> componentIds,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        ISet<string> selectedIds,
        Func<WorkflowGraphNode, bool> predicate,
        Func<WorkflowGraphNode, int> ranking)
    {
        var path = FindBestPath(startId, componentIds, nodeMap, adjacency, predicate, ranking);
        if (path is null)
        {
            return;
        }

        foreach (var nodeId in path)
        {
            selectedIds.Add(nodeId);
        }
    }

    private static void ExpandSupportingNeighbors(
        string anchorId,
        IReadOnlySet<string> componentIds,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        ISet<string> selectedIds)
    {
        if (!adjacency.TryGetValue(anchorId, out var neighbors))
        {
            return;
        }

        foreach (var neighbor in neighbors)
        {
            if (!componentIds.Contains(neighbor.NodeId) || selectedIds.Contains(neighbor.NodeId))
            {
                continue;
            }

            var neighborNode = nodeMap[neighbor.NodeId];
            if (!ShouldIncludeSupportNode(anchorId, neighborNode, metadataMap))
            {
                continue;
            }

            selectedIds.Add(neighbor.NodeId);
        }
    }

    private static bool ShouldIncludeSupportNode(
        string anchorId,
        WorkflowGraphNode node,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap)
    {
        if (node.Id == anchorId)
        {
            return true;
        }

        if (node.Kind is WorkflowNodeKind.Executor or WorkflowNodeKind.Handler)
        {
            return !IsConcreteExecutionAnchor(node, metadataMap);
        }

        return node.Kind is WorkflowNodeKind.Controller
            or WorkflowNodeKind.Endpoint
            or WorkflowNodeKind.ExternalClient
            or WorkflowNodeKind.BackgroundService
            or WorkflowNodeKind.HostedService
            or WorkflowNodeKind.RequestEntity
            or WorkflowNodeKind.ExecutorFactory
            or WorkflowNodeKind.Service
            or WorkflowNodeKind.Repository
            or WorkflowNodeKind.DbContext
            or WorkflowNodeKind.StatusEnum;
    }

    private static bool IsProfileEligibleAnchor(
        WorkflowGraphNode node,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        RepositoryWorkflowProfile activeProfile)
    {
        return activeProfile.Mode switch
        {
            RepositoryWorkflowProfileMode.WcsRequestExecutor => IsWcsRequestExecutorAnchor(node, metadataMap, activeProfile),
            _ => false
        };
    }

    private static bool IsWcsRequestExecutorAnchor(
        WorkflowGraphNode node,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        RepositoryWorkflowProfile? activeProfile = null)
    {
        if (node.Kind is not WorkflowNodeKind.Executor and not WorkflowNodeKind.Handler)
        {
            return false;
        }

        var nameMatched = activeProfile?.AnchorNames is not { Count: > 0 } ||
                          activeProfile.AnchorNames.Any(name =>
                              string.Equals(node.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                              MetadataContainsServiceKey(metadataMap, node.Id, name));
        if (!nameMatched)
        {
            return false;
        }

        return MatchesDirectoriesOrFallback(
            node.FilePath,
            activeProfile?.AnchorDirectories,
            "WcsRequestExecutors");
    }

    private static bool IsWcsRequestRecordNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return node.Kind == WorkflowNodeKind.RequestEntity &&
               MatchesDisplayNameOrFallback(
                   node.DisplayName,
                   activeProfile?.RequestEntityNames,
                   "WcsRequest");
    }

    private static bool IsWcsSchedulerNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return (node.Kind is WorkflowNodeKind.BackgroundService or WorkflowNodeKind.HostedService) &&
               MatchesProfileNode(
                   node,
                   activeProfile?.SchedulerNames,
                   activeProfile?.SchedulerDirectories,
                   "WcsRequestWmsExecutorJob",
                   null);
    }

    private static bool IsWcsRequestServiceNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return node.Kind == WorkflowNodeKind.Service &&
               MatchesProfileNode(
                   node,
                   activeProfile?.RequestServiceNames,
                   activeProfile?.ServiceDirectories,
                   "WcsRequestService",
                   null,
                   "IWcsRequestService");
    }

    private static bool IsWcsRequestRepositoryNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return node.Kind == WorkflowNodeKind.Repository &&
               MatchesProfileNode(
                   node,
                   activeProfile?.RequestRepositoryNames,
                   activeProfile?.RepositoryDirectories,
                   "WcsRequestRepository",
                   null,
                   "IWcsRequestRepository");
    }

    private static bool IsPrimaryWcsTriggerNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return (node.Kind is WorkflowNodeKind.Controller or WorkflowNodeKind.Endpoint or WorkflowNodeKind.ExternalClient) &&
               MatchesProfileNode(
                   node,
                   activeProfile?.PrimaryTriggerNames,
                   activeProfile?.PrimaryTriggerDirectories,
                   "WmsJobInterfaceController",
                   null);
    }

    private static bool IsCompensationWcsTriggerNode(WorkflowGraphNode node, RepositoryWorkflowProfile? activeProfile = null)
    {
        return node.Kind == WorkflowNodeKind.Controller &&
               MatchesProfileNode(
                   node,
                   activeProfile?.CompensationTriggerNames,
                   activeProfile?.CompensationTriggerDirectories,
                   "LogExternalInterfaceController",
                   null);
    }

    private static List<string>? FindBestPath(
        string startId,
        IReadOnlySet<string> allowedIds,
        IReadOnlyDictionary<string, WorkflowGraphNode> nodeMap,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        Func<WorkflowGraphNode, bool> predicate,
        Func<WorkflowGraphNode, int> ranking)
    {
        var queue = new Queue<string>();
        var distance = new Dictionary<string, int>(StringComparer.Ordinal) { [startId] = 0 };
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal) { [startId] = null };
        var matches = new List<string>();
        int? bestDistance = null;

        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentDistance = distance[currentId];

            if (bestDistance.HasValue && currentDistance > bestDistance.Value)
            {
                break;
            }

            if (!string.Equals(currentId, startId, StringComparison.Ordinal) &&
                nodeMap.TryGetValue(currentId, out var currentNode) &&
                predicate(currentNode))
            {
                bestDistance = currentDistance;
                matches.Add(currentId);
                continue;
            }

            if (!adjacency.TryGetValue(currentId, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors.OrderBy(item => nodeMap[item.NodeId].DisplayName, StringComparer.Ordinal))
            {
                if (!allowedIds.Contains(neighbor.NodeId) || distance.ContainsKey(neighbor.NodeId))
                {
                    continue;
                }

                distance[neighbor.NodeId] = currentDistance + 1;
                previous[neighbor.NodeId] = currentId;
                queue.Enqueue(neighbor.NodeId);
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        var bestTargetId = matches
            .OrderByDescending(id => ranking(nodeMap[id]))
            .ThenBy(id => nodeMap[id].DisplayName, StringComparer.Ordinal)
            .First();

        return ReconstructPath(bestTargetId, previous);
    }

    private static List<string> ReconstructPath(string targetId, IReadOnlyDictionary<string, string?> previous)
    {
        var path = new List<string>();
        for (var currentId = targetId; currentId is not null; currentId = previous[currentId]!)
        {
            path.Add(currentId);
            if (previous[currentId] is null)
            {
                break;
            }
        }

        path.Reverse();
        return path;
    }

    private static Dictionary<string, List<WorkflowGraphNeighbor>> BuildAdjacency(WorkflowSemanticGraph graph)
    {
        var adjacency = new Dictionary<string, List<WorkflowGraphNeighbor>>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            adjacency[node.Id] = [];
        }

        foreach (var edge in graph.Edges)
        {
            if (!adjacency.TryGetValue(edge.FromId, out var fromNeighbors) ||
                !adjacency.TryGetValue(edge.ToId, out var toNeighbors))
            {
                continue;
            }

            fromNeighbors.Add(new WorkflowGraphNeighbor(edge.ToId, edge));
            toNeighbors.Add(new WorkflowGraphNeighbor(edge.FromId, edge));
        }

        return adjacency;
    }

    private static HashSet<string> TraverseComponent(
        string startId,
        IReadOnlyDictionary<string, List<WorkflowGraphNeighbor>> adjacency,
        ISet<string> visited)
    {
        var component = new HashSet<string>(StringComparer.Ordinal) { startId };
        var queue = new Queue<string>();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (!component.Add(neighbor.NodeId))
                {
                    continue;
                }

                visited.Add(neighbor.NodeId);
                queue.Enqueue(neighbor.NodeId);
            }
        }

        return component;
    }

    private static bool IsWorkflowCandidate(IReadOnlyCollection<WorkflowGraphNode> nodes)
    {
        var hasTrigger = nodes.Any(node => node.Kind is WorkflowNodeKind.Controller or WorkflowNodeKind.Endpoint or WorkflowNodeKind.ExternalClient);
        var hasRequest = nodes.Any(node => node.Kind == WorkflowNodeKind.RequestEntity);
        var hasScheduler = nodes.Any(node => node.Kind is WorkflowNodeKind.BackgroundService or WorkflowNodeKind.HostedService);
        var hasExecutor = nodes.Any(node => node.Kind is WorkflowNodeKind.Executor or WorkflowNodeKind.Handler);

        return hasTrigger && hasRequest && hasScheduler && hasExecutor;
    }

    private static WorkflowTopicCandidate BuildCandidate(
        List<WorkflowGraphNode> nodes,
        List<WorkflowGraphEdge> edges,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        WorkflowGraphNode? preferredExecutor = null)
    {
        var triggerNodes = nodes.Where(node => node.Kind is WorkflowNodeKind.Controller or WorkflowNodeKind.Endpoint or WorkflowNodeKind.ExternalClient).ToList();
        var requestNodes = nodes.Where(node => node.Kind == WorkflowNodeKind.RequestEntity).ToList();
        var schedulerNodes = nodes.Where(node => node.Kind is WorkflowNodeKind.BackgroundService or WorkflowNodeKind.HostedService).ToList();
        var executorNodes = nodes.Where(node => node.Kind is WorkflowNodeKind.Executor or WorkflowNodeKind.Handler).ToList();
        var concreteExecutorNodes = executorNodes.Where(node => IsConcreteExecutionAnchor(node, metadataMap)).ToList();
        var serviceNodes = nodes.Where(node => node.Kind is WorkflowNodeKind.Service or WorkflowNodeKind.Repository).ToList();
        var externalNodes = nodes.Where(node => node.Kind == WorkflowNodeKind.ExternalClient).ToList();

        var primaryExecutors = preferredExecutor is not null
            ? [preferredExecutor]
            : concreteExecutorNodes.Count > 0
                ? concreteExecutorNodes
                : executorNodes;

        var nameSeed = ChooseNameSeed(requestNodes, primaryExecutors, triggerNodes, serviceNodes);
        var name = SplitPascalCase(nameSeed);

        return new WorkflowTopicCandidate
        {
            Key = ToSlug(nameSeed),
            Name = name,
            Summary = $"{name} workflow with {nodes.Count} related nodes",
            Score = CalculateScore(triggerNodes, requestNodes, schedulerNodes, primaryExecutors, externalNodes, edges),
            Actors = triggerNodes.Concat(schedulerNodes).Concat(primaryExecutors).Select(node => node.DisplayName).Distinct(StringComparer.Ordinal).ToList(),
            TriggerPoints = triggerNodes.Select(node => node.DisplayName).Distinct(StringComparer.Ordinal).ToList(),
            RequestEntities = requestNodes.Select(node => node.DisplayName).Distinct(StringComparer.Ordinal).ToList(),
            SchedulerFiles = schedulerNodes.Select(node => node.FilePath).Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToList(),
            ExecutorFiles = primaryExecutors.Select(node => node.FilePath).Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToList(),
            ServiceFiles = serviceNodes.Select(node => node.FilePath).Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToList(),
            EvidenceFiles = nodes.Select(node => node.FilePath).Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToList(),
            SeedQueries = requestNodes.Concat(schedulerNodes).Concat(primaryExecutors).Select(node => node.DisplayName).Distinct(StringComparer.Ordinal).ToList(),
            ExternalSystems = externalNodes.Select(node => node.DisplayName).Distinct(StringComparer.Ordinal).ToList(),
            StateFields = ExtractStateFields(edges)
        };
    }

    private static string ChooseNameSeed(
        IReadOnlyCollection<WorkflowGraphNode> requestNodes,
        IReadOnlyCollection<WorkflowGraphNode> executorNodes,
        IReadOnlyCollection<WorkflowGraphNode> triggerNodes,
        IReadOnlyCollection<WorkflowGraphNode> serviceNodes)
    {
        var candidates = requestNodes
            .OrderByDescending(RankRequestNode)
            .Select(node => NormalizeNameSeed(node.DisplayName))
            .Concat(executorNodes
                .OrderByDescending(node => RankNameSeed(node.DisplayName))
                .Select(node => NormalizeNameSeed(node.DisplayName)))
            .Concat(triggerNodes.Select(node => NormalizeNameSeed(node.DisplayName)))
            .Concat(serviceNodes.Select(node => NormalizeNameSeed(node.DisplayName)))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        return candidates.FirstOrDefault(static item => !IsGenericNameSeed(item))
               ?? candidates.FirstOrDefault()
               ?? "Workflow";
    }

    private static string? NormalizeNameSeed(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        return rawName
            .Replace("Controller", string.Empty, StringComparison.Ordinal)
            .Replace("RequestScanWorker", string.Empty, StringComparison.Ordinal)
            .Replace("Worker", string.Empty, StringComparison.Ordinal)
            .Replace("Request", string.Empty, StringComparison.Ordinal)
            .Replace("Executor", string.Empty, StringComparison.Ordinal)
            .Replace("Service", string.Empty, StringComparison.Ordinal)
            .Replace("Handler", string.Empty, StringComparison.Ordinal)
            .Replace("Body", string.Empty, StringComparison.Ordinal)
            .Replace("Job", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static bool IsGenericNameSeed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Length <= 3 ||
               value.Equals("Wcs", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Wms", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Workflow", StringComparison.OrdinalIgnoreCase);
    }

    private static int RankRequestNode(WorkflowGraphNode node)
    {
        var score = RankNameSeed(node.DisplayName);
        if (node.DisplayName.Contains("Body", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (node.DisplayName.Contains("Request", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static int RankSupportNode(WorkflowGraphNode node)
    {
        return node.Kind switch
        {
            WorkflowNodeKind.Controller => 8,
            WorkflowNodeKind.Endpoint => 7,
            WorkflowNodeKind.ExternalClient => 6,
            WorkflowNodeKind.HostedService => 5,
            WorkflowNodeKind.BackgroundService => 5,
            WorkflowNodeKind.RequestEntity => RankRequestNode(node),
            _ => RankNameSeed(node.DisplayName)
        };
    }

    private static int RankNameSeed(string value)
    {
        var normalized = NormalizeNameSeed(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return int.MinValue;
        }

        var score = normalized.Length;
        if (value.Contains("Executor", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        if (value.Contains("Body", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (value.Contains("Request", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (IsGenericNameSeed(normalized))
        {
            score -= 100;
        }

        return score;
    }

    private static bool IsConcreteExecutionAnchor(
        WorkflowGraphNode node,
        IReadOnlyDictionary<string, NodeMetadata> metadataMap)
    {
        if (node.Kind is not WorkflowNodeKind.Executor and not WorkflowNodeKind.Handler)
        {
            return false;
        }

        var metadata = metadataMap.TryGetValue(node.Id, out var resolvedMetadata)
            ? resolvedMetadata
            : NodeMetadata.Empty;
        return !metadata.IsAbstract && !metadata.IsInterface;
    }

    private static string ChooseWcsCandidateKey(WorkflowGraphNode anchor, NodeMetadata metadata)
    {
        foreach (var serviceKey in metadata.ServiceKeys)
        {
            var slug = ToServiceKeySlug(serviceKey);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return slug;
            }
        }

        var fallbackSeed = NormalizeNameSeed(anchor.DisplayName) ?? anchor.DisplayName;
        return ToSlug(fallbackSeed);
    }

    private static string ChooseWcsCandidateName(WorkflowGraphNode anchor, NodeMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.DocumentationSummary))
        {
            return metadata.DocumentationSummary!;
        }

        var normalizedName = NormalizeNameSeed(anchor.DisplayName) ?? anchor.DisplayName;
        return SplitPascalCase(normalizedName);
    }

    private static List<string> BuildWcsSeedQueries(
        IReadOnlyCollection<WorkflowGraphNode> requestNodes,
        IReadOnlyCollection<WorkflowGraphNode> schedulerNodes,
        IReadOnlyCollection<WorkflowGraphNode> serviceNodes,
        WorkflowGraphNode anchor,
        NodeMetadata metadata,
        IReadOnlyCollection<WorkflowGraphNode> primaryTriggerNodes,
        IReadOnlyCollection<WorkflowGraphNode> compensationTriggerNodes)
    {
        return requestNodes
            .Concat(schedulerNodes)
            .Concat(serviceNodes)
            .Concat(primaryTriggerNodes)
            .Concat(compensationTriggerNodes)
            .Select(node => node.DisplayName)
            .Append(anchor.DisplayName)
            .Concat(metadata.ServiceKeys)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static double CalculateScore(
        IReadOnlyCollection<WorkflowGraphNode> triggerNodes,
        IReadOnlyCollection<WorkflowGraphNode> requestNodes,
        IReadOnlyCollection<WorkflowGraphNode> schedulerNodes,
        IReadOnlyCollection<WorkflowGraphNode> executorNodes,
        IReadOnlyCollection<WorkflowGraphNode> externalNodes,
        IReadOnlyCollection<WorkflowGraphEdge> edges)
    {
        var score = 0d;
        score += triggerNodes.Count * 1.0d;
        score += requestNodes.Count * 1.5d;
        score += schedulerNodes.Count * 1.25d;
        score += executorNodes.Count * 1.25d;
        score += externalNodes.Count * 0.75d;

        if (edges.Any(edge => edge.Kind == WorkflowEdgeKind.Dispatches))
        {
            score += 0.5d;
        }

        if (edges.Any(edge => edge.Kind == WorkflowEdgeKind.UpdatesStatus))
        {
            score += 0.5d;
        }

        if (edges.Any(edge => edge.Kind == WorkflowEdgeKind.ConsumesEntity))
        {
            score += 0.5d;
        }

        score += edges.Count(edge => edge.Kind is WorkflowEdgeKind.Writes or WorkflowEdgeKind.Queries) * 0.25d;
        return score;
    }

    private static List<string> ExtractStateFields(IEnumerable<WorkflowGraphEdge> edges)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges.Where(edge => edge.Kind == WorkflowEdgeKind.UpdatesStatus))
        {
            if (string.IsNullOrWhiteSpace(edge.EvidenceJson))
            {
                continue;
            }

            using var document = JsonDocument.Parse(edge.EvidenceJson);
            if (document.RootElement.TryGetProperty("member", out var member))
            {
                var value = member.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fields.Add(value);
                }
            }
        }

        return fields.ToList();
    }

    private static NodeMetadata GetNodeMetadata(WorkflowGraphNode node)
    {
        if (string.IsNullOrWhiteSpace(node.MetadataJson))
        {
            return NodeMetadata.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(node.MetadataJson);
            var root = document.RootElement;

            return new NodeMetadata(
                root.TryGetProperty("typeKind", out var typeKind) ? typeKind.GetString() : null,
                root.TryGetProperty("isAbstract", out var isAbstract) && isAbstract.ValueKind == JsonValueKind.True,
                root.TryGetProperty("isInterface", out var isInterface) && isInterface.ValueKind == JsonValueKind.True,
                root.TryGetProperty("documentationSummary", out var documentationSummary) ? documentationSummary.GetString() : null,
                root.TryGetProperty("serviceKeys", out var serviceKeys) && serviceKeys.ValueKind == JsonValueKind.Array
                    ? serviceKeys.EnumerateArray()
                        .Select(item => item.GetString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Select(static item => item!)
                        .ToList()
                    : []);
        }
        catch (JsonException)
        {
            return NodeMetadata.Empty;
        }
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var buffer = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(value[index - 1]) ||
                 (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                buffer.Add(' ');
            }

            buffer.Add(current);
        }

        return new string(buffer.ToArray()).Trim();
    }

    private static string ToSlug(string value)
    {
        return string.Join(
            "-",
            SplitPascalCase(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant()));
    }

    private static string ToServiceKeySlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var segments = value
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (segments.Count > 1 &&
            segments[^1].Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return string.Join(
            "-",
            segments
                .Select(ToSlug)
                .Where(static segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static bool MatchesProfileNode(
        WorkflowGraphNode node,
        IReadOnlyCollection<string>? configuredNames,
        IReadOnlyCollection<string>? configuredDirectories,
        string fallbackName,
        string? fallbackDirectory,
        params string[] additionalFallbackNames)
    {
        if (!MatchesDisplayNameOrFallback(node.DisplayName, configuredNames, fallbackName, additionalFallbackNames))
        {
            return false;
        }

        return MatchesDirectoriesOrFallback(node.FilePath, configuredDirectories, fallbackDirectory);
    }

    private static bool MatchesDisplayNameOrFallback(
        string displayName,
        IReadOnlyCollection<string>? configuredNames,
        string fallbackName,
        params string[] additionalFallbackNames)
    {
        if (configuredNames is { Count: > 0 })
        {
            return configuredNames.Any(name => string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(displayName, fallbackName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return additionalFallbackNames.Any(name => string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesDirectoriesOrFallback(
        string? filePath,
        IReadOnlyCollection<string>? configuredDirectories,
        string? fallbackDirectory)
    {
        if (configuredDirectories is { Count: > 0 })
        {
            return configuredDirectories.Any(directory => PathContainsDirectoryPrefix(filePath, directory));
        }

        return string.IsNullOrWhiteSpace(fallbackDirectory) || PathContainsSegment(filePath, fallbackDirectory);
    }

    private static bool MetadataContainsServiceKey(
        IReadOnlyDictionary<string, NodeMetadata> metadataMap,
        string nodeId,
        string value)
    {
        return metadataMap.TryGetValue(nodeId, out var metadata) &&
               metadata.ServiceKeys.Any(serviceKey => string.Equals(serviceKey, value, StringComparison.OrdinalIgnoreCase));
    }

    private static WorkflowDocumentPreferences CloneDocumentPreferences(WorkflowDocumentPreferences? preferences)
    {
        preferences ??= new WorkflowDocumentPreferences();
        return new WorkflowDocumentPreferences
        {
            WritingHint = preferences.WritingHint,
            PreferredTerms = [.. preferences.PreferredTerms],
            RequiredSections = [.. preferences.RequiredSections],
            AvoidPrimaryTriggerNames = [.. preferences.AvoidPrimaryTriggerNames]
        };
    }

    private static bool PathContainsSegment(string? filePath, string segment)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = $"/{filePath.Replace('\\', '/').Trim('/')}/";
        var normalizedSegment = $"/{segment.Replace('\\', '/').Trim('/')}/";
        return normalizedPath.Contains(normalizedSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathContainsDirectoryPrefix(string? filePath, string directory)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var normalizedPath = filePath.Replace('\\', '/').Trim('/');
        var normalizedDirectory = directory.Replace('\\', '/').Trim('/');
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct WorkflowGraphNeighbor(string NodeId, WorkflowGraphEdge Edge);

    private readonly record struct NodeMetadata(
        string? TypeKind,
        bool IsAbstract,
        bool IsInterface,
        string? DocumentationSummary,
        IReadOnlyList<string> ServiceKeys)
    {
        public static NodeMetadata Empty { get; } = new(null, false, false, null, []);
    }
}

public sealed class WorkflowTopicCandidate
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public double Score { get; init; }

    public List<string> Actors { get; init; } = [];

    public List<string> TriggerPoints { get; init; } = [];

    public List<string> CompensationTriggerPoints { get; init; } = [];

    public List<string> RequestEntities { get; init; } = [];

    public List<string> SchedulerFiles { get; init; } = [];

    public List<string> ExecutorFiles { get; init; } = [];

    public List<string> ServiceFiles { get; init; } = [];

    public List<string> EvidenceFiles { get; init; } = [];

    public List<string> SeedQueries { get; init; } = [];

    public List<string> ExternalSystems { get; init; } = [];

    public List<string> StateFields { get; init; } = [];

    public WorkflowDocumentPreferences DocumentPreferences { get; init; } = new();
}
