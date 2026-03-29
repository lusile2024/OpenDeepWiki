using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowTopicContextService
{
    private const string WorkflowTopicKind = "workflow";
    private readonly IContextFactory _contextFactory;

    public WorkflowTopicContextService(IContextFactory contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task UpsertWorkflowContextsAsync(
        string branchLanguageId,
        IEnumerable<WorkflowTopicContextItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchLanguageId);
        ArgumentNullException.ThrowIfNull(items);

        using var context = _contextFactory.CreateContext();

        foreach (var item in items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.CatalogPath);
            ArgumentNullException.ThrowIfNull(item.Candidate);

            var existing = await context.DocTopicContexts
                .FirstOrDefaultAsync(
                    topic => topic.BranchLanguageId == branchLanguageId &&
                             topic.CatalogPath == item.CatalogPath,
                    cancellationToken);

            var serializedCandidate = JsonSerializer.Serialize(item.Candidate);
            if (existing is null)
            {
                context.DocTopicContexts.Add(new DocTopicContext
                {
                    Id = Guid.NewGuid().ToString(),
                    BranchLanguageId = branchLanguageId,
                    CatalogPath = item.CatalogPath,
                    TopicKind = WorkflowTopicKind,
                    ContextJson = serializedCandidate
                });
                continue;
            }

            existing.TopicKind = WorkflowTopicKind;
            existing.ContextJson = serializedCandidate;
            existing.UpdateTimestamp();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowTopicCandidate?> GetWorkflowCandidateAsync(
        string branchLanguageId,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchLanguageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        using var context = _contextFactory.CreateContext();

        var record = await context.DocTopicContexts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                topic => topic.BranchLanguageId == branchLanguageId &&
                         topic.CatalogPath == catalogPath &&
                         topic.TopicKind == WorkflowTopicKind,
                cancellationToken);

        if (record is null)
        {
            return null;
        }

        var candidate = JsonSerializer.Deserialize<WorkflowTopicCandidate>(record.ContextJson);
        if (candidate is null)
        {
            return null;
        }

        candidate.DeepAnalysis = await LoadDeepAnalysisSnapshotAsync(
            context,
            branchLanguageId,
            candidate.Key,
            cancellationToken);

        return candidate;
    }

    public async Task ClearBranchAsync(
        string branchLanguageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchLanguageId);

        using var context = _contextFactory.CreateContext();

        var records = await context.DocTopicContexts
            .Where(topic => topic.BranchLanguageId == branchLanguageId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        context.DocTopicContexts.RemoveRange(records);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<WorkflowDeepAnalysisSnapshot?> LoadDeepAnalysisSnapshotAsync(
        IContext context,
        string branchLanguageId,
        string profileKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            return null;
        }

        var branchLanguage = await context.BranchLanguages
            .AsNoTracking()
            .Where(item => item.Id == branchLanguageId && !item.IsDeleted)
            .Select(item => new BranchLanguageLookup(item.RepositoryBranchId, item.LanguageCode))
            .FirstOrDefaultAsync(cancellationToken);

        if (branchLanguage is null)
        {
            return null;
        }

        var sessions = await context.WorkflowAnalysisSessions
            .AsNoTracking()
            .Where(item =>
                item.BranchId == branchLanguage.RepositoryBranchId &&
                item.LanguageCode == branchLanguage.LanguageCode &&
                item.ProfileKey == profileKey &&
                (item.Status == "Completed" ||
                 item.Status == "CompletedWithErrors" ||
                 item.Status == "Composing") &&
                !item.IsDeleted)
            .OrderByDescending(item => item.CompletedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => new WorkflowAnalysisSessionLookup(
                item.Id,
                item.Status,
                item.Objective,
                item.Summary,
                item.ChapterKey,
                item.CompletedAt,
                item.CreatedAt))
            .Take(8)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
        {
            return null;
        }

        var sessionIds = sessions.Select(item => item.Id).ToList();
        var artifacts = await context.WorkflowAnalysisArtifacts
            .AsNoTracking()
            .Where(item => sessionIds.Contains(item.AnalysisSessionId) && !item.IsDeleted)
            .Select(item => new WorkflowAnalysisArtifactLookup(
                item.AnalysisSessionId,
                item.ArtifactType,
                item.Title,
                item.Content,
                item.MetadataJson))
            .ToListAsync(cancellationToken);

        return BuildDeepAnalysisSnapshot(sessions, artifacts);
    }

    private static WorkflowDeepAnalysisSnapshot BuildDeepAnalysisSnapshot(
        IReadOnlyList<WorkflowAnalysisSessionLookup> sessions,
        IReadOnlyList<WorkflowAnalysisArtifactLookup> artifacts)
    {
        var sessionOrder = sessions
            .Select((session, index) => new { session.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        var sessionMap = sessions.ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);

        var orderedArtifacts = artifacts
            .OrderBy(item => sessionOrder.GetValueOrDefault(item.AnalysisSessionId, int.MaxValue))
            .ThenBy(item => item.ArtifactType, StringComparer.Ordinal)
            .ToList();

        var chapterMap = new Dictionary<string, WorkflowDeepAnalysisChapterSeed>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in orderedArtifacts)
        {
            if (!sessionMap.TryGetValue(artifact.AnalysisSessionId, out var session))
            {
                continue;
            }

            var chapterKey = ResolveChapterKey(artifact.MetadataJson, session.ChapterKey);
            if (string.IsNullOrWhiteSpace(chapterKey))
            {
                continue;
            }

            if (!chapterMap.TryGetValue(chapterKey, out var chapter))
            {
                chapter = new WorkflowDeepAnalysisChapterSeed
                {
                    ChapterKey = chapterKey,
                    Title = ResolveChapterTitle(artifact.Title, artifact.ArtifactType, chapterKey)
                };
                chapterMap[chapterKey] = chapter;
            }
            else if (string.IsNullOrWhiteSpace(chapter.Title))
            {
                chapter.Title = ResolveChapterTitle(artifact.Title, artifact.ArtifactType, chapterKey);
            }

            switch (artifact.ArtifactType)
            {
                case "chapter-brief" when string.IsNullOrWhiteSpace(chapter.BriefMarkdown):
                    chapter.BriefMarkdown = artifact.Content;
                    break;
                case "flowchart" when string.IsNullOrWhiteSpace(chapter.FlowchartSeedMermaid):
                    chapter.FlowchartSeedMermaid = artifact.Content;
                    break;
                case "mindmap" when string.IsNullOrWhiteSpace(chapter.MindMapSeedMarkdown):
                    chapter.MindMapSeedMarkdown = artifact.Content;
                    break;
            }
        }

        var latestSession = sessions[0];
        var overviewArtifact = orderedArtifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactType, "analysis-overview", StringComparison.OrdinalIgnoreCase));

        return new WorkflowDeepAnalysisSnapshot
        {
            SessionIds = sessions.Select(item => item.Id).ToList(),
            Status = latestSession.Status,
            Objective = latestSession.Objective,
            Summary = latestSession.Summary,
            OverviewMarkdown = overviewArtifact?.Content,
            LastCompletedAt = latestSession.CompletedAt ?? latestSession.CreatedAt,
            Chapters = chapterMap.Values
                .OrderBy(item => item.ChapterKey, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static string? ResolveChapterKey(string? metadataJson, string? fallbackChapterKey)
    {
        var metadataValue = TryGetMetadataValue(metadataJson, "chapterKey");
        return string.IsNullOrWhiteSpace(metadataValue) ? fallbackChapterKey : metadataValue;
    }

    private static string ResolveChapterTitle(string title, string artifactType, string fallbackChapterKey)
    {
        var normalizedTitle = title ?? string.Empty;
        var suffix = artifactType switch
        {
            "chapter-brief" => " 章节摘要",
            "flowchart" => " 流程图种子",
            "mindmap" => " 脑图种子",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(suffix) &&
            normalizedTitle.EndsWith(suffix, StringComparison.Ordinal))
        {
            normalizedTitle = normalizedTitle[..^suffix.Length].Trim();
        }

        return string.IsNullOrWhiteSpace(normalizedTitle)
            ? fallbackChapterKey
            : normalizedTitle;
    }

    private static string? TryGetMetadataValue(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class WorkflowTopicContextItem
{
    public string CatalogPath { get; init; } = string.Empty;

    public WorkflowTopicCandidate Candidate { get; init; } = new();
}

internal sealed record BranchLanguageLookup(string RepositoryBranchId, string LanguageCode);

internal sealed record WorkflowAnalysisSessionLookup(
    string Id,
    string Status,
    string? Objective,
    string? Summary,
    string? ChapterKey,
    DateTime? CompletedAt,
    DateTime CreatedAt);

internal sealed record WorkflowAnalysisArtifactLookup(
    string AnalysisSessionId,
    string ArtifactType,
    string Title,
    string Content,
    string? MetadataJson);
