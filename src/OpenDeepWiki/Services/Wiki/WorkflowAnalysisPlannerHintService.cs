namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowAnalysisPlannerHintService
{
    Task<WorkflowAnalysisPlannerHintResult> GenerateHintsAsync(
        WorkflowAnalysisPlannerHintRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowAnalysisPlannerHintService : IWorkflowAnalysisPlannerHintService
{
    private readonly IWorkflowAnalysisPlannerHintAiClient _aiClient;

    public WorkflowAnalysisPlannerHintService(IWorkflowAnalysisPlannerHintAiClient aiClient)
    {
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
    }

    public async Task<WorkflowAnalysisPlannerHintResult> GenerateHintsAsync(
        WorkflowAnalysisPlannerHintRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var aiResult = await _aiClient.GenerateAsync(
            new WorkflowAnalysisPlannerHintAiRequest
            {
                AnalysisSessionId = request.AnalysisSessionId,
                ProfileKey = request.ProfileKey,
                LanguageCode = request.LanguageCode,
                Objective = request.Objective,
                Profile = request.Profile,
                ChapterSlices = request.ChapterSlices,
                ExistingTasks = request.ExistingTasks,
                RemainingBranchCapacityByChapter = request.RemainingBranchCapacityByChapter
            },
            cancellationToken);

        return new WorkflowAnalysisPlannerHintResult
        {
            Summary = aiResult.Summary.Trim(),
            SuggestedBranchTasks = aiResult.SuggestedBranchTasks
                .Select(NormalizeSuggestion)
                .Where(static item => item is not null)
                .Cast<WorkflowAnalysisPlannerHintSuggestion>()
                .ToList()
        };
    }

    private static WorkflowAnalysisPlannerHintSuggestion? NormalizeSuggestion(
        WorkflowAnalysisPlannerHintSuggestion suggestion)
    {
        var chapterKey = suggestion.ChapterKey?.Trim();
        var branchRoot = suggestion.BranchRoot?.Trim();
        if (string.IsNullOrWhiteSpace(chapterKey) || string.IsNullOrWhiteSpace(branchRoot))
        {
            return null;
        }

        var focusSymbols = new List<string> { branchRoot };
        if (suggestion.FocusSymbols.Count > 0)
        {
            focusSymbols.AddRange(suggestion.FocusSymbols);
        }

        return new WorkflowAnalysisPlannerHintSuggestion
        {
            ChapterKey = chapterKey,
            BranchRoot = branchRoot,
            FocusSymbols = focusSymbols
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList(),
            Summary = suggestion.Summary?.Trim() ?? string.Empty,
            Reason = suggestion.Reason?.Trim() ?? string.Empty
        };
    }
}
