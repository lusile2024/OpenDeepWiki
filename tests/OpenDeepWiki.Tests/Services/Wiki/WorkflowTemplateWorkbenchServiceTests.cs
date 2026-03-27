using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.Wiki;
using OpenDeepWiki.Tests.Chat.Sessions;
using Xunit;
using SessionTestDbContext = OpenDeepWiki.Tests.Chat.Sessions.TestDbContext;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowTemplateWorkbenchServiceTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldCreateAssistantVersionAndPersistConversation()
    {
        using var context = SessionTestDbContext.Create();
        var repository = await SeedRepositoryAsync(context);

        var service = CreateService(
            context,
            aiClient: new StubAiClient(() => new WorkflowTemplateWorkbenchAiResult
            {
                Title = "货位异常恢复模板",
                AssistantMessage = "已把草稿收敛为货位异常恢复单流程，并排除了日志重试入口。",
                ChangeSummary = "明确单流程锚点、入口和调度链路",
                RiskNotes = ["尚未确认是否还有第二个补偿入口"],
                EvidenceFiles =
                [
                    "src/Cimc.Tianda.Wms.Application/Wcs/WcsRequestExecutors/LocExceptionRecoverExecutor.cs",
                    "src/Cimc.Tianda.Wms.Jobs/Wcs/WcsRequestWmsExecutorJob.cs"
                ],
                UpdatedDraft = new RepositoryWorkflowProfile
                {
                    Key = "loc-exception-recover",
                    Name = "货位异常恢复",
                    Description = "WCS 发起货位异常恢复请求，WMS 持久化后由定时任务扫描并分发到对应 executor。",
                    Enabled = false,
                    Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
                    AnchorNames = ["LocExceptionRecoverExecutor"],
                    PrimaryTriggerNames = ["WmsJobInterfaceController"],
                    CompensationTriggerNames = ["LogExternalInterfaceController"],
                    SchedulerNames = ["WcsRequestWmsExecutorJob"],
                    RequestEntityNames = ["WcsRequest"],
                    RequestServiceNames = ["IWcsRequestService", "WcsRequestService"],
                    RequestRepositoryNames = ["IWcsRequestRepository", "WcsRequestRepository"],
                    DocumentPreferences = new WorkflowDocumentPreferences
                    {
                        WritingHint = "正文要强调主入口、请求落库、调度扫描、executor 执行、失败补偿的顺序。",
                        PreferredTerms = ["货位异常恢复", "主入口", "补偿入口"],
                        RequiredSections = ["端到端时序", "状态流转"],
                        AvoidPrimaryTriggerNames = ["LogExternalInterfaceController"]
                    }
                }
            }));

        var session = await service.CreateSessionAsync(
            repository.Id,
            new CreateWorkflowTemplateSessionRequest
            {
                BranchId = "branch-main",
                LanguageCode = "zh"
            });

        var detail = await service.SendMessageAsync(
            repository.Id,
            session.SessionId,
            new WorkflowTemplateMessageRequest
            {
                Content = "把这个流程限定成货位异常恢复，主入口不是日志控制器。"
            });

        Assert.Equal(1, detail.CurrentVersionNumber);
        Assert.Equal("货位异常恢复模板", detail.Title);
        Assert.Equal("loc-exception-recover", detail.CurrentDraftKey);
        Assert.Equal("货位异常恢复", detail.CurrentDraftName);
        Assert.Equal(2, detail.Messages.Count);
        Assert.Equal("User", detail.Messages[0].Role);
        Assert.Equal("Assistant", detail.Messages[1].Role);

        var version = Assert.Single(detail.Versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("assistant", version.SourceType);
        Assert.Empty(version.ValidationIssues);
        Assert.Contains("LogExternalInterfaceController", version.Draft.DocumentPreferences.AvoidPrimaryTriggerNames);
        Assert.Contains("LocExceptionRecoverExecutor", version.Draft.AnchorNames);
    }

    [Fact]
    public async Task RollbackAsync_ShouldCloneSelectedVersionAsNewCurrentVersion()
    {
        using var context = SessionTestDbContext.Create();
        var repository = await SeedRepositoryAsync(context);

        var aiClient = new StubAiClient(
            new WorkflowTemplateWorkbenchAiResult
            {
                Title = "货位异常恢复模板",
                AssistantMessage = "第一版草稿。",
                ChangeSummary = "创建第一版",
                UpdatedDraft = CreateDraft("loc-exception-recover-v1", "货位异常恢复 V1", "LocExceptionRecoverExecutor")
            },
            new WorkflowTemplateWorkbenchAiResult
            {
                Title = "货位异常恢复模板",
                AssistantMessage = "第二版草稿。",
                ChangeSummary = "补充补偿入口",
                UpdatedDraft = CreateDraft("loc-exception-recover-v2", "货位异常恢复 V2", "LocExceptionRecoverExecutor")
            });

        var service = CreateService(context, aiClient: aiClient);
        var session = await service.CreateSessionAsync(
            repository.Id,
            new CreateWorkflowTemplateSessionRequest
            {
                BranchId = "branch-main",
                LanguageCode = "zh"
            });

        await service.SendMessageAsync(repository.Id, session.SessionId, new WorkflowTemplateMessageRequest { Content = "先出第一版。" });
        await service.SendMessageAsync(repository.Id, session.SessionId, new WorkflowTemplateMessageRequest { Content = "再出第二版。" });

        var detail = await service.RollbackAsync(repository.Id, session.SessionId, versionNumber: 1);

        Assert.Equal(3, detail.CurrentVersionNumber);
        Assert.Equal("loc-exception-recover-v1", detail.CurrentDraftKey);
        Assert.Equal("货位异常恢复 V1", detail.CurrentDraftName);
        Assert.Equal(5, detail.Messages.Count);
        Assert.Equal("System", detail.Messages[^1].Role);
        Assert.Equal(3, detail.Versions.Count);
        Assert.Equal(1, detail.Versions[0].BasedOnVersionNumber);
        Assert.Equal("rollback", detail.Versions[0].SourceType);
    }

    [Fact]
    public async Task AdoptVersionAsync_ShouldMergeDraftIntoFormalWorkflowConfigAndKeepDisabled()
    {
        using var context = SessionTestDbContext.Create();
        var repository = await SeedRepositoryAsync(context);

        var service = CreateService(
            context,
            aiClient: new StubAiClient(() => new WorkflowTemplateWorkbenchAiResult
            {
                Title = "容器托盘入库模板",
                AssistantMessage = "已生成容器托盘入库模板。",
                ChangeSummary = "新增容器托盘入库正式草稿",
                UpdatedDraft = CreateDraft("container-pallet-inbound", "容器托盘入库", "ContainerPalletInboundExecutor")
            }));

        var session = await service.CreateSessionAsync(
            repository.Id,
            new CreateWorkflowTemplateSessionRequest
            {
                BranchId = "branch-main",
                LanguageCode = "zh"
            });

        var draftDetail = await service.SendMessageAsync(
            repository.Id,
            session.SessionId,
            new WorkflowTemplateMessageRequest
            {
                Content = "帮我新增容器托盘入库流程模板。"
            });

        var adopted = await service.AdoptVersionAsync(repository.Id, session.SessionId, draftDetail.CurrentVersionNumber);

        var profile = Assert.Single(adopted.SavedConfig.Profiles);
        Assert.Equal("container-pallet-inbound", profile.Key);
        Assert.False(profile.Enabled);
        Assert.Equal("ai-workbench", profile.Source.Type);
        Assert.Equal(session.SessionId, profile.Source.SessionId);
        Assert.Equal(draftDetail.CurrentVersionNumber, profile.Source.VersionNumber);
        Assert.Equal(draftDetail.CurrentVersionNumber, adopted.Session.AdoptedVersionNumber);
    }

    private static WorkflowTemplateWorkbenchService CreateService(
        SessionTestDbContext context,
        StubAiClient? aiClient = null,
        IWorkflowTemplateContextCollector? contextCollector = null)
    {
        var workflowConfigService = new RepositoryWorkflowConfigService(
            context,
            NullLogger<RepositoryWorkflowConfigService>.Instance);

        return new WorkflowTemplateWorkbenchService(
            context,
            workflowConfigService,
            contextCollector ?? new StubContextCollector(),
            aiClient ?? new StubAiClient(() => throw new InvalidOperationException("AI response was not configured.")),
            new StubUserContext());
    }

    private static async Task<Repository> SeedRepositoryAsync(SessionTestDbContext context)
    {
        var repository = new Repository
        {
            Id = "repo-1",
            OwnerUserId = "user-1",
            OrgName = "local",
            RepoName = "WmsServerV4Dev",
            GitUrl = @"local/D:\WMS4\DevWorkSpace\WmsServerV4Dev",
            Status = RepositoryStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new RepositoryBranch
        {
            Id = "branch-main",
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = "snapshot-1",
            CreatedAt = DateTime.UtcNow
        };
        var language = new BranchLanguage
        {
            Id = "lang-zh",
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);
        await context.SaveChangesAsync();
        return repository;
    }

    private static RepositoryWorkflowProfile CreateDraft(string key, string name, string anchorName)
    {
        return new RepositoryWorkflowProfile
        {
            Key = key,
            Name = name,
            Description = $"{name}业务流模板",
            Enabled = false,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            AnchorNames = [anchorName],
            PrimaryTriggerNames = ["WmsJobInterfaceController"],
            CompensationTriggerNames = ["LogExternalInterfaceController"],
            SchedulerNames = ["WcsRequestWmsExecutorJob"],
            RequestEntityNames = ["WcsRequest"],
            RequestServiceNames = ["WcsRequestService"],
            RequestRepositoryNames = ["WcsRequestRepository"]
        };
    }

    private sealed class StubContextCollector : IWorkflowTemplateContextCollector
    {
        public Task<WorkflowTemplateSessionContextDto> CollectAsync(
            Repository repository,
            RepositoryBranch? branch,
            string? languageCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowTemplateSessionContextDto
            {
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BranchName = branch?.BranchName ?? "main",
                LanguageCode = languageCode ?? "zh",
                PrimaryLanguage = "C#",
                SourceLocation = repository.SourceLocation,
                DirectoryPreview = "- src/\n  - Cimc.Tianda.Wms.Application/\n  - Cimc.Tianda.Wms.Jobs/",
                DiscoveryCandidates =
                [
                    new WorkflowTemplateDiscoveryCandidateDto
                    {
                        Key = "loc-exception-recover",
                        Name = "货位异常恢复",
                        Summary = "货位异常恢复相关业务流程",
                        TriggerPoints = ["WmsJobInterfaceController"],
                        CompensationTriggerPoints = ["LogExternalInterfaceController"],
                        ExecutorFiles = ["src/Cimc.Tianda.Wms.Application/Wcs/WcsRequestExecutors/LocExceptionRecoverExecutor.cs"],
                        EvidenceFiles = ["src/Cimc.Tianda.Wms.Jobs/Wcs/WcsRequestWmsExecutorJob.cs"]
                    }
                ]
            });
        }
    }

    private sealed class StubAiClient : IWorkflowTemplateWorkbenchAiClient
    {
        private readonly Queue<Func<WorkflowTemplateWorkbenchAiResult>> _responses;

        public StubAiClient(params WorkflowTemplateWorkbenchAiResult[] responses)
        {
            _responses = new Queue<Func<WorkflowTemplateWorkbenchAiResult>>();
            foreach (var response in responses)
            {
                _responses.Enqueue(() => response);
            }
        }

        public StubAiClient(Func<WorkflowTemplateWorkbenchAiResult> responseFactory)
        {
            _responses = new Queue<Func<WorkflowTemplateWorkbenchAiResult>>([responseFactory]);
        }

        public Task<WorkflowTemplateWorkbenchAiResult> GenerateDraftAsync(
            WorkflowTemplateWorkbenchAiRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more AI responses were configured.");
            }

            return Task.FromResult(_responses.Dequeue().Invoke());
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public string? UserId => "admin-1";

        public string? UserName => "token帅比";

        public string? Email => "admin@example.com";

        public bool IsAuthenticated => true;

        public System.Security.Claims.ClaimsPrincipal? User => null;
    }
}
