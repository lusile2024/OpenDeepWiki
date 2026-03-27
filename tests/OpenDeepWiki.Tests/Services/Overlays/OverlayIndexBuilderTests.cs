using OpenDeepWiki.Services.Overlays;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Overlays;

public class OverlayIndexBuilderTests
{
    [Fact]
    public void Build_ShouldTreatFileNameVariantSuffixAsOverride_WhenBaseFileExists()
    {
        var workspace = CreateTempDirectory();
        WriteFile(workspace, "src/App/Services/OrderService.cs", "public class OrderService {}");
        WriteFile(workspace, "src/App/1282/Services/OrderService1282.cs", "public class OrderService1282 : OrderService {}");

        var builder = new OverlayIndexBuilder();

        var index = builder.Build(workspace, CreateProfile());

        var item = Assert.Single(index.Overrides);
        Assert.Equal("src/App/1282/Services/OrderService1282.cs", item.ProjectPath);
        Assert.Equal("src/App/Services/OrderService.cs", item.BasePath);
        Assert.Empty(index.Added);
    }

    [Fact]
    public void Build_ShouldTreatPartialOrOverrideFileAsOverride_WhenMappedBaseFileDoesNotExist()
    {
        var workspace = CreateTempDirectory();
        WriteFile(
            workspace,
            "src/App/1282/Services/OrderServiceExtension.cs",
            """
            public partial class OrderService
            {
                public override string ToString() => base.ToString();
            }
            """);

        var builder = new OverlayIndexBuilder();

        var index = builder.Build(workspace, CreateProfile());

        var item = Assert.Single(index.Overrides);
        Assert.Equal("src/App/1282/Services/OrderServiceExtension.cs", item.ProjectPath);
        Assert.Equal("src/App/Services/OrderServiceExtension.cs", item.BasePath);
        Assert.Empty(index.Added);
    }

    [Fact]
    public void Build_ShouldTreatInheritedBaseTypeMatchAsOverride_WhenBaseFileLivesOutsideCustomDirectory()
    {
        var workspace = CreateTempDirectory();
        WriteFile(workspace, "src/App/MoveOut/DeliveringRecordService.cs", "public class DeliveringRecordService {}");
        WriteFile(
            workspace,
            "src/App/1282/Custom/MoveOut/DeliveringRecordService1282.cs",
            "public class DeliveringRecordCusService1282 : DeliveringRecordService {}");

        var builder = new OverlayIndexBuilder();

        var index = builder.Build(workspace, CreateProfile());

        var item = Assert.Single(index.Overrides);
        Assert.Equal("src/App/1282/Custom/MoveOut/DeliveringRecordService1282.cs", item.ProjectPath);
        Assert.Equal("src/App/MoveOut/DeliveringRecordService.cs", item.BasePath);
        Assert.Empty(index.Added);
    }

    [Fact]
    public void Build_ShouldKeepGenericFrameworkInheritanceAsAdded_WhenNoConcreteBaseFileMatches()
    {
        var workspace = CreateTempDirectory();
        WriteFile(workspace, "src/App/ApplicationService.cs", "public class ApplicationService {}");
        WriteFile(
            workspace,
            "src/App/1282/Custom/Product/ProductLineService.cs",
            "public class ProductLineService : ApplicationService {}");

        var builder = new OverlayIndexBuilder();

        var index = builder.Build(workspace, CreateProfile());

        var item = Assert.Single(index.Added);
        Assert.Equal("src/App/1282/Custom/Product/ProductLineService.cs", item.ProjectPath);
        Assert.Equal("src/App/Custom/Product/ProductLineService.cs", item.DisplayPath);
        Assert.Empty(index.Overrides);
    }

    private static OverlayProfile CreateProfile()
    {
        return new OverlayProfile
        {
            Key = "1282",
            Name = "1282 Overlay",
            BaseBranchName = "main",
            Roots = ["src/App"],
            Variants =
            [
                new OverlayVariant
                {
                    Key = "1282",
                    DetectionMode = OverlayVariantDetectionMode.PathSegmentEquals
                }
            ],
            MappingRules =
            [
                new OverlayMappingRule
                {
                    Type = OverlayMappingRuleType.RemoveVariantSegment
                }
            ]
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
