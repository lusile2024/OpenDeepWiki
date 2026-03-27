namespace OpenDeepWiki.Services.Wiki;

public sealed class WorkflowDiscoveryOptions
{
    public const string SectionName = "WorkflowDiscovery";

    public bool Enabled { get; set; } = true;

    public double MinScore { get; set; } = 1.0d;

    public bool RefreshOnIncrementalCodeChanges { get; set; } = true;

    public string[] CodeChangeExtensions { get; set; } = [".cs", ".csproj", ".sln"];
}
