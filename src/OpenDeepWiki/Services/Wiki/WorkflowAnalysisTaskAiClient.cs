using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Services.Prompts;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisTaskAiClient
{
    Task<WorkflowAnalysisTaskAiResult> GenerateAsync(
        WorkflowAnalysisTaskAiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowAnalysisTaskAiRequest
{
    public string AnalysisSessionId { get; init; } = string.Empty;

    public string TaskId { get; init; } = string.Empty;

    public string TaskType { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int Depth { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<string> FocusSymbols { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkflowDeepAnalysisArtifactResult> PlannedArtifacts { get; init; } = [];
}

public sealed class WorkflowAnalysisTaskAiResult
{
    public string Summary { get; set; } = string.Empty;

    public string? LogMessage { get; set; }

    public string? ArtifactTitle { get; set; }

    public string MarkdownDraft { get; set; } = string.Empty;
}

public sealed class WorkflowAnalysisTaskAiClient : IWorkflowAnalysisTaskAiClient
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
    private readonly ILogger<WorkflowAnalysisTaskAiClient> _logger;

    public WorkflowAnalysisTaskAiClient(
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> wikiOptions,
        ILogger<WorkflowAnalysisTaskAiClient> logger)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _promptPlugin = promptPlugin ?? throw new ArgumentNullException(nameof(promptPlugin));
        _wikiOptions = wikiOptions?.Value ?? throw new ArgumentNullException(nameof(wikiOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowAnalysisTaskAiResult> GenerateAsync(
        WorkflowAnalysisTaskAiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = await _promptPlugin.LoadPromptAsync(
            "workflow-analysis-task-runner",
            new Dictionary<string, string>
            {
                ["analysis_session_id"] = request.AnalysisSessionId,
                ["task_id"] = request.TaskId,
                ["task_type"] = request.TaskType,
                ["task_title"] = request.Title,
                ["task_depth"] = request.Depth.ToString(),
                ["task_summary"] = request.Summary ?? string.Empty,
                ["focus_symbols_json"] = JsonSerializer.Serialize(request.FocusSymbols, JsonOptions),
                ["metadata_json"] = JsonSerializer.Serialize(request.Metadata, JsonOptions),
                ["planned_artifacts_json"] = JsonSerializer.Serialize(request.PlannedArtifacts, JsonOptions)
            },
            cancellationToken);

        var client = _agentFactory.CreateSimpleChatClient(
            _wikiOptions.ContentModel,
            maxToken: Math.Min(_wikiOptions.MaxOutputTokens, 8000),
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
            return WorkflowAnalysisTaskAiResponseParser.Parse(builder.ToString());
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to parse workflow analysis task AI response: {Content}", builder.ToString().Trim());
            throw new InvalidOperationException("AI 返回的任务结果结构无法解析。", ex);
        }
    }
}
