using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Prompts;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowTemplateWorkbenchAiClient
{
    Task<WorkflowTemplateWorkbenchAiResult> GenerateDraftAsync(
        WorkflowTemplateWorkbenchAiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowTemplateWorkbenchAiRequest
{
    public string RepositoryName { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string LanguageCode { get; init; } = "zh";

    public RepositoryWorkflowConfig ExistingConfig { get; init; } = new();

    public RepositoryWorkflowProfile CurrentDraft { get; init; } = new();

    public WorkflowTemplateSessionContextDto? Context { get; init; }

    public IReadOnlyList<WorkflowTemplateConversationTurn> History { get; init; } = [];

    public string UserMessage { get; init; } = string.Empty;
}

public sealed class WorkflowTemplateConversationTurn
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public int? VersionNumber { get; init; }

    public string? ChangeSummary { get; init; }

    public DateTime Timestamp { get; init; }
}

public sealed class WorkflowTemplateWorkbenchAiResult
{
    public string? Title { get; set; }

    public string? AssistantMessage { get; set; }

    public string? ChangeSummary { get; set; }

    public List<string> RiskNotes { get; set; } = [];

    public List<string> EvidenceFiles { get; set; } = [];

    public RepositoryWorkflowProfile UpdatedDraft { get; set; } = new();
}

public sealed class WorkflowTemplateWorkbenchAiClient : IWorkflowTemplateWorkbenchAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AgentFactory _agentFactory;
    private readonly IPromptPlugin _promptPlugin;
    private readonly WikiGeneratorOptions _wikiOptions;
    private readonly ILogger<WorkflowTemplateWorkbenchAiClient> _logger;

    public WorkflowTemplateWorkbenchAiClient(
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> wikiOptions,
        ILogger<WorkflowTemplateWorkbenchAiClient> logger)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _promptPlugin = promptPlugin ?? throw new ArgumentNullException(nameof(promptPlugin));
        _wikiOptions = wikiOptions?.Value ?? throw new ArgumentNullException(nameof(wikiOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowTemplateWorkbenchAiResult> GenerateDraftAsync(
        WorkflowTemplateWorkbenchAiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = await _promptPlugin.LoadPromptAsync(
            "workflow-template-workbench",
            new Dictionary<string, string>
            {
                ["repository_name"] = request.RepositoryName,
                ["branch_name"] = request.BranchName,
                ["language"] = request.LanguageCode,
                ["existing_config_json"] = JsonSerializer.Serialize(request.ExistingConfig, JsonOptions),
                ["current_draft_json"] = JsonSerializer.Serialize(request.CurrentDraft, JsonOptions),
                ["session_context_json"] = JsonSerializer.Serialize(request.Context ?? new WorkflowTemplateSessionContextDto(), JsonOptions),
                ["conversation_history_json"] = JsonSerializer.Serialize(request.History, JsonOptions),
                ["user_message"] = request.UserMessage
            },
            cancellationToken);

        var client = _agentFactory.CreateSimpleChatClient(
            _wikiOptions.ContentModel,
            maxToken: _wikiOptions.MaxOutputTokens,
            requestOptions: _wikiOptions.GetContentRequestOptions());
        var session = await client.CreateSessionAsync(cancellationToken);
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, prompt)
        };

        var builder = new StringBuilder();
        await foreach (var update in client.RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                builder.Append(update.Text);
            }
        }

        try
        {
            return WorkflowTemplateWorkbenchAiResponseParser.Parse(builder.ToString());
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to parse workflow template AI response: {Content}", builder.ToString().Trim());
            throw new InvalidOperationException("AI 返回的模板结构无法解析。", ex);
        }
    }
}
