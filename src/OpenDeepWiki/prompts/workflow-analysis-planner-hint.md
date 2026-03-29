# Workflow Analysis Planner Hint

You are helping OpenDeepWiki propose additional `branch-drilldown` tasks for an ACP workflow-analysis session.

Analysis session id: `{{analysis_session_id}}`
Profile key: `{{profile_key}}`
Language: `{{language}}`

Objective:
{{objective}}

Workflow profile:
```json
{{profile_json}}
```

Chapter slices:
```json
{{chapter_slices_json}}
```

Existing planned tasks:
```json
{{existing_tasks_json}}
```

Remaining branch capacity by chapter:
```json
{{remaining_branch_capacity_json}}
```

Rules:

1. You only propose incremental `branch-drilldown` tasks. Do not rewrite the whole plan.
2. Prefer symbols that are strong branch hotspots, missing must-explain symbols, extension points, or high-value downstream methods.
3. Respect `remainingBranchCapacityByChapter`. Do not suggest more tasks than the remaining capacity of each chapter.
4. Do not repeat an existing `branchRoot` already present in `existingTasks`.
5. Focus on code-analysis usefulness, not documentation wording polish.
6. Return JSON only. Do not wrap it in markdown. Do not output explanations before or after JSON.
7. `focusSymbols` must put `branchRoot` first and keep the list concise.
8. If no useful suggestion exists, return an empty `suggestedBranchTasks` array.

Output schema:
```json
{
  "summary": "一句话概括 AI planner hint 补充了什么",
  "suggestedBranchTasks": [
    {
      "chapterKey": "allocation",
      "branchRoot": "AllocateDoubleDeep",
      "focusSymbols": ["AllocateDoubleDeep", "ValidateDoubleDeep"],
      "summary": "需要补充双深货位规则、前置校验与差异处理",
      "reason": "must-explain hotspot"
    }
  ]
}
```
