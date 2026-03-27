using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Overlays;

namespace OpenDeepWiki.Services.Admin;

public interface IAdminRepositoryOverlayService
{
    Task<RepositoryOverlayConfig> GetConfigAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<RepositoryOverlayConfig> SaveConfigAsync(string repositoryId, RepositoryOverlayConfig config, CancellationToken cancellationToken = default);
}

public sealed class AdminRepositoryOverlayService : IAdminRepositoryOverlayService
{
    private const string SettingKeyPrefix = "repo.overlay.config:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IContext _context;
    private readonly ILogger<AdminRepositoryOverlayService> _logger;

    public AdminRepositoryOverlayService(IContext context, ILogger<AdminRepositoryOverlayService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<RepositoryOverlayConfig> GetConfigAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            throw new ArgumentException("RepositoryId cannot be empty.", nameof(repositoryId));
        }

        var key = SettingKeyPrefix + repositoryId;
        var setting = await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting?.Value is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return CreateDefaultConfig();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RepositoryOverlayConfig>(setting.Value, JsonOptions);
            return RepositoryOverlayConfigRules.Sanitize(parsed ?? CreateDefaultConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse overlay config for repository {RepositoryId}", repositoryId);
            return CreateDefaultConfig();
        }
    }

    public async Task<RepositoryOverlayConfig> SaveConfigAsync(
        string repositoryId,
        RepositoryOverlayConfig config,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            throw new ArgumentException("RepositoryId cannot be empty.", nameof(repositoryId));
        }

        config = RepositoryOverlayConfigRules.Sanitize(config ?? CreateDefaultConfig());
        RepositoryOverlayConfigRules.Validate(config);

        var key = SettingKeyPrefix + repositoryId;
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        var serialized = JsonSerializer.Serialize(config, JsonOptions);
        if (setting is null)
        {
            setting = new SystemSetting
            {
                Id = Guid.NewGuid().ToString(),
                Key = key,
                Category = "repository",
                Description = "Repository overlay config (base vs project override mapping)",
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
                setting.Description = "Repository overlay config (base vs project override mapping)";
            }

            _context.SystemSettings.Update(setting);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return config;
    }

    private static RepositoryOverlayConfig CreateDefaultConfig()
    {
        return RepositoryOverlayConfigRules.Sanitize(new RepositoryOverlayConfig
        {
            Version = 1,
            ActiveProfileKey = "default",
            Profiles =
            [
                new OverlayProfile
                {
                    Key = "default",
                    Name = "默认 Overlay 规则",
                    BaseBranchName = "main",
                    OverlayBranchNameTemplate = "overlay/{profileKey}",
                    Roots = ["src/Domain", "src/Application"],
                    Variants =
                    [
                        new OverlayVariant
                        {
                            Key = "1397",
                            DetectionMode = OverlayVariantDetectionMode.PathSegmentEquals
                        }
                    ],
                    MappingRules =
                    [
                        new OverlayMappingRule
                        {
                            Type = OverlayMappingRuleType.RemoveVariantSegment
                        }
                    ],
                    Generation = new OverlayGenerationOptions
                    {
                        OnlyShowProjectChanges = true,
                        DiffMode = "B"
                    }
                }
            ]
        });
    }
}
