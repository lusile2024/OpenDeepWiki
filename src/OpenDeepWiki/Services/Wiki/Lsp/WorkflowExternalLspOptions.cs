namespace OpenDeepWiki.Services.Wiki.Lsp;

public sealed class WorkflowExternalLspOptions
{
    public const string SectionName = "WorkflowExternalLsp";

    public bool Enabled { get; set; }

    public string? Command { get; set; }

    public string? Arguments { get; set; }

    public string WorkingDirectoryMode { get; set; } = "workspace";

    public int InitializeTimeoutMs { get; set; } = 10000;

    public int WarmupDelayMs { get; set; } = 4000;

    public int RequestTimeoutMs { get; set; } = 10000;

    public int MaxConcurrentRequests { get; set; } = 1;

    public bool TracePayloads { get; set; }
}
