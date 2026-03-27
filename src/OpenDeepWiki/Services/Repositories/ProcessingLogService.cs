using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// 处理日志服务实现
/// </summary>
public class ProcessingLogService : IProcessingLogService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProcessingLogService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        string repositoryId,
        ProcessingStep step,
        string message,
        bool isAiOutput = false,
        string? toolName = null,
        CancellationToken cancellationToken = default)
    {
        // 使用独立的 scope 来保存日志，避免影响其他操作
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var log = new RepositoryProcessingLog
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            Step = step,
            Message = message,
            IsAiOutput = isAiOutput,
            ToolName = toolName,
            CreatedAt = DateTime.UtcNow
        };

        context.RepositoryProcessingLogs.Add(log);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProcessingLogResponse> GetLogsAsync(
        string repositoryId,
        DateTime? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var query = context.RepositoryProcessingLogs
            .Where(log => log.RepositoryId == repositoryId);

        if (since.HasValue)
        {
            query = query.Where(log => log.CreatedAt > since.Value);
        }

        var logs = await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(limit)
            .Select(log => new ProcessingLogItem
            {
                Id = log.Id,
                Step = log.Step,
                Message = log.Message,
                IsAiOutput = log.IsAiOutput,
                ToolName = log.ToolName,
                CreatedAt = log.CreatedAt
            })
            .ToListAsync(cancellationToken);

        // 反转列表，使其按时间正序排列（最早的在前）
        logs.Reverse();

        // 获取当前步骤（最新日志的步骤）
        var currentStep = logs.LastOrDefault()?.Step ?? ProcessingStep.Workspace;

        // 解析文档生成进度
        var (totalDocuments, completedDocuments) = ParseDocumentProgress(logs);

        // 获取开始时间（第一条日志的时间）
        var startedAt = logs.FirstOrDefault()?.CreatedAt;

        return new ProcessingLogResponse
        {
            CurrentStep = currentStep,
            Logs = logs,
            TotalDocuments = totalDocuments,
            CompletedDocuments = completedDocuments,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// 从日志中解析文档生成进度
    /// </summary>
    private static (int total, int completed) ParseDocumentProgress(List<ProcessingLogItem> logs)
    {
        int total = 0;
        int completed = 0;

        foreach (var log in logs)
        {
            if (log.Step != ProcessingStep.Content || log.IsAiOutput || !string.IsNullOrEmpty(log.ToolName))
                continue;

            // 匹配 "发现 X 个文档需要生成" 格式
            var totalMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"发现\s*(\d+)\s*个文档");
            if (totalMatch.Success)
            {
                total = int.Parse(totalMatch.Groups[1].Value);
                continue;
            }

            // 匹配 "开始重建业务流程文档，候选数: X" 格式
            var workflowTotalMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"开始重建业务流程文档，候选数:\s*(\d+)");
            if (workflowTotalMatch.Success)
            {
                total = int.Parse(workflowTotalMatch.Groups[1].Value);
                continue;
            }

            // 匹配 "文档完成 (X/Y)" 格式（以完成为准）
            var completedMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"文档完成\s*\((\d+)/(\d+)\)");
            if (completedMatch.Success)
            {
                completed = Math.Max(completed, int.Parse(completedMatch.Groups[1].Value));
                if (total == 0)
                {
                    total = int.Parse(completedMatch.Groups[2].Value);
                }
                continue;
            }

            // 匹配 "业务流程文档完成 (X/Y)" 格式（以完成为准）
            var workflowCompletedMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"业务流程文档完成\s*\((\d+)/(\d+)\)");
            if (workflowCompletedMatch.Success)
            {
                completed = Math.Max(completed, int.Parse(workflowCompletedMatch.Groups[1].Value));
                if (total == 0)
                {
                    total = int.Parse(workflowCompletedMatch.Groups[2].Value);
                }
                continue;
            }

            // 匹配 "开始生成文档 (X/Y)" 或旧格式 "正在生成文档 (X/Y)" - 仅用于补全总数
            var progressMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"(开始生成文档|正在生成文档)\s*\((\d+)/(\d+)\)");
            if (progressMatch.Success)
            {
                if (total == 0)
                {
                    total = int.Parse(progressMatch.Groups[3].Value);
                }
                continue;
            }

            // 匹配 "开始重建业务流程 (X/Y)" - 仅用于补全总数
            var workflowProgressMatch = System.Text.RegularExpressions.Regex.Match(
                log.Message, @"开始重建业务流程\s*\((\d+)/(\d+)\)");
            if (workflowProgressMatch.Success)
            {
                if (total == 0)
                {
                    total = int.Parse(workflowProgressMatch.Groups[2].Value);
                }
                continue;
            }

            // 匹配 "文档生成完成" 格式
            if (log.Message.Contains("文档生成完成"))
            {
                completed = total;
            }
        }

        return (total, completed);
    }

    /// <inheritdoc />
    public async Task ClearLogsAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var logs = await context.RepositoryProcessingLogs
            .Where(log => log.RepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        context.RepositoryProcessingLogs.RemoveRange(logs);
        await context.SaveChangesAsync(cancellationToken);
    }
}
