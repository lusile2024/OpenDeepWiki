using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Admin;

namespace OpenDeepWiki.Infrastructure;

/// <summary>
/// 数据库初始化服务
/// </summary>
public static class DbInitializer
{
    private const string SqliteMigrationHistoryTableName = "__EFMigrationsHistory";

    /// <summary>
    /// 初始化数据库（创建默认角色和OAuth提供商）
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (context is DbContext dbContext)
        {
            await EnsureDatabaseSchemaAsync(dbContext);
        }

        // 初始化默认角色
        await InitializeRolesAsync(context);

        // 初始化默认管理员账户
        await InitializeAdminUserAsync(context);

        // 初始化OAuth提供商
        await InitializeOAuthProvidersAsync(context);

        // 初始化系统设置默认值（仅在首次运行时从环境变量创建）
        await SystemSettingDefaults.InitializeDefaultsAsync(configuration, context);
    }

    private static async Task EnsureDatabaseSchemaAsync(DbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        if (dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await BaselineLegacySqliteMigrationsAsync(dbContext, cancellationToken);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BaselineLegacySqliteMigrationsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        if (await SqliteMigrationHistoryExistsAsync(dbContext, cancellationToken))
        {
            return;
        }

        var existingTables = await GetExistingSqliteTablesAsync(dbContext, cancellationToken);
        if (existingTables.Count == 0)
        {
            return;
        }

        var baselineMigrationIds = SqliteLegacyMigrationPlanner.GetBaselineMigrationIds(
            dbContext.Database.GetMigrations(),
            existingTables);

        if (baselineMigrationIds.Count == 0)
        {
            return;
        }

        const string createHistoryTableSql = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """;

        await dbContext.Database.ExecuteSqlRawAsync(createHistoryTableSql, cancellationToken);

        var productVersion = SqliteLegacyMigrationPlanner.GetEfProductVersion();
        foreach (var migrationId in baselineMigrationIds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ({migrationId}, {productVersion});
                """,
                cancellationToken);
        }
    }

    private static async Task<bool> SqliteMigrationHistoryExistsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var tableNames = await GetExistingSqliteTablesAsync(dbContext, cancellationToken, includeHistoryTable: true);
        return tableNames.Contains(SqliteMigrationHistoryTableName, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> GetExistingSqliteTablesAsync(
        DbContext dbContext,
        CancellationToken cancellationToken,
        bool includeHistoryTable = false)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = includeHistoryTable
                ? """
                  SELECT name
                  FROM sqlite_master
                  WHERE type = 'table'
                    AND name NOT LIKE 'sqlite_%';
                  """
                : """
                  SELECT name
                  FROM sqlite_master
                  WHERE type = 'table'
                    AND name NOT LIKE 'sqlite_%'
                    AND name <> '__EFMigrationsHistory';
                  """;

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            return tables;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task InitializeAdminUserAsync(IContext context)
    {
        const string adminEmail = "admin@routin.ai";
        const string adminPassword = "Admin@123";

        var exists = await context.Users.AnyAsync(u => u.Email == adminEmail && !u.IsDeleted);
        if (exists) return;

        var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin" && !r.IsDeleted);
        if (adminRole == null) return;

        var adminUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Name = "admin",
            Email = adminEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Status = 1,
            IsSystem = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(adminUser);

        var userRole = new UserRole
        {
            Id = Guid.NewGuid().ToString(),
            UserId = adminUser.Id,
            RoleId = adminRole.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.UserRoles.Add(userRole);
        await context.SaveChangesAsync();
    }

    private static async Task InitializeRolesAsync(IContext context)
    {
        var roles = new[]
        {
            new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Admin",
                Description = "系统管理员",
                IsActive = true,
                IsSystemRole = true,
                CreatedAt = DateTime.UtcNow
            },
            new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = "User",
                Description = "普通用户",
                IsActive = true,
                IsSystemRole = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var role in roles)
        {
            var exists = await context.Roles.AnyAsync(r => r.Name == role.Name && !r.IsDeleted);
            if (!exists)
            {
                context.Roles.Add(role);
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task InitializeOAuthProvidersAsync(IContext context)
    {
        var providers = new[]
        {
            new OAuthProvider
            {
                Id = Guid.NewGuid().ToString(),
                Name = "github",
                DisplayName = "GitHub",
                AuthorizationUrl = "https://github.com/login/oauth/authorize",
                TokenUrl = "https://github.com/login/oauth/access_token",
                UserInfoUrl = "https://api.github.com/user",
                ClientId = "YOUR_GITHUB_CLIENT_ID",
                ClientSecret = "YOUR_GITHUB_CLIENT_SECRET",
                RedirectUri = "http://localhost:8080/api/oauth/github/callback",
                Scope = "user:email",
                UserInfoMapping = "{\"id\":\"id\",\"name\":\"login\",\"email\":\"email\",\"avatar\":\"avatar_url\"}",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            },
            new OAuthProvider
            {
                Id = Guid.NewGuid().ToString(),
                Name = "gitee",
                DisplayName = "Gitee",
                AuthorizationUrl = "https://gitee.com/oauth/authorize",
                TokenUrl = "https://gitee.com/oauth/token",
                UserInfoUrl = "https://gitee.com/api/v5/user",
                ClientId = "YOUR_GITEE_CLIENT_ID",
                ClientSecret = "YOUR_GITEE_CLIENT_SECRET",
                RedirectUri = "http://localhost:8080/api/oauth/gitee/callback",
                Scope = "user_info emails",
                UserInfoMapping = "{\"id\":\"id\",\"name\":\"name\",\"email\":\"email\",\"avatar\":\"avatar_url\"}",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var provider in providers)
        {
            var exists = await context.OAuthProviders.AnyAsync(p => p.Name == provider.Name && !p.IsDeleted);
            if (!exists)
            {
                context.OAuthProviders.Add(provider);
            }
        }

        await context.SaveChangesAsync();
    }

}
