using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace OpenDeepWiki.Services.Wiki;

public sealed class MsBuildWorkspaceBootstrap
{
    private static readonly object SyncRoot = new();
    private static bool _registered;
    private readonly ILogger<MsBuildWorkspaceBootstrap> _logger;

    public MsBuildWorkspaceBootstrap(ILogger<MsBuildWorkspaceBootstrap> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MSBuildWorkspace CreateWorkspace()
    {
        EnsureRegistered();

        var workspace = MSBuildWorkspace.Create(
            new Dictionary<string, string>
            {
                ["DesignTimeBuild"] = "true",
                ["BuildingInsideVisualStudio"] = "true"
            });

        workspace.LoadMetadataForReferencedProjects = true;
        workspace.SkipUnrecognizedProjects = true;
        workspace.WorkspaceFailed += (_, args) =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                _logger.LogWarning("MSBuildWorkspace diagnostic: {Message}", args.Diagnostic.Message);
                return;
            }

            _logger.LogDebug("MSBuildWorkspace diagnostic: {Message}", args.Diagnostic.Message);
        };

        return workspace;
    }

    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered)
            {
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                var instance = MSBuildLocator.RegisterDefaults();
                _logger.LogInformation("Registered MSBuild from {Path}", instance.MSBuildPath);
            }

            _registered = true;
        }
    }
}
