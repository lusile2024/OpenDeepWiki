using OpenDeepWiki.Infrastructure;
using Xunit;

namespace OpenDeepWiki.Tests.Infrastructure;

public class SqliteLegacyMigrationPlannerTests
{
    [Fact]
    public void GetBaselineMigrationIds_WithoutExistingTables_ReturnsEmpty()
    {
        var result = SqliteLegacyMigrationPlanner.GetBaselineMigrationIds(
            [
                "20260223135846_Initial",
                "20260325014032_AddDocTopicContexts"
            ],
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void GetBaselineMigrationIds_WithLegacySchemaWithoutTopicContext_OnlyMarksInitial()
    {
        var result = SqliteLegacyMigrationPlanner.GetBaselineMigrationIds(
            [
                "20260223135846_Initial",
                "20260325014032_AddDocTopicContexts"
            ],
            ["Users", "Repositories", "DocCatalogs"]);

        Assert.Equal(["20260223135846_Initial"], result);
    }

    [Fact]
    public void GetBaselineMigrationIds_WithTopicContextTable_MarksKnownFollowupMigration()
    {
        var result = SqliteLegacyMigrationPlanner.GetBaselineMigrationIds(
            [
                "20260223135846_Initial",
                "20260325014032_AddDocTopicContexts"
            ],
            ["Users", "Repositories", "DocTopicContexts"]);

        Assert.Equal(
            [
                "20260223135846_Initial",
                "20260325014032_AddDocTopicContexts"
            ],
            result);
    }
}
