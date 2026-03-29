using System.Text.Json.Serialization;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowAnalysisPlannerHintRequest
{
    public string AnalysisSessionId { get; init; } = string.Empty;

    public string ProfileKey { get; init; } = string.Empty;

    public string LanguageCode { get; init; } = "zh";

    public string? Objective { get; init; }

    public RepositoryWorkflowProfile Profile { get; init; } = new();

    public IReadOnlyList<WorkflowChapterSlice> ChapterSlices { get; init; } = [];

    public IReadOnlyList<WorkflowDeepAnalysisTaskResult> ExistingTasks { get; init; } = [];

    public IReadOnlyDictionary<string, int> RemainingBranchCapacityByChapter { get; init; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkflowAnalysisPlannerHintResult
{
    public string Summary { get; set; } = string.Empty;

    public List<WorkflowAnalysisPlannerHintSuggestion> SuggestedBranchTasks { get; set; } = [];
}

public sealed class WorkflowAnalysisPlannerHintSuggestion
{
    public string ChapterKey { get; set; } = string.Empty;

    public string BranchRoot { get; set; } = string.Empty;

    public List<string> FocusSymbols { get; set; } = [];

    public string Summary { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public sealed class WorkflowAnalysisPlannerHintAiRequest
{
    public string AnalysisSessionId { get; init; } = string.Empty;

    public string ProfileKey { get; init; } = string.Empty;

    public string LanguageCode { get; init; } = "zh";

    public string? Objective { get; init; }

    public RepositoryWorkflowProfile Profile { get; init; } = new();

    public IReadOnlyList<WorkflowChapterSlice> ChapterSlices { get; init; } = [];

    public IReadOnlyList<WorkflowDeepAnalysisTaskResult> ExistingTasks { get; init; } = [];

    public IReadOnlyDictionary<string, int> RemainingBranchCapacityByChapter { get; init; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkflowAnalysisPlannerHintAiResult
{
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("suggestedBranchTasks")]
    public List<WorkflowAnalysisPlannerHintSuggestion> SuggestedBranchTasks { get; set; } = [];
}
