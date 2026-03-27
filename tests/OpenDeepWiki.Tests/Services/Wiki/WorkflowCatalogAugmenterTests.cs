using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowCatalogAugmenterTests
{
    [Fact]
    public void Merge_ShouldCreateBusinessWorkflowFolderWhenMissing()
    {
        var augmenter = new WorkflowCatalogAugmenter();
        var root = new CatalogRoot
        {
            Items =
            [
                new CatalogItem { Title = "概览", Path = "overview", Order = 0 }
            ]
        };

        var merged = augmenter.Merge(
            root,
            [
                new WorkflowTopicCandidate
                {
                    Key = "container-pallet-inbound",
                    Name = "容器托盘入库流程"
                }
            ]);

        var workflowRoot = Assert.Single(merged.Items, item => item.Path == "business-workflows");
        Assert.Equal("核心业务流程", workflowRoot.Title);
        Assert.Equal(90, workflowRoot.Order);
        var child = Assert.Single(workflowRoot.Children);
        Assert.Equal("容器托盘入库流程", child.Title);
        Assert.Equal("business-workflows/container-pallet-inbound", child.Path);
    }

    [Fact]
    public void Merge_ShouldAppendWorkflowLeavesInStableOrderAndKeepOtherNodesUnchanged()
    {
        var augmenter = new WorkflowCatalogAugmenter();
        var root = new CatalogRoot
        {
            Items =
            [
                new CatalogItem { Title = "概览", Path = "overview", Order = 0 },
                new CatalogItem { Title = "系统架构", Path = "architecture", Order = 10 }
            ]
        };

        var merged = augmenter.Merge(
            root,
            [
                new WorkflowTopicCandidate { Key = "inventory-inbound", Name = "库存入库流程" },
                new WorkflowTopicCandidate { Key = "container-pallet-inbound", Name = "容器托盘入库流程" }
            ]);

        Assert.Equal(3, merged.Items.Count);
        Assert.Equal("概览", merged.Items[0].Title);
        Assert.Equal("系统架构", merged.Items[1].Title);

        var workflowRoot = merged.Items[2];
        Assert.Equal("核心业务流程", workflowRoot.Title);
        Assert.Equal(
            ["business-workflows/container-pallet-inbound", "business-workflows/inventory-inbound"],
            workflowRoot.Children.Select(child => child.Path).ToArray());
    }
}
