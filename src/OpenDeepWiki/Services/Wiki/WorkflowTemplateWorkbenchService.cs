using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Auth;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowTemplateWorkbenchService
{
    Task<List<WorkflowTemplateSessionSummaryDto>> GetSessionsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task<WorkflowTemplateSessionDetailDto> CreateSessionAsync(
        string repositoryId,
        CreateWorkflowTemplateSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowTemplateSessionDetailDto> GetSessionAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<WorkflowTemplateSessionDetailDto> SendMessageAsync(
        string repositoryId,
        string sessionId,
        WorkflowTemplateMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowTemplateSessionDetailDto> RollbackAsync(
        string repositoryId,
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken = default);

    Task<WorkflowTemplateAdoptResultDto> AdoptVersionAsync(
        string repositoryId,
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowTemplateWorkbenchService : IWorkflowTemplateWorkbenchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IContext _context;
    private readonly IRepositoryWorkflowConfigService _workflowConfigService;
    private readonly IWorkflowTemplateContextCollector _contextCollector;
    private readonly IWorkflowTemplateWorkbenchAiClient _aiClient;
    private readonly IUserContext _userContext;

    public WorkflowTemplateWorkbenchService(
        IContext context,
        IRepositoryWorkflowConfigService workflowConfigService,
        IWorkflowTemplateContextCollector contextCollector,
        IWorkflowTemplateWorkbenchAiClient aiClient,
        IUserContext userContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _workflowConfigService = workflowConfigService ?? throw new ArgumentNullException(nameof(workflowConfigService));
        _contextCollector = contextCollector ?? throw new ArgumentNullException(nameof(contextCollector));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public async Task<List<WorkflowTemplateSessionSummaryDto>> GetSessionsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        await EnsureRepositoryExistsAsync(repositoryId, cancellationToken);

        return await _context.WorkflowTemplateSessions
            .AsNoTracking()
            .Where(session => session.RepositoryId == repositoryId && !session.IsDeleted)
            .OrderByDescending(session => session.LastActivityAt)
            .Select(session => new WorkflowTemplateSessionSummaryDto
            {
                SessionId = session.Id,
                RepositoryId = session.RepositoryId,
                Status = session.Status,
                Title = session.Title,
                BranchId = session.BranchId,
                BranchName = session.BranchName,
                LanguageCode = session.LanguageCode,
                CurrentDraftKey = session.CurrentDraftKey,
                CurrentDraftName = session.CurrentDraftName,
                CurrentVersionNumber = session.CurrentVersionNumber,
                AdoptedVersionNumber = session.AdoptedVersionNumber,
                MessageCount = session.MessageCount,
                LastActivityAt = session.LastActivityAt,
                CreatedAt = session.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowTemplateSessionDetailDto> CreateSessionAsync(
        string repositoryId,
        CreateWorkflowTemplateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var repository = await GetRepositoryAsync(repositoryId, cancellationToken);
        var branch = await ResolveBranchAsync(repositoryId, request.BranchId, cancellationToken);
        var languageCode = await ResolveLanguageCodeAsync(branch?.Id, request.LanguageCode, cancellationToken);
        var context = await _contextCollector.CollectAsync(repository, branch, languageCode, cancellationToken);

        var session = new WorkflowTemplateSession
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchId = branch?.Id,
            BranchName = context.BranchName,
            LanguageCode = context.LanguageCode,
            Status = "Active",
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            CreatedByUserId = _userContext.UserId,
            CreatedByUserName = _userContext.UserName,
            LastActivityAt = DateTime.UtcNow,
            ContextJson = JsonSerializer.Serialize(context, JsonOptions)
        };

        _context.WorkflowTemplateSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetSessionAsync(repositoryId, session.Id, cancellationToken);
    }

    public async Task<WorkflowTemplateSessionDetailDto> GetSessionAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionEntityAsync(repositoryId, sessionId, cancellationToken);
        var messages = await _context.WorkflowTemplateMessages
            .AsNoTracking()
            .Where(message => message.SessionId == session.Id && !message.IsDeleted)
            .OrderBy(message => message.SequenceNumber)
            .ToListAsync(cancellationToken);
        var versions = await _context.WorkflowTemplateDraftVersions
            .AsNoTracking()
            .Where(version => version.SessionId == session.Id && !version.IsDeleted)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

        return new WorkflowTemplateSessionDetailDto
        {
            SessionId = session.Id,
            RepositoryId = session.RepositoryId,
            Status = session.Status,
            Title = session.Title,
            BranchId = session.BranchId,
            BranchName = session.BranchName,
            LanguageCode = session.LanguageCode,
            CurrentDraftKey = session.CurrentDraftKey,
            CurrentDraftName = session.CurrentDraftName,
            CurrentVersionNumber = session.CurrentVersionNumber,
            AdoptedVersionNumber = session.AdoptedVersionNumber,
            MessageCount = session.MessageCount,
            LastActivityAt = session.LastActivityAt,
            CreatedAt = session.CreatedAt,
            Context = DeserializeContext(session.ContextJson),
            CurrentDraft = versions
                .Where(version => version.VersionNumber == session.CurrentVersionNumber)
                .Select(version => DeserializeDraft(version.DraftJson))
                .FirstOrDefault(),
            Messages = messages.Select(message => new WorkflowTemplateMessageDto
            {
                Id = message.Id,
                SequenceNumber = message.SequenceNumber,
                Role = message.Role,
                Content = message.Content,
                VersionNumber = message.VersionNumber,
                ChangeSummary = message.ChangeSummary,
                MessageTimestamp = message.MessageTimestamp
            }).ToList(),
            Versions = versions.Select(MapVersion).ToList()
        };
    }

    public async Task<WorkflowTemplateSessionDetailDto> SendMessageAsync(
        string repositoryId,
        string sessionId,
        WorkflowTemplateMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new InvalidOperationException("消息内容不能为空。");
        }

        var repository = await GetRepositoryAsync(repositoryId, cancellationToken);
        var session = await GetSessionEntityAsync(repositoryId, sessionId, cancellationToken);

        if (string.IsNullOrWhiteSpace(session.ContextJson))
        {
            var branch = await ResolveBranchAsync(repositoryId, session.BranchId, cancellationToken);
            var context = await _contextCollector.CollectAsync(repository, branch, session.LanguageCode, cancellationToken);
            session.ContextJson = JsonSerializer.Serialize(context, JsonOptions);
        }

        var userSequenceNumber = await GetNextMessageSequenceAsync(session.Id, cancellationToken);
        _context.WorkflowTemplateMessages.Add(new WorkflowTemplateMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            SequenceNumber = userSequenceNumber,
            Role = "User",
            Content = request.Content.Trim(),
            MessageTimestamp = DateTime.UtcNow
        });

        session.MessageCount += 1;
        session.LastActivityAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var history = await _context.WorkflowTemplateMessages
            .AsNoTracking()
            .Where(message => message.SessionId == session.Id && !message.IsDeleted)
            .OrderBy(message => message.SequenceNumber)
            .ToListAsync(cancellationToken);
        var currentDraft = await GetCurrentDraftAsync(session.Id, session.CurrentVersionNumber, cancellationToken)
                          ?? CreateDefaultDraft();
        var existingConfig = await _workflowConfigService.GetConfigAsync(repositoryId, cancellationToken);

        var aiResult = await _aiClient.GenerateDraftAsync(
            new WorkflowTemplateWorkbenchAiRequest
            {
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BranchName = session.BranchName ?? "main",
                LanguageCode = string.IsNullOrWhiteSpace(session.LanguageCode) ? "zh" : session.LanguageCode!,
                ExistingConfig = existingConfig,
                CurrentDraft = currentDraft,
                Context = DeserializeContext(session.ContextJson),
                History = history
                    .TakeLast(12)
                    .Select(message => new WorkflowTemplateConversationTurn
                    {
                        Role = message.Role,
                        Content = message.Content,
                        VersionNumber = message.VersionNumber,
                        ChangeSummary = message.ChangeSummary,
                        Timestamp = message.MessageTimestamp
                    })
                    .ToList(),
                UserMessage = request.Content.Trim()
            },
            cancellationToken);

        var updatedDraft = RepositoryWorkflowConfigRules.SanitizeProfile(aiResult.UpdatedDraft);
        var nextVersionNumber = session.CurrentVersionNumber + 1;
        updatedDraft.Source = new RepositoryWorkflowProfileSource
        {
            Type = "ai-workbench-draft",
            SessionId = session.Id,
            VersionNumber = nextVersionNumber,
            UpdatedByUserId = _userContext.UserId,
            UpdatedByUserName = _userContext.UserName,
            UpdatedAt = DateTime.UtcNow
        };

        var validationIssues = RepositoryWorkflowConfigRules.GetDraftValidationIssues(updatedDraft);
        _context.WorkflowTemplateDraftVersions.Add(new WorkflowTemplateDraftVersion
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            VersionNumber = nextVersionNumber,
            SourceType = "assistant",
            ChangeSummary = string.IsNullOrWhiteSpace(aiResult.ChangeSummary)
                ? "根据最新指令更新草稿"
                : aiResult.ChangeSummary.Trim(),
            DraftJson = JsonSerializer.Serialize(updatedDraft, JsonOptions),
            RiskNotesJson = JsonSerializer.Serialize(aiResult.RiskNotes ?? [], JsonOptions),
            EvidenceFilesJson = JsonSerializer.Serialize(aiResult.EvidenceFiles ?? [], JsonOptions),
            ValidationIssuesJson = JsonSerializer.Serialize(validationIssues, JsonOptions)
        });

        _context.WorkflowTemplateMessages.Add(new WorkflowTemplateMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            SequenceNumber = userSequenceNumber + 1,
            Role = "Assistant",
            Content = string.IsNullOrWhiteSpace(aiResult.AssistantMessage)
                ? "已更新业务流模板草稿。"
                : aiResult.AssistantMessage.Trim(),
            VersionNumber = nextVersionNumber,
            ChangeSummary = string.IsNullOrWhiteSpace(aiResult.ChangeSummary)
                ? null
                : aiResult.ChangeSummary.Trim(),
            MessageTimestamp = DateTime.UtcNow
        });

        session.CurrentVersionNumber = nextVersionNumber;
        session.CurrentDraftKey = updatedDraft.Key;
        session.CurrentDraftName = updatedDraft.Name;
        session.MessageCount += 1;
        session.LastActivityAt = DateTime.UtcNow;
        session.Title = ResolveSessionTitle(session.Title, aiResult.Title, updatedDraft.Name);

        await _context.SaveChangesAsync(cancellationToken);
        return await GetSessionAsync(repositoryId, session.Id, cancellationToken);
    }

    public async Task<WorkflowTemplateSessionDetailDto> RollbackAsync(
        string repositoryId,
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionEntityAsync(repositoryId, sessionId, cancellationToken);
        var sourceVersion = await GetVersionEntityAsync(session.Id, versionNumber, cancellationToken);
        var rollbackDraft = DeserializeDraft(sourceVersion.DraftJson);
        var nextVersionNumber = session.CurrentVersionNumber + 1;

        _context.WorkflowTemplateDraftVersions.Add(new WorkflowTemplateDraftVersion
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            VersionNumber = nextVersionNumber,
            BasedOnVersionNumber = sourceVersion.VersionNumber,
            SourceType = "rollback",
            ChangeSummary = $"回滚到 v{sourceVersion.VersionNumber}",
            DraftJson = sourceVersion.DraftJson,
            RiskNotesJson = sourceVersion.RiskNotesJson,
            EvidenceFilesJson = sourceVersion.EvidenceFilesJson,
            ValidationIssuesJson = sourceVersion.ValidationIssuesJson
        });

        var sequenceNumber = await GetNextMessageSequenceAsync(session.Id, cancellationToken);
        _context.WorkflowTemplateMessages.Add(new WorkflowTemplateMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            SequenceNumber = sequenceNumber,
            Role = "System",
            Content = $"已基于 v{sourceVersion.VersionNumber} 创建回滚草稿，当前版本为 v{nextVersionNumber}。",
            VersionNumber = nextVersionNumber,
            ChangeSummary = $"回滚到 v{sourceVersion.VersionNumber}",
            MessageTimestamp = DateTime.UtcNow
        });

        session.CurrentVersionNumber = nextVersionNumber;
        session.CurrentDraftKey = rollbackDraft.Key;
        session.CurrentDraftName = rollbackDraft.Name;
        session.LastActivityAt = DateTime.UtcNow;
        session.MessageCount += 1;

        await _context.SaveChangesAsync(cancellationToken);
        return await GetSessionAsync(repositoryId, session.Id, cancellationToken);
    }

    public async Task<WorkflowTemplateAdoptResultDto> AdoptVersionAsync(
        string repositoryId,
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionEntityAsync(repositoryId, sessionId, cancellationToken);
        var version = await GetVersionEntityAsync(session.Id, versionNumber, cancellationToken);
        var draft = RepositoryWorkflowConfigRules.SanitizeProfile(DeserializeDraft(version.DraftJson));
        draft.Enabled = false;
        draft.Source = new RepositoryWorkflowProfileSource
        {
            Type = "ai-workbench",
            SessionId = session.Id,
            VersionNumber = version.VersionNumber,
            UpdatedByUserId = _userContext.UserId,
            UpdatedByUserName = _userContext.UserName,
            UpdatedAt = DateTime.UtcNow
        };

        var config = await _workflowConfigService.GetConfigAsync(repositoryId, cancellationToken);
        var index = config.Profiles.FindIndex(profile =>
            string.Equals(profile.Key, draft.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            config.Profiles[index] = draft;
        }
        else
        {
            config.Profiles.Add(draft);
        }

        var savedConfig = await _workflowConfigService.SaveConfigAsync(repositoryId, config, cancellationToken);
        var sequenceNumber = await GetNextMessageSequenceAsync(session.Id, cancellationToken);
        _context.WorkflowTemplateMessages.Add(new WorkflowTemplateMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            SequenceNumber = sequenceNumber,
            Role = "System",
            Content = $"已将 v{version.VersionNumber} 采用到正式 Workflow 配置，并保持 enabled=false 以便你继续人工确认后再启用。",
            VersionNumber = version.VersionNumber,
            ChangeSummary = $"采用 v{version.VersionNumber} 到正式配置",
            MessageTimestamp = DateTime.UtcNow
        });

        session.AdoptedVersionNumber = version.VersionNumber;
        session.LastActivityAt = DateTime.UtcNow;
        session.MessageCount += 1;
        await _context.SaveChangesAsync(cancellationToken);

        return new WorkflowTemplateAdoptResultDto
        {
            Session = await GetSessionAsync(repositoryId, session.Id, cancellationToken),
            SavedConfig = savedConfig
        };
    }

    private async Task<Repository> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        return await _context.Repositories
            .FirstOrDefaultAsync(repository => repository.Id == repositoryId && !repository.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("仓库不存在。");
    }

    private async Task EnsureRepositoryExistsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var exists = await _context.Repositories.AnyAsync(
            repository => repository.Id == repositoryId && !repository.IsDeleted,
            cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("仓库不存在。");
        }
    }

    private async Task<WorkflowTemplateSession> GetSessionEntityAsync(
        string repositoryId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        return await _context.WorkflowTemplateSessions
            .FirstOrDefaultAsync(
                session => session.RepositoryId == repositoryId &&
                           session.Id == sessionId &&
                           !session.IsDeleted,
                cancellationToken)
            ?? throw new KeyNotFoundException("模板会话不存在。");
    }

    private async Task<WorkflowTemplateDraftVersion> GetVersionEntityAsync(
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        return await _context.WorkflowTemplateDraftVersions
            .FirstOrDefaultAsync(
                version => version.SessionId == sessionId &&
                           version.VersionNumber == versionNumber &&
                           !version.IsDeleted,
                cancellationToken)
            ?? throw new KeyNotFoundException("模板版本不存在。");
    }

    private async Task<RepositoryBranch?> ResolveBranchAsync(
        string repositoryId,
        string? branchId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            return await _context.RepositoryBranches
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    branch => branch.RepositoryId == repositoryId &&
                              branch.Id == branchId &&
                              !branch.IsDeleted,
                    cancellationToken)
                ?? throw new KeyNotFoundException("分支不存在。");
        }

        var branches = await _context.RepositoryBranches
            .AsNoTracking()
            .Where(branch => branch.RepositoryId == repositoryId && !branch.IsDeleted)
            .ToListAsync(cancellationToken);

        return branches
            .OrderBy(branch => GetBranchOrder(branch.BranchName))
            .ThenBy(branch => branch.BranchName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task<string> ResolveLanguageCodeAsync(
        string? branchId,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return string.IsNullOrWhiteSpace(languageCode) ? "zh" : languageCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var exists = await _context.BranchLanguages.AnyAsync(
                language => language.RepositoryBranchId == branchId &&
                            !language.IsDeleted &&
                            language.LanguageCode.ToLower() == languageCode.Trim().ToLower(),
                cancellationToken);
            if (!exists)
            {
                throw new KeyNotFoundException("语言不存在。");
            }

            return languageCode.Trim();
        }

        var branchLanguage = await _context.BranchLanguages
            .AsNoTracking()
            .Where(language => language.RepositoryBranchId == branchId && !language.IsDeleted)
            .OrderByDescending(language => language.IsDefault)
            .ThenBy(language => language.LanguageCode)
            .FirstOrDefaultAsync(cancellationToken);

        return branchLanguage?.LanguageCode ?? "zh";
    }

    private async Task<int> GetNextMessageSequenceAsync(string sessionId, CancellationToken cancellationToken)
    {
        var currentMax = await _context.WorkflowTemplateMessages
            .Where(message => message.SessionId == sessionId && !message.IsDeleted)
            .Select(message => (int?)message.SequenceNumber)
            .MaxAsync(cancellationToken);

        return (currentMax ?? 0) + 1;
    }

    private async Task<RepositoryWorkflowProfile?> GetCurrentDraftAsync(
        string sessionId,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        if (versionNumber <= 0)
        {
            return null;
        }

        var draftJson = await _context.WorkflowTemplateDraftVersions
            .AsNoTracking()
            .Where(version => version.SessionId == sessionId &&
                              version.VersionNumber == versionNumber &&
                              !version.IsDeleted)
            .Select(version => version.DraftJson)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(draftJson)
            ? null
            : DeserializeDraft(draftJson);
    }

    private static WorkflowTemplateSessionContextDto? DeserializeContext(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowTemplateSessionContextDto>(rawJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RepositoryWorkflowProfile DeserializeDraft(string rawJson)
    {
        try
        {
            return RepositoryWorkflowConfigRules.SanitizeProfile(
                JsonSerializer.Deserialize<RepositoryWorkflowProfile>(rawJson, JsonOptions) ?? new RepositoryWorkflowProfile());
        }
        catch (JsonException)
        {
            return new RepositoryWorkflowProfile();
        }
    }

    private static WorkflowTemplateDraftVersionDto MapVersion(WorkflowTemplateDraftVersion version)
    {
        return new WorkflowTemplateDraftVersionDto
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            BasedOnVersionNumber = version.BasedOnVersionNumber,
            SourceType = version.SourceType,
            ChangeSummary = version.ChangeSummary,
            RiskNotes = DeserializeStringList(version.RiskNotesJson),
            EvidenceFiles = DeserializeStringList(version.EvidenceFilesJson),
            ValidationIssues = DeserializeStringList(version.ValidationIssuesJson),
            Draft = DeserializeDraft(version.DraftJson),
            CreatedAt = version.CreatedAt
        };
    }

    private static List<string> DeserializeStringList(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static RepositoryWorkflowProfile CreateDefaultDraft()
    {
        return new RepositoryWorkflowProfile
        {
            Enabled = false,
            Mode = RepositoryWorkflowProfileMode.WcsRequestExecutor,
            Source = new RepositoryWorkflowProfileSource
            {
                Type = "ai-workbench-draft"
            },
            DocumentPreferences = new WorkflowDocumentPreferences()
        };
    }

    private static string? ResolveSessionTitle(string? currentTitle, string? suggestedTitle, string? draftName)
    {
        if (!string.IsNullOrWhiteSpace(suggestedTitle))
        {
            return suggestedTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(draftName))
        {
            return draftName.Trim();
        }

        return currentTitle;
    }

    private static int GetBranchOrder(string? branchName)
    {
        return branchName?.ToLowerInvariant() switch
        {
            "main" => 0,
            "master" => 1,
            "develop" => 2,
            "dev" => 3,
            _ => 99
        };
    }
}
