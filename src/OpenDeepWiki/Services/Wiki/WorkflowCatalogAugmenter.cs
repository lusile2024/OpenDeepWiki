using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowCatalogAugmenter
{
    private const string WorkflowRootTitle = "核心业务流程";
    private const string WorkflowRootPath = "business-workflows";
    private const int WorkflowRootOrder = 90;

    public CatalogRoot Merge(CatalogRoot root, IReadOnlyCollection<WorkflowTopicCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidates);

        var clonedRoot = CloneRoot(root);
        clonedRoot.Items = clonedRoot.Items
            .Where(item => item.Path != WorkflowRootPath)
            .ToList();

        if (candidates.Count > 0)
        {
            var workflowRoot = new CatalogItem
            {
                Title = WorkflowRootTitle,
                Path = WorkflowRootPath,
                Order = WorkflowRootOrder
            };
            clonedRoot.Items.Add(workflowRoot);
            workflowRoot.Children = candidates
                .OrderBy(candidate => candidate.Key, StringComparer.Ordinal)
                .Select((candidate, index) => new CatalogItem
                {
                    Title = candidate.Name,
                    Path = $"{WorkflowRootPath}/{candidate.Key}",
                    Order = index,
                    Children = []
                })
                .ToList();
        }

        clonedRoot.Items = clonedRoot.Items
            .OrderBy(item => item.Order)
            .ToList();

        return clonedRoot;
    }

    private static CatalogRoot CloneRoot(CatalogRoot root)
    {
        return new CatalogRoot
        {
            Items = root.Items.Select(CloneItem).ToList()
        };
    }

    private static CatalogItem CloneItem(CatalogItem item)
    {
        return new CatalogItem
        {
            Title = item.Title,
            Path = item.Path,
            Order = item.Order,
            Children = item.Children.Select(CloneItem).ToList()
        };
    }
}
