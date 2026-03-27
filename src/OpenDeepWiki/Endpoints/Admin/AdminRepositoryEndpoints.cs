using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Overlays;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Endpoints.Admin;

/// <summary>
/// 管理端仓库管理端点
/// </summary>
public static class AdminRepositoryEndpoints
{
    public static RouteGroupBuilder MapAdminRepositoryEndpoints(this RouteGroupBuilder group)
    {
        var repoGroup = group.MapGroup("/repositories")
            .WithTags("管理端-仓库管理");

        // 获取仓库列表（分页）
        repoGroup.MapGet("/", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromQuery] int? status,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            var result = await repositoryService.GetRepositoriesAsync(page, pageSize, search, status);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminGetRepositories")
        .WithSummary("获取仓库列表");

        // 获取仓库详情
        repoGroup.MapGet("/{id}", async (
            string id,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.GetRepositoryByIdAsync(id);
            if (result == null)
                return Results.NotFound(new { success = false, message = "仓库不存在" });
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminGetRepository")
        .WithSummary("获取仓库详情");

        // 获取仓库深度管理信息（分支、语言、增量任务）
        repoGroup.MapGet("/{id}/management", async (
            string id,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.GetRepositoryManagementAsync(id);
            if (result == null)
                return Results.NotFound(new { success = false, message = "仓库不存在" });
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminGetRepositoryManagement")
        .WithSummary("获取仓库深度管理信息");

        // 更新仓库
        repoGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateRepositoryRequest request,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.UpdateRepositoryAsync(id, request);
            if (!result)
                return Results.NotFound(new { success = false, message = "仓库不存在" });
            return Results.Ok(new { success = true, message = "更新成功" });
        })
        .WithName("AdminUpdateRepository")
        .WithSummary("更新仓库");

        // 删除仓库
        repoGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.DeleteRepositoryAsync(id);
            if (!result)
                return Results.NotFound(new { success = false, message = "仓库不存在" });
            return Results.Ok(new { success = true, message = "删除成功" });
        })
        .WithName("AdminDeleteRepository")
        .WithSummary("删除仓库");

        // 更新仓库状态
        repoGroup.MapPut("/{id}/status", async (
            string id,
            [FromBody] UpdateStatusRequest request,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.UpdateRepositoryStatusAsync(id, request.Status);
            if (!result)
                return Results.NotFound(new { success = false, message = "仓库不存在" });
            return Results.Ok(new { success = true, message = "状态更新成功" });
        })
        .WithName("AdminUpdateRepositoryStatus")
        .WithSummary("更新仓库状态");

        // 同步单个仓库统计信息
        repoGroup.MapPost("/{id}/sync-stats", async (
            string id,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.SyncRepositoryStatsAsync(id);
            return Results.Ok(new { success = result.Success, message = result.Message, data = result });
        })
        .WithName("AdminSyncRepositoryStats")
        .WithSummary("同步仓库统计信息");

        // 触发仓库全量重生成
        repoGroup.MapPost("/{id}/regenerate", async (
            string id,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.RegenerateRepositoryAsync(id);
            return Results.Ok(new { success = result.Success, message = result.Message, data = result });
        })
        .WithName("AdminRegenerateRepository")
        .WithSummary("触发仓库全量重生成");

        // 触发指定文档重生成
        repoGroup.MapPost("/{id}/documents/regenerate", async (
            string id,
            [FromBody] RegenerateRepositoryDocumentRequest request,
            [FromServices] IAdminRepositoryService repositoryService,
            CancellationToken cancellationToken) =>
        {
            var result = await repositoryService.RegenerateDocumentAsync(id, request, cancellationToken);
            return Results.Ok(new { success = result.Success, message = result.Message, data = result });
        })
        .WithName("AdminRegenerateRepositoryDocument")
        .WithSummary("触发指定文档重生成");

        // 触发业务流程局部重建
        repoGroup.MapPost("/{id}/workflows/regenerate", async (
            string id,
            [FromBody] RegenerateRepositoryWorkflowRequest request,
            [FromServices] IAdminRepositoryService repositoryService,
            CancellationToken cancellationToken) =>
        {
            var result = await repositoryService.RegenerateWorkflowDocumentsAsync(id, request, cancellationToken);
            return Results.Ok(new { success = result.Success, message = result.Message, data = result });
        })
        .WithName("AdminRegenerateRepositoryWorkflows")
        .WithSummary("触发业务流程局部重建");

        // 手动更新指定文档内容
        repoGroup.MapPut("/{id}/documents/content", async (
            string id,
            [FromBody] UpdateRepositoryDocumentContentRequest request,
            [FromServices] IAdminRepositoryService repositoryService,
            CancellationToken cancellationToken) =>
        {
            var result = await repositoryService.UpdateDocumentContentAsync(id, request, cancellationToken);
            return Results.Ok(new { success = result.Success, message = result.Message, data = result });
        })
        .WithName("AdminUpdateRepositoryDocumentContent")
        .WithSummary("手动更新指定文档内容");

        // 获取仓库 Workflow 配置
        repoGroup.MapGet("/{id}/workflow-config", async (
            string id,
            [FromServices] IRepositoryWorkflowConfigService workflowConfigService,
            CancellationToken cancellationToken) =>
        {
            var config = await workflowConfigService.GetConfigAsync(id, cancellationToken);
            return Results.Ok(new { success = true, data = config });
        })
        .WithName("AdminGetRepositoryWorkflowConfig")
        .WithSummary("获取仓库 Workflow 配置");

        // 保存仓库 Workflow 配置
        repoGroup.MapPut("/{id}/workflow-config", async (
            string id,
            [FromBody] RepositoryWorkflowConfig config,
            [FromServices] IRepositoryWorkflowConfigService workflowConfigService,
            CancellationToken cancellationToken) =>
        {
            var saved = await workflowConfigService.SaveConfigAsync(id, config, cancellationToken);
            return Results.Ok(new { success = true, data = saved });
        })
        .WithName("AdminSaveRepositoryWorkflowConfig")
        .WithSummary("保存仓库 Workflow 配置");

        repoGroup.MapGet("/{id}/workflow-template/sessions", async (
            string id,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var sessions = await workbenchService.GetSessionsAsync(id, cancellationToken);
                return Results.Ok(new { success = true, data = sessions });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminGetRepositoryWorkflowTemplateSessions")
        .WithSummary("获取业务流模板工作台会话列表");

        repoGroup.MapPost("/{id}/workflow-template/sessions", async (
            string id,
            [FromBody] CreateWorkflowTemplateSessionRequest request,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await workbenchService.CreateSessionAsync(id, request, cancellationToken);
                return Results.Ok(new { success = true, data = session });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminCreateRepositoryWorkflowTemplateSession")
        .WithSummary("创建业务流模板工作台会话");

        repoGroup.MapGet("/{id}/workflow-template/sessions/{sessionId}", async (
            string id,
            string sessionId,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await workbenchService.GetSessionAsync(id, sessionId, cancellationToken);
                return Results.Ok(new { success = true, data = session });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminGetRepositoryWorkflowTemplateSession")
        .WithSummary("获取业务流模板工作台会话详情");

        repoGroup.MapPost("/{id}/workflow-template/sessions/{sessionId}/messages", async (
            string id,
            string sessionId,
            [FromBody] WorkflowTemplateMessageRequest request,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await workbenchService.SendMessageAsync(id, sessionId, request, cancellationToken);
                return Results.Ok(new { success = true, data = session });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminSendRepositoryWorkflowTemplateMessage")
        .WithSummary("发送业务流模板工作台消息");

        repoGroup.MapPost("/{id}/workflow-template/sessions/{sessionId}/versions/{versionNumber}/adopt", async (
            string id,
            string sessionId,
            int versionNumber,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await workbenchService.AdoptVersionAsync(id, sessionId, versionNumber, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminAdoptRepositoryWorkflowTemplateVersion")
        .WithSummary("采用业务流模板版本到正式配置");

        repoGroup.MapPost("/{id}/workflow-template/sessions/{sessionId}/versions/{versionNumber}/rollback", async (
            string id,
            string sessionId,
            int versionNumber,
            [FromServices] IWorkflowTemplateWorkbenchService workbenchService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await workbenchService.RollbackAsync(id, sessionId, versionNumber, cancellationToken);
                return Results.Ok(new { success = true, data = session });
            }
            catch (Exception ex)
            {
                return MapWorkflowTemplateError(ex);
            }
        })
        .WithName("AdminRollbackRepositoryWorkflowTemplateVersion")
        .WithSummary("回滚业务流模板草稿到指定版本");

        // 批量同步仓库统计信息
        repoGroup.MapPost("/batch/sync-stats", async (
            [FromBody] BatchOperationRequest request,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.BatchSyncRepositoryStatsAsync(request.Ids);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminBatchSyncRepositoryStats")
        .WithSummary("批量同步仓库统计信息");

        // 批量删除仓库
        repoGroup.MapPost("/batch/delete", async (
            [FromBody] BatchOperationRequest request,
            [FromServices] IAdminRepositoryService repositoryService) =>
        {
            var result = await repositoryService.BatchDeleteRepositoriesAsync(request.Ids);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminBatchDeleteRepositories")
        .WithSummary("批量删除仓库");

        // ==================== Overlay Wiki ====================

        // 获取仓库 Overlay 配置
        repoGroup.MapGet("/{id}/overlay-config", async (
            string id,
            [FromServices] IAdminRepositoryOverlayService overlayService,
            CancellationToken cancellationToken) =>
        {
            var config = await overlayService.GetConfigAsync(id, cancellationToken);
            return Results.Ok(new { success = true, data = config });
        })
        .WithName("AdminGetRepositoryOverlayConfig")
        .WithSummary("获取仓库 Overlay 配置");

        // 保存仓库 Overlay 配置
        repoGroup.MapPut("/{id}/overlay-config", async (
            string id,
            [FromBody] RepositoryOverlayConfig config,
            [FromServices] IAdminRepositoryOverlayService overlayService,
            CancellationToken cancellationToken) =>
        {
            var saved = await overlayService.SaveConfigAsync(id, config, cancellationToken);
            return Results.Ok(new { success = true, data = saved });
        })
        .WithName("AdminSaveRepositoryOverlayConfig")
        .WithSummary("保存仓库 Overlay 配置");

        // AI 分析代码结构并生成 Overlay 配置建议
        repoGroup.MapPost("/{id}/overlay/suggest", async (
            string id,
            [FromBody] OverlaySuggestRequest? request,
            [FromServices] IOverlaySuggestionService overlaySuggestionService,
            CancellationToken cancellationToken) =>
        {
            var result = await overlaySuggestionService.SuggestAsync(id, request, cancellationToken);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("AdminSuggestRepositoryOverlayConfig")
        .WithSummary("AI 分析代码结构并生成 Overlay 配置建议");

        // 预览 Overlay 差异
        repoGroup.MapPost("/{id}/overlay/preview", async (
            string id,
            [FromQuery] string? profileKey,
            [FromServices] IOverlayWikiService overlayWikiService,
            CancellationToken cancellationToken) =>
        {
            var preview = await overlayWikiService.PreviewAsync(id, profileKey, cancellationToken);
            return Results.Ok(new { success = true, data = preview });
        })
        .WithName("AdminPreviewRepositoryOverlay")
        .WithSummary("预览 Overlay 差异（覆盖/新增）");

        // 生成 Overlay Wiki（写入 overlay/* 虚拟分支）
        repoGroup.MapPost("/{id}/overlay/generate", async (
            string id,
            [FromQuery] string? profileKey,
            [FromServices] IOverlayWikiService overlayWikiService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await overlayWikiService.GenerateAsync(id, profileKey, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { success = false, message = $"生成 Overlay Wiki 失败: {ex.Message}" },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("AdminGenerateRepositoryOverlayWiki")
        .WithSummary("生成 Overlay Wiki（虚拟分支）");

        return group;
    }

    private static IResult MapWorkflowTemplateError(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => Results.NotFound(new { success = false, message = ex.Message }),
            InvalidOperationException => Results.BadRequest(new { success = false, message = ex.Message }),
            ArgumentException => Results.BadRequest(new { success = false, message = ex.Message }),
            _ => Results.Problem(ex.Message)
        };
    }
}
