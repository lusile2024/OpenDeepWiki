using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace OpenDeepWiki.Infrastructure;

/// <summary>
/// 为历史 sqlite 库补录缺失的迁移历史，避免从 EnsureCreated 迁移到 Migrate 时重复执行初始迁移。
/// </summary>
public static class SqliteLegacyMigrationPlanner
{
    public static IReadOnlyList<string> GetBaselineMigrationIds(
        IEnumerable<string> migrations,
        IEnumerable<string> existingTables)
    {
        var migrationList = migrations.ToList();
        var tableSet = new HashSet<string>(existingTables, StringComparer.OrdinalIgnoreCase);

        if (migrationList.Count == 0 || tableSet.Count == 0)
        {
            return [];
        }

        var baseline = new List<string>();
        AddIfPresent(
            baseline,
            migrationList.FirstOrDefault(migrationId =>
                migrationId.EndsWith("_Initial", StringComparison.OrdinalIgnoreCase)));

        if (tableSet.Contains("DocTopicContexts"))
        {
            AddIfPresent(
                baseline,
                migrationList.FirstOrDefault(migrationId =>
                    migrationId.EndsWith("_AddDocTopicContexts", StringComparison.OrdinalIgnoreCase)));
        }

        return baseline;
    }

    public static string GetEfProductVersion()
    {
        return typeof(DbContext).Assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion
               ?? typeof(DbContext).Assembly.GetName().Version?.ToString()
               ?? "10.0.0";
    }

    private static void AddIfPresent(ICollection<string> baseline, string? migrationId)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            return;
        }

        if (!baseline.Contains(migrationId, StringComparer.OrdinalIgnoreCase))
        {
            baseline.Add(migrationId);
        }
    }
}
