using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Services.Prompts;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisPlannerHintAiClient
{
    Task<WorkflowAnalysisPlannerHintAiResult> GenerateAsync(
        WorkflowAnalysisPlannerHintAiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowAnalysisPlannerHintAiClient : IWorkflowAnalysisPlannerHintAiClient
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
    private readonly ILogger<WorkflowAnalysisPlannerHintAiClient> _logger;

    public WorkflowAnalysisPlannerHintAiClient(
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> wikiOptions,
        ILogger<WorkflowAnalysisPlannerHintAiClient> logger)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _promptPlugin = promptPlugin ?? throw new ArgumentNullException(nameof(promptPlugin));
        _wikiOptions = wikiOptions?.Value ?? throw new ArgumentNullException(nameof(wikiOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowAnalysisPlannerHintAiResult> GenerateAsync(
        WorkflowAnalysisPlannerHintAiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = await _promptPlugin.LoadPromptAsync(
            "workflow-analysis-planner-hint",
            new Dictionary<string, string>
            {
                ["analysis_session_id"] = request.AnalysisSessionId,
                ["profile_key"] = request.ProfileKey,
                ["language"] = request.LanguageCode,
                ["objective"] = request.Objective ?? request.Profile.Acp.Objective ?? string.Empty,
                ["profile_json"] = JsonSerializer.Serialize(request.Profile, JsonOptions),
                ["chapter_slices_json"] = JsonSerializer.Serialize(request.ChapterSlices, JsonOptions),
                ["existing_tasks_json"] = JsonSerializer.Serialize(request.ExistingTasks, JsonOptions),
                ["remaining_branch_capacity_json"] = JsonSerializer.Serialize(request.RemainingBranchCapacityByChapter, JsonOptions)
            },
            cancellationToken);

        var client = _agentFactory.CreateSimpleChatClient(
            _wikiOptions.ContentModel,
            maxToken: Math.Min(_wikiOptions.MaxOutputTokens, 4000),
            requestOptions: _wikiOptions.GetContentRequestOptions());

        var session = await client.CreateSessionAsync(cancellationToken);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
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
            return WorkflowAnalysisPlannerHintAiResponseParser.Parse(builder.ToString());
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to parse workflow analysis planner hint AI response: {Content}", builder.ToString().Trim());
            throw new InvalidOperationException("AI 返回的 planner hint 结构无法解析。", ex);
        }
    }
}
