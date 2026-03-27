using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.Agents;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Overlays;
using OpenDeepWiki.Services.Prompts;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Overlays;

public class OverlaySuggestionServiceTests
{
    [Fact]
    public void AnalyzeWorkspace_ShouldCountVariantSuffixAndPartialOverrideFilesAsOverrides()
    {
        var workspace = CreateTempDirectory();
        WriteFile(workspace, "src/App/Services/OrderService.cs", "public class OrderService {}");
        WriteFile(workspace, "src/App/1282/Services/OrderService1282.cs", "public class OrderService1282 : OrderService {}");
        WriteFile(
            workspace,
            "src/App/1282/Services/OrderServiceExtension.cs",
            """
            public partial class OrderService
            {
                public override string ToString() => base.ToString();
            }
            """);

        var service = new OverlaySuggestionService(
            Mock.Of<IContext>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IAdminRepositoryOverlayService>(),
            new AgentFactory(Options.Create(new AiRequestOptions())),
            Mock.Of<IPromptPlugin>(),
            Options.Create(new WikiGeneratorOptions()),
            NullLogger<OverlaySuggestionService>.Instance);

        var method = typeof(OverlaySuggestionService).GetMethod(
            "AnalyzeWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var analysis = method!.Invoke(service, [workspace, "C#", 3, 8]);
        Assert.NotNull(analysis);

        var toResponse = analysis!.GetType().GetMethod("ToResponse", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(toResponse);

        var response = Assert.IsType<OverlayRepositoryStructureAnalysis>(toResponse!.Invoke(analysis, null));
        var candidate = Assert.Single(response.VariantCandidates);

        Assert.Equal("1282", candidate.Key);
        Assert.Equal(2, candidate.OverrideCount);
        Assert.Equal(0, candidate.AddedCount);
        Assert.Contains(candidate.OverrideSamples, sample =>
            sample.ProjectPath == "src/App/1282/Services/OrderService1282.cs" &&
            sample.BasePath == "src/App/Services/OrderService.cs");
        Assert.Contains(candidate.OverrideSamples, sample =>
            sample.ProjectPath == "src/App/1282/Services/OrderServiceExtension.cs");
    }

    [Fact]
    public void AnalyzeWorkspace_ShouldTreatInheritedConcreteBaseTypeMatchAsOverride_ButKeepGenericBaseInheritanceAsAdded()
    {
        var workspace = CreateTempDirectory();

        WriteFile(workspace, "src/App/MoveOut/DeliveringRecordService.cs", "public class DeliveringRecordService {}");
        WriteFile(
            workspace,
            "src/App/1282/Custom/MoveOut/DeliveringRecordService1282.cs",
            "public class DeliveringRecordCusService1282 : DeliveringRecordService {}");

        WriteFile(workspace, "src/App/ApplicationService.cs", "public class ApplicationService {}");
        WriteFile(
            workspace,
            "src/App/1282/Custom/Product/ProductLineService.cs",
            "public class ProductLineService : ApplicationService {}");

        var service = new OverlaySuggestionService(
            Mock.Of<IContext>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IAdminRepositoryOverlayService>(),
            new AgentFactory(Options.Create(new AiRequestOptions())),
            Mock.Of<IPromptPlugin>(),
            Options.Create(new WikiGeneratorOptions()),
            NullLogger<OverlaySuggestionService>.Instance);

        var method = typeof(OverlaySuggestionService).GetMethod(
            "AnalyzeWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var analysis = method!.Invoke(service, [workspace, "C#", 3, 8]);
        Assert.NotNull(analysis);

        var toResponse = analysis!.GetType().GetMethod("ToResponse", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(toResponse);

        var response = Assert.IsType<OverlayRepositoryStructureAnalysis>(toResponse!.Invoke(analysis, null));
        var candidate = Assert.Single(response.VariantCandidates);

        Assert.Equal("1282", candidate.Key);
        Assert.Equal(1, candidate.OverrideCount);
        Assert.Equal(1, candidate.AddedCount);
        Assert.Contains(candidate.OverrideSamples, sample =>
            sample.ProjectPath == "src/App/1282/Custom/MoveOut/DeliveringRecordService1282.cs" &&
            sample.BasePath == "src/App/MoveOut/DeliveringRecordService.cs");
        Assert.Contains(candidate.AddedSamples, sample =>
            sample.ProjectPath == "src/App/1282/Custom/Product/ProductLineService.cs");
    }

    [Fact]
    public void AnalyzeWorkspace_ShouldPreferProjectCodeVariantOverBusinessDirectorySegments()
    {
        var workspace = CreateTempDirectory();

        WriteFile(workspace, "src/App/Inventory/LotDisableJob.cs", "public class LotDisableJob {}");
        WriteFile(workspace, "src/App/LotDisableJob.cs", "public class LotDisableJobBase {}");
        WriteFile(workspace, "src/App/Inventory/StockLimitMessageJob.cs", "public class StockLimitMessageJob {}");
        WriteFile(workspace, "src/App/StockLimitMessageJob.cs", "public class StockLimitMessageJobBase {}");
        WriteFile(workspace, "src/App/Inventory/InventoryWarningJob.cs", "public class InventoryWarningJob {}");
        WriteFile(workspace, "src/App/InventoryWarningJob.cs", "public class InventoryWarningJobBase {}");

        WriteFile(workspace, "src/App/BarcodeManage/Barcode1Service.cs", "public class Barcode1Service {}");
        WriteFile(workspace, "src/App/1282/BarcodeManage/Barcode1Service1282.cs", "public class Barcode1Service1282 : Barcode1Service {}");
        WriteFile(workspace, "src/App/BasicManage/MaterialService.cs", "public class MaterialService {}");
        WriteFile(workspace, "src/App/1282/BasicManage/MaterialService1282.cs", "public class MaterialService1282 : MaterialService {}");

        var service = new OverlaySuggestionService(
            Mock.Of<IContext>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IAdminRepositoryOverlayService>(),
            new AgentFactory(Options.Create(new AiRequestOptions())),
            Mock.Of<IPromptPlugin>(),
            Options.Create(new WikiGeneratorOptions()),
            NullLogger<OverlaySuggestionService>.Instance);

        var method = typeof(OverlaySuggestionService).GetMethod(
            "AnalyzeWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var analysis = method!.Invoke(service, [workspace, "C#", 3, 8]);
        Assert.NotNull(analysis);

        var toResponse = analysis!.GetType().GetMethod("ToResponse", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(toResponse);

        var response = Assert.IsType<OverlayRepositoryStructureAnalysis>(toResponse!.Invoke(analysis, null));
        var candidate = Assert.Single(response.VariantCandidates);

        Assert.Equal("1282", candidate.Key);
        Assert.Equal(2, candidate.OverrideCount);
        Assert.Equal(0, candidate.AddedCount);
    }

    [Fact]
    public void AnalyzeWorkspace_ShouldIgnoreBusinessSegmentCandidates_WhenNumericVariantExists()
    {
        var workspace = CreateTempDirectory();

        WriteFile(workspace, "src/App/Inventory/LotDisableJob.cs", "public class LotDisableJob {}");
        WriteFile(workspace, "src/App/LotDisableJob.cs", "public class LotDisableJobBase {}");

        WriteFile(
            workspace,
            "src/App/1282/Inventory/OrderServiceExtension.cs",
            """
            public partial class OrderService
            {
                public override string ToString() => base.ToString();
            }
            """);

        var service = new OverlaySuggestionService(
            Mock.Of<IContext>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IAdminRepositoryOverlayService>(),
            new AgentFactory(Options.Create(new AiRequestOptions())),
            Mock.Of<IPromptPlugin>(),
            Options.Create(new WikiGeneratorOptions()),
            NullLogger<OverlaySuggestionService>.Instance);

        var method = typeof(OverlaySuggestionService).GetMethod(
            "AnalyzeWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var analysis = method!.Invoke(service, [workspace, "C#", 3, 8]);
        Assert.NotNull(analysis);

        var toResponse = analysis!.GetType().GetMethod("ToResponse", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(toResponse);

        var response = Assert.IsType<OverlayRepositoryStructureAnalysis>(toResponse!.Invoke(analysis, null));
        var candidate = Assert.Single(response.VariantCandidates);

        Assert.Equal("1282", candidate.Key);
        Assert.DoesNotContain(response.VariantCandidates, item => item.Key == "Inventory");
    }

    [Fact]
    public void AnalyzeWorkspace_ShouldNotTreatBarcodeDirectoryAsExplicitVariant_WhenOnlySingleDigitSuffixExists()
    {
        var workspace = CreateTempDirectory();

        WriteFile(workspace, "src/App/BarcodeManage/Barcode2/Barcode2Dto.cs", "public class Barcode2Dto {}");
        WriteFile(workspace, "src/App/BarcodeManage/Dto.cs", "public class Dto {}");
        WriteFile(workspace, "src/App/1282/BarcodeManage/Barcode1Service1282.cs", "public class Barcode1Service1282 : Barcode1Service {}");
        WriteFile(workspace, "src/App/BarcodeManage/Barcode1Service.cs", "public class Barcode1Service {}");

        var service = new OverlaySuggestionService(
            Mock.Of<IContext>(),
            Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IAdminRepositoryOverlayService>(),
            new AgentFactory(Options.Create(new AiRequestOptions())),
            Mock.Of<IPromptPlugin>(),
            Options.Create(new WikiGeneratorOptions()),
            NullLogger<OverlaySuggestionService>.Instance);

        var method = typeof(OverlaySuggestionService).GetMethod(
            "AnalyzeWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var analysis = method!.Invoke(service, [workspace, "C#", 3, 8]);
        Assert.NotNull(analysis);

        var toResponse = analysis!.GetType().GetMethod("ToResponse", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(toResponse);

        var response = Assert.IsType<OverlayRepositoryStructureAnalysis>(toResponse!.Invoke(analysis, null));

        Assert.Single(response.VariantCandidates);
        Assert.Equal("1282", response.VariantCandidates[0].Key);
        Assert.DoesNotContain(response.VariantCandidates, item => item.Key == "Barcode2");
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
