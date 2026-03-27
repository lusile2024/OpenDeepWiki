using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowRequiredSectionEnforcerTests
{
    [Fact]
    public void Enforce_ShouldAppendMissingRequiredSections()
    {
        const string content = """
# 工位入库流程

## 概览

已有内容。
""";

        var preferences = new WorkflowDocumentPreferences
        {
            RequiredSections =
            [
                "业务分支判断逻辑总览",
                "无库存场景处理逻辑"
            ]
        };

        var result = WorkflowRequiredSectionEnforcer.Enforce(content, preferences);

        Assert.Equal(2, result.MissingSections.Count);
        Assert.Contains("## 业务分支判断逻辑总览", result.Content);
        Assert.Contains("## 无库存场景处理逻辑", result.Content);
        Assert.Contains("当前自动生成结果未覆盖该章节", result.Content);
    }

    [Fact]
    public void Enforce_ShouldNotDuplicateExistingRequiredSectionsWithNumbering()
    {
        const string content = """
# 工位入库流程

## 1. 业务分支判断逻辑总览

已覆盖。
""";

        var preferences = new WorkflowDocumentPreferences
        {
            RequiredSections =
            [
                "业务分支判断逻辑总览"
            ]
        };

        var result = WorkflowRequiredSectionEnforcer.Enforce(content, preferences);

        Assert.Empty(result.MissingSections);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public void Enforce_ShouldReturnOriginalContentWhenRequiredSectionsAreEmpty()
    {
        const string content = """
# 工位入库流程

## 概览
""";

        var result = WorkflowRequiredSectionEnforcer.Enforce(content, new WorkflowDocumentPreferences());

        Assert.Empty(result.MissingSections);
        Assert.Equal(content, result.Content);
    }
}
