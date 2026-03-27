using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WorkflowTemplateWorkbenchAiResponseParserTests
{
    [Fact]
    public void Parse_ShouldRepairUnescapedQuotesInsideStringValues()
    {
        var content = """
{
  "title": "定时创建出库任务流程定位",
  "assistantMessage": "已更新草稿。",
  "changeSummary": "排除无关流程，收敛到出库任务。",
  "riskNotes": [
    "当前未明确找到单独的"定时创建出库任务"专属流程，本次先按出库申请流程建模"
  ],
  "evidenceFiles": [
    "src/Cimc.Tianda.Wms.Jobs/Wcs/WcsRequestWmsExecutorJob.cs"
  ],
  "updatedDraft": {
    "key": "wcs-stn-move-out",
    "name": "处理站台出库申请",
    "description": "处理WCS站台出库申请。",
    "enabled": false,
    "mode": "WcsRequestExecutor",
    "anchorDirectories": [
      "src/Cimc.Tianda.Wms.Application/Wcs/WcsRequestExecutors"
    ],
    "anchorNames": [
      "WcsStnMoveOutExecutor"
    ],
    "primaryTriggerDirectories": [],
    "compensationTriggerDirectories": [],
    "schedulerDirectories": [],
    "serviceDirectories": [],
    "repositoryDirectories": [],
    "primaryTriggerNames": [],
    "compensationTriggerNames": [],
    "schedulerNames": [],
    "requestEntityNames": [],
    "requestServiceNames": [],
    "requestRepositoryNames": [],
    "source": {
      "type": "ai-workbench-draft"
    },
    "documentPreferences": {
      "writingHint": "聚焦定时出库任务。",
      "preferredTerms": [],
      "requiredSections": [],
      "avoidPrimaryTriggerNames": []
    }
  }
}
""";

        var result = WorkflowTemplateWorkbenchAiResponseParser.Parse(content);

        Assert.Equal("wcs-stn-move-out", result.UpdatedDraft.Key);
        Assert.Single(result.RiskNotes);
        Assert.Contains("定时创建出库任务", result.RiskNotes[0]);
    }

    [Fact]
    public void Parse_ShouldPromoteDraftAliasAndNormalizeScalarArrays()
    {
        var content = """
```json
{
  "title": "货位异常恢复",
  "assistantMessage": "已调整草稿。",
  "changeSummary": "修正入口识别。",
  "riskNotes": "仍需确认定时任务是否参与",
  "evidenceFiles": "src/Cimc.Tianda.Wms.Jobs/Wcs/WcsRequestWmsExecutorJob.cs",
  "draft": {
    "key": "loc-exception-recover",
    "name": "货位异常恢复",
    "description": "货位异常恢复请求处理流程。",
    "enabled": false,
    "mode": "WcsRequestExecutor",
    "anchorDirectories": "src/Cimc.Tianda.Wms.Application/Wcs/WcsRequestExecutors",
    "anchorNames": "LocExceptionRecoverExecutor",
    "primaryTriggerDirectories": [],
    "compensationTriggerDirectories": [],
    "schedulerDirectories": [],
    "serviceDirectories": [],
    "repositoryDirectories": [],
    "primaryTriggerNames": [],
    "compensationTriggerNames": [],
    "schedulerNames": [],
    "requestEntityNames": [],
    "requestServiceNames": [],
    "requestRepositoryNames": [],
    "source": {
      "type": "ai-workbench-draft"
    },
    "documentPreferences": {
      "writingHint": "聚焦异常恢复。",
      "preferredTerms": "货位异常恢复",
      "requiredSections": "异常处理",
      "avoidPrimaryTriggerNames": "LogExternalInterfaceController"
    }
  }
}
``` 
""";

        var result = WorkflowTemplateWorkbenchAiResponseParser.Parse(content);

        Assert.Equal("loc-exception-recover", result.UpdatedDraft.Key);
        Assert.Single(result.RiskNotes);
        Assert.Single(result.EvidenceFiles);
        Assert.Single(result.UpdatedDraft.AnchorDirectories);
        Assert.Single(result.UpdatedDraft.DocumentPreferences.PreferredTerms);
        Assert.Single(result.UpdatedDraft.DocumentPreferences.RequiredSections);
        Assert.Single(result.UpdatedDraft.DocumentPreferences.AvoidPrimaryTriggerNames);
    }
}
