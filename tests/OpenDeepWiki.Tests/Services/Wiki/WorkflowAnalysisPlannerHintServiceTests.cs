using Moq;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowAnalysisPlannerHintServiceTests
{
    [Fact]
    public async Task GenerateHintsAsync_ShouldNormalizeFocusSymbols_AndDropInvalidSuggestions()
    {
        var aiClient = new Mock<IWorkflowAnalysisPlannerHintAiClient>(MockBehavior.Strict);
        aiClient
            .Setup(item => item.GenerateAsync(
                It.IsAny<WorkflowAnalysisPlannerHintAiRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowAnalysisPlannerHintAiResult
            {
                Summary = "补充了一个有效建议",
                SuggestedBranchTasks =
                [
                    new WorkflowAnalysisPlannerHintSuggestion
                    {
                        ChapterKey = "allocation",
                        BranchRoot = "ValidateDoubleDeep",
                        FocusSymbols = ["ValidateDoubleDeep", "LoadCandidateBins", "ValidateDoubleDeep", ""],
                        Summary = "  补双深校验  ",
                        Reason = "  hotspot  "
                    },
                    new WorkflowAnalysisPlannerHintSuggestion
                    {
                        ChapterKey = "",
                        BranchRoot = "InvalidSuggestion",
                        FocusSymbols = ["InvalidSuggestion"],
                        Summary = "无效",
                        Reason = "无效"
                    }
                ]
            });

        var service = new WorkflowAnalysisPlannerHintService(aiClient.Object);

        var result = await service.GenerateHintsAsync(new WorkflowAnalysisPlannerHintRequest
        {
            AnalysisSessionId = "session-1",
            ProfileKey = "wcs-stn-move-in",
            Profile = new RepositoryWorkflowProfile(),
            ChapterSlices = [],
            ExistingTasks = []
        });

        Assert.Equal("补充了一个有效建议", result.Summary);
        var suggestion = Assert.Single(result.SuggestedBranchTasks);
        Assert.Equal("allocation", suggestion.ChapterKey);
        Assert.Equal("ValidateDoubleDeep", suggestion.BranchRoot);
        Assert.Equal(["ValidateDoubleDeep", "LoadCandidateBins"], suggestion.FocusSymbols);
        Assert.Equal("补双深校验", suggestion.Summary);
        Assert.Equal("hotspot", suggestion.Reason);
    }
}
