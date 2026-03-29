namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowDeepAnalysisService
{
    WorkflowDeepAnalysisResult Analyze(WorkflowDeepAnalysisInput input);
}
