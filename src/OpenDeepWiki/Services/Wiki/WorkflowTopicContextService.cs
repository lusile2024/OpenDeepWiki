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

        return record is null
            ? null
            : JsonSerializer.Deserialize<WorkflowTopicCandidate>(record.ContextJson);
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
}

public sealed class WorkflowTopicContextItem
{
    public string CatalogPath { get; init; } = string.Empty;

    public WorkflowTopicCandidate Candidate { get; init; } = new();
}
