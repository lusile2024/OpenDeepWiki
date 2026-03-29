using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowAnalysisWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int MaxConcurrentSessions = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow analysis worker started.");
        var runningExecutions = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                runningExecutions.RemoveAll(task => task.IsCompleted);

                while (runningExecutions.Count < MaxConcurrentSessions)
                {
                    using var scope = scopeFactory.CreateScope();
                    var queueService = scope.ServiceProvider.GetRequiredService<IWorkflowAnalysisQueueService>();
                    var lease = await queueService.TryAcquireNextAsync(stoppingToken);
                    if (lease is null)
                    {
                        break;
                    }

                    runningExecutions.Add(ProcessSessionAsync(lease.AnalysisSessionId, stoppingToken));
                }

                if (runningExecutions.Count == 0)
                {
                    await Task.Delay(PollingInterval, stoppingToken);
                    continue;
                }

                var delayTask = Task.Delay(PollingInterval, stoppingToken);
                var waitList = runningExecutions.Cast<Task>().Append(delayTask).ToArray();
                await Task.WhenAny(waitList);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow analysis worker loop failed unexpectedly.");
                await Task.Delay(PollingInterval, stoppingToken);
            }
        }

        await Task.WhenAll(runningExecutions);
        logger.LogInformation("Workflow analysis worker stopped.");
    }

    private async Task ProcessSessionAsync(string analysisSessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<IWorkflowAnalysisExecutionService>();
            await executionService.ExecuteAsync(analysisSessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow analysis session {AnalysisSessionId} failed unexpectedly.", analysisSessionId);
        }
    }
}
