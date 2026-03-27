using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Wiki;

public interface IRepositoryWorkflowConfigService
{
    Task<RepositoryWorkflowConfig> GetConfigAsync(string repositoryId, CancellationToken cancellationToken = default);

    Task<RepositoryWorkflowConfig> SaveConfigAsync(
        string repositoryId,
        RepositoryWorkflowConfig config,
        CancellationToken cancellationToken = default);

    Task<RepositoryWorkflowProfile?> GetActiveProfileAsync(string repositoryId, CancellationToken cancellationToken = default);

    Task<RepositoryWorkflowProfile?> GetProfileAsync(
        string repositoryId,
        string profileKey,
        CancellationToken cancellationToken = default);
}

public sealed class RepositoryWorkflowConfigService : IRepositoryWorkflowConfigService
{
    private const string SettingKeyPrefix = "repo.workflow.config:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IContext _context;
    private readonly ILogger<RepositoryWorkflowConfigService> _logger;

    public RepositoryWorkflowConfigService(IContext context, ILogger<RepositoryWorkflowConfigService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<RepositoryWorkflowConfig> GetConfigAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            throw new ArgumentException("RepositoryId cannot be empty.", nameof(repositoryId));
        }

        var setting = await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Key == BuildSettingKey(repositoryId), cancellationToken);

        if (string.IsNullOrWhiteSpace(setting?.Value))
        {
            return CreateDefaultConfig();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RepositoryWorkflowConfig>(setting.Value, JsonOptions);
            return RepositoryWorkflowConfigRules.Sanitize(parsed ?? CreateDefaultConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse workflow config for repository {RepositoryId}", repositoryId);
            return CreateDefaultConfig();
        }
    }

    public async Task<RepositoryWorkflowConfig> SaveConfigAsync(
        string repositoryId,
        RepositoryWorkflowConfig config,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            throw new ArgumentException("RepositoryId cannot be empty.", nameof(repositoryId));
        }

        config = RepositoryWorkflowConfigRules.Sanitize(config ?? CreateDefaultConfig());
        RepositoryWorkflowConfigRules.Validate(config);

        var key = BuildSettingKey(repositoryId);
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken);

        var serialized = JsonSerializer.Serialize(config, JsonOptions);
        if (setting is null)
        {
            setting = new SystemSetting
            {
                Id = Guid.NewGuid().ToString(),
                Key = key,
                Category = "repository",
                Description = "Repository workflow discovery profile config",
                Value = serialized
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = serialized;
            setting.Category = "repository";
            if (string.IsNullOrWhiteSpace(setting.Description))
            {
                setting.Description = "Repository workflow discovery profile config";
            }

            _context.SystemSettings.Update(setting);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return config;
    }

    public async Task<RepositoryWorkflowProfile?> GetActiveProfileAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(repositoryId, cancellationToken);
        if (config.Profiles.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(config.ActiveProfileKey))
        {
            return config.Profiles.FirstOrDefault(profile =>
                profile.Enabled &&
                string.Equals(profile.Key, config.ActiveProfileKey, StringComparison.OrdinalIgnoreCase));
        }

        return config.Profiles.FirstOrDefault(profile => profile.Enabled);
    }

    public async Task<RepositoryWorkflowProfile?> GetProfileAsync(
        string repositoryId,
        string profileKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            return null;
        }

        var config = await GetConfigAsync(repositoryId, cancellationToken);
        return config.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Key, profileKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSettingKey(string repositoryId)
    {
        return SettingKeyPrefix + repositoryId;
    }

    private static RepositoryWorkflowConfig CreateDefaultConfig()
    {
        return RepositoryWorkflowConfigRules.Sanitize(new RepositoryWorkflowConfig
        {
            Version = 1,
            Profiles = []
        });
    }
}
