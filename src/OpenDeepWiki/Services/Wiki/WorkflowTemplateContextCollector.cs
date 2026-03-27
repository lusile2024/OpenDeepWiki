using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowTemplateContextCollector
{
    Task<WorkflowTemplateSessionContextDto> CollectAsync(
        Repository repository,
        RepositoryBranch? branch,
        string? languageCode,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowTemplateContextCollector : IWorkflowTemplateContextCollector
{
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IWorkflowDiscoveryService _workflowDiscoveryService;
    private readonly ILogger<WorkflowTemplateContextCollector> _logger;

    public WorkflowTemplateContextCollector(
        IRepositoryAnalyzer repositoryAnalyzer,
        IWorkflowDiscoveryService workflowDiscoveryService,
        ILogger<WorkflowTemplateContextCollector> logger)
    {
        _repositoryAnalyzer = repositoryAnalyzer ?? throw new ArgumentNullException(nameof(repositoryAnalyzer));
        _workflowDiscoveryService = workflowDiscoveryService ?? throw new ArgumentNullException(nameof(workflowDiscoveryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowTemplateSessionContextDto> CollectAsync(
        Repository repository,
        RepositoryBranch? branch,
        string? languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var branchName = branch?.BranchName ?? "main";
        var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            branchName,
            branch?.LastCommitId,
            cancellationToken);

        try
        {
            var primaryLanguage = await _repositoryAnalyzer.DetectPrimaryLanguageAsync(workspace, cancellationToken)
                                  ?? repository.PrimaryLanguage;
            var discoveryCandidates = await CollectDiscoveryCandidatesAsync(workspace, cancellationToken);

            return new WorkflowTemplateSessionContextDto
            {
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BranchName = branchName,
                LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "zh" : languageCode.Trim(),
                PrimaryLanguage = primaryLanguage,
                SourceLocation = repository.SourceLocation,
                DirectoryPreview = BuildDirectoryPreview(workspace.WorkingDirectory),
                DiscoveryCandidates = discoveryCandidates
            };
        }
        finally
        {
            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
    }

    private async Task<List<WorkflowTemplateDiscoveryCandidateDto>> CollectDiscoveryCandidatesAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflowDiscoveryService.DiscoverAsync(workspace, cancellationToken: cancellationToken);
            return result.Candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(candidate => new WorkflowTemplateDiscoveryCandidateDto
                {
                    Key = candidate.Key,
                    Name = candidate.Name,
                    Summary = candidate.Summary,
                    TriggerPoints = candidate.TriggerPoints.Take(5).ToList(),
                    CompensationTriggerPoints = candidate.CompensationTriggerPoints.Take(5).ToList(),
                    RequestEntities = candidate.RequestEntities.Take(5).ToList(),
                    SchedulerFiles = candidate.SchedulerFiles.Take(5).ToList(),
                    ExecutorFiles = candidate.ExecutorFiles.Take(5).ToList(),
                    EvidenceFiles = candidate.EvidenceFiles.Take(8).ToList()
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to collect workflow discovery candidates for template workbench.");
            return [];
        }
    }

    private static string BuildDirectoryPreview(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".next", "dist", "build"
        };

        var lines = new List<string>();
        AppendDirectory(rootPath, 0, lines, excludedDirectories);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendDirectory(
        string directory,
        int depth,
        List<string> lines,
        HashSet<string> excludedDirectories)
    {
        if (depth > 3 || lines.Count >= 120)
        {
            return;
        }

        var currentName = Path.GetFileName(directory);
        if (depth == 0 && string.IsNullOrWhiteSpace(currentName))
        {
            currentName = directory;
        }

        lines.Add($"{new string(' ', depth * 2)}- {currentName}/");

        IEnumerable<string> subDirectories;
        try
        {
            subDirectories = Directory.EnumerateDirectories(directory)
                .Where(path => !excludedDirectories.Contains(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var subDirectory in subDirectories)
        {
            if (lines.Count >= 120)
            {
                break;
            }

            AppendDirectory(subDirectory, depth + 1, lines, excludedDirectories);
        }

        if (depth >= 3 || lines.Count >= 120)
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var file in files)
        {
            if (lines.Count >= 120)
            {
                break;
            }

            lines.Add($"{new string(' ', (depth + 1) * 2)}- {Path.GetFileName(file)}");
        }
    }
}
