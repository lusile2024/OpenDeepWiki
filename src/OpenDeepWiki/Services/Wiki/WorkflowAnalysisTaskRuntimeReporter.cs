using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisTaskRuntimeReporter
{
    Task ReportLogAsync(
        string analysisSessionId,
        string? taskId,
        string level,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowAnalysisTaskRuntimeReporter : IWorkflowAnalysisTaskRuntimeReporter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WorkflowAnalysisTaskRuntimeReporter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task ReportLogAsync(
        string analysisSessionId,
        string? taskId,
        string level,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(analysisSessionId) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var session = await context.WorkflowAnalysisSessions
            .FirstOrDefaultAsync(
                item => item.Id == analysisSessionId && !item.IsDeleted,
                cancellationToken);
        if (session is null)
        {
            return;
        }

        context.WorkflowAnalysisLogs.Add(new WorkflowAnalysisLog
        {
            Id = Guid.NewGuid().ToString(),
            AnalysisSessionId = analysisSessionId,
            TaskId = taskId,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim(),
            Message = message.Trim()
        });

        session.LastActivityAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
