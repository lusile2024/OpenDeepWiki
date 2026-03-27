using System.Reflection;
using OpenDeepWiki.Services.Overlays;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Overlays;

public class OverlayWikiServiceCappingTests
{
    [Fact]
    public void CapIndex_ShouldMarkPreviewAsCapped_WhenTotalFilesExceedMaxFiles()
    {
        var index = new OverlayIndex
        {
            ProfileKey = "1282",
            ProfileName = "1282 Overlay",
            BaseBranchName = "main",
            Overrides =
            [
                new OverlayOverrideItem
                {
                    VariantKey = "1282",
                    VariantName = "1282项目定制",
                    ProjectPath = "src/App/1282/OrderService.cs",
                    BasePath = "src/App/OrderService.cs",
                    DisplayPath = "src/App/OrderService.cs"
                },
                new OverlayOverrideItem
                {
                    VariantKey = "1282",
                    VariantName = "1282项目定制",
                    ProjectPath = "src/App/1282/StockService.cs",
                    BasePath = "src/App/StockService.cs",
                    DisplayPath = "src/App/StockService.cs"
                }
            ],
            Added =
            [
                new OverlayAddedItem
                {
                    VariantKey = "1282",
                    VariantName = "1282项目定制",
                    ProjectPath = "src/App/1282/NewService.cs",
                    DisplayPath = "src/App/NewService.cs"
                }
            ]
        };

        var capped = InvokeCapIndex(index, maxFiles: 2);

        Assert.True(capped.IsCapped);
        Assert.Equal(2, capped.MaxFilesApplied);
        Assert.NotNull(capped.UncappedSummary);
        Assert.Equal(2, capped.Summary.TotalCount);
        Assert.Equal(3, capped.UncappedSummary!.TotalCount);
        Assert.Equal(2, capped.UncappedSummary.OverrideCount);
        Assert.Equal(1, capped.UncappedSummary.AddedCount);
    }

    [Fact]
    public void CapIndex_ShouldKeepPreviewAsUncapped_WhenTotalFilesDoNotExceedMaxFiles()
    {
        var index = new OverlayIndex
        {
            ProfileKey = "1282",
            ProfileName = "1282 Overlay",
            BaseBranchName = "main",
            Overrides =
            [
                new OverlayOverrideItem
                {
                    VariantKey = "1282",
                    VariantName = "1282项目定制",
                    ProjectPath = "src/App/1282/OrderService.cs",
                    BasePath = "src/App/OrderService.cs",
                    DisplayPath = "src/App/OrderService.cs"
                }
            ]
        };

        var capped = InvokeCapIndex(index, maxFiles: 5);

        Assert.False(capped.IsCapped);
        Assert.Equal(5, capped.MaxFilesApplied);
        Assert.Null(capped.UncappedSummary);
        Assert.Equal(1, capped.Summary.TotalCount);
    }

    [Fact]
    public void SlugifyLeaf_ShouldProduceDifferentSlugs_WhenSameDisplayPathHasDifferentSourceFiles()
    {
        var method = typeof(OverlayWikiService).GetMethod(
            "SlugifyLeaf",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var slugA = Assert.IsType<string>(method!.Invoke(null, ["DeliveringRecordService.cs", "src/App/MoveOut/DeliveringRecordService.cs|src/App/1282/MoveOut/DeliveringRecordService1282.cs"]));
        var slugB = Assert.IsType<string>(method.Invoke(null, ["DeliveringRecordService.cs", "src/App/MoveOut/DeliveringRecordService.cs|src/App/1282/Custom/MoveOut/DeliveringRecordService1282.cs"]));

        Assert.NotEqual(slugA, slugB);
    }

    private static OverlayIndex InvokeCapIndex(OverlayIndex index, int maxFiles)
    {
        var method = typeof(OverlayWikiService).GetMethod(
            "CapIndex",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<OverlayIndex>(method!.Invoke(null, [index, maxFiles]));
    }
}
