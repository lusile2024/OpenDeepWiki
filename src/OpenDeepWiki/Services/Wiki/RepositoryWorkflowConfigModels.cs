using System.Text.Json.Serialization;

namespace OpenDeepWiki.Services.Wiki;

public sealed class RepositoryWorkflowConfig
{
    public int Version { get; set; } = 1;

    public string? ActiveProfileKey { get; set; }

    public List<RepositoryWorkflowProfile> Profiles { get; set; } = [];
}

public sealed class RepositoryWorkflowProfile
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RepositoryWorkflowProfileMode Mode { get; set; } = RepositoryWorkflowProfileMode.WcsRequestExecutor;

    public List<string> AnchorDirectories { get; set; } = [];

    public List<string> AnchorNames { get; set; } = [];

    public List<string> PrimaryTriggerDirectories { get; set; } = [];

    public List<string> CompensationTriggerDirectories { get; set; } = [];

    public List<string> SchedulerDirectories { get; set; } = [];

    public List<string> ServiceDirectories { get; set; } = [];

    public List<string> RepositoryDirectories { get; set; } = [];

    public List<string> PrimaryTriggerNames { get; set; } = [];

    public List<string> CompensationTriggerNames { get; set; } = [];

    public List<string> SchedulerNames { get; set; } = [];

    public List<string> RequestEntityNames { get; set; } = [];

    public List<string> RequestServiceNames { get; set; } = [];

    public List<string> RequestRepositoryNames { get; set; } = [];

    public RepositoryWorkflowProfileSource Source { get; set; } = new();

    public WorkflowDocumentPreferences DocumentPreferences { get; set; } = new();
}

public enum RepositoryWorkflowProfileMode
{
    WcsRequestExecutor = 0
}

public sealed class RepositoryWorkflowProfileSource
{
    public string Type { get; set; } = "manual";

    public string? SessionId { get; set; }

    public int? VersionNumber { get; set; }

    public string? UpdatedByUserId { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public sealed class WorkflowDocumentPreferences
{
    public string? WritingHint { get; set; }

    public List<string> PreferredTerms { get; set; } = [];

    public List<string> RequiredSections { get; set; } = [];

    public List<string> AvoidPrimaryTriggerNames { get; set; } = [];
}
