using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisQueueService : IWorkflowAnalysisQueueService
{
    private readonly IContext _context;
    private readonly ILogger<WorkflowAnalysisQueueService> _logger;

    public WorkflowAnalysisQueueService(
        IContext context,
        ILogger<WorkflowAnalysisQueueService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> EnqueueAsync(string analysisSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisSessionId);

        var session = await _context.WorkflowAnalysisSessions
            .FirstOrDefaultAsync(item => item.Id == analysisSessionId && !item.IsDeleted, cancellationToken);
        if (session is null)
        {
            return false;
        }

        if (string.Equals(session.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        session.Status = "Queued";
        session.QueuedAt = session.QueuedAt ?? DateTime.UtcNow;
        session.LastActivityAt = DateTime.UtcNow;
        session.ProgressMessage = "等待后台 worker 执行";
        session.CurrentTaskId = null;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WorkflowAnalysisQueueLease?> TryAcquireNextAsync(CancellationToken cancellationToken = default)
    {
        var session = await _context.WorkflowAnalysisSessions
            .OrderBy(item => item.QueuedAt ?? item.CreatedAt)
            .ThenBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(
                item => item.Status == "Queued" && !item.IsDeleted,
                cancellationToken);
        if (session is null)
        {
            return null;
        }

        session.Status = "Running";
        session.StartedAt ??= DateTime.UtcNow;
        session.LastActivityAt = DateTime.UtcNow;
        session.ProgressMessage = "开始执行分析";
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Acquired workflow analysis session {AnalysisSessionId} for execution.",
            session.Id);

        return new WorkflowAnalysisQueueLease
        {
            AnalysisSessionId = session.Id,
            RepositoryId = session.RepositoryId,
            WorkflowTemplateSessionId = session.WorkflowTemplateSessionId,
            ProfileKey = session.ProfileKey,
            ChapterKey = session.ChapterKey
        };
    }
}
