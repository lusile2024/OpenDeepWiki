namespace OpenDeepWiki.Services.Wiki;

public interface IWorkflowChapterSliceBuilder
{
    WorkflowChapterSlice Build(
        WorkflowSemanticGraph graph,
        RepositoryWorkflowProfile profile,
        WorkflowChapterProfile? chapterProfile = null);
}
