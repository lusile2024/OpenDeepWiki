You are helping OpenDeepWiki generate an Overlay configuration for a repository.

Output language for explanations: Chinese (Simplified).

Repository:
{{repository_name}}

Base branch candidate:
{{base_branch_name}}

Detected primary language:
{{detected_primary_language}}

User intent:
{{user_intent}}

Existing overlay config JSON:
```json
{{existing_config_json}}
```

Repository structure analysis JSON:
```json
{{analysis_json}}
```

Heuristic fallback config JSON:
```json
{{heuristic_config_json}}
```

Task:
1. Read the structure analysis and user intent.
2. Propose the best Overlay config for separating “base version” and “project version”.
3. Prefer a config that only shows project overrides/additions.
4. Do not invent unsupported fields. Only use the real schema fields from the provided JSON examples.
5. `overlayBranchNameTemplate` must start with `overlay/`.
6. Prefer `DiffMode = "B"` and `OnlyShowProjectChanges = true`.
7. When evidence is weak, keep the heuristic config mostly unchanged and explain the uncertainty.

Return exactly one JSON object with this shape:
```json
{
  "summary": "一句到两句的总结",
  "reasoningSummary": "解释为什么选择这些 roots / variants / mappingRules",
  "warnings": ["可选警告1", "可选警告2"],
  "config": {
    "version": 1,
    "activeProfileKey": "string",
    "profiles": [
      {
        "key": "string",
        "name": "string",
        "baseBranchName": "string",
        "overlayBranchNameTemplate": "overlay/string",
        "roots": ["string"],
        "variants": [
          {
            "key": "string",
            "name": "string",
            "detectionMode": "PathSegmentEquals"
          }
        ],
        "mappingRules": [
          {
            "type": "RemoveVariantSegment",
            "segment": "string"
          }
        ],
        "includeGlobs": ["string"],
        "excludeGlobs": ["string"],
        "generation": {
          "onlyShowProjectChanges": true,
          "diffMode": "B",
          "maxFiles": 200,
          "maxFileBytes": 200000
        }
      }
    ]
  }
}
```

Return JSON only. No markdown fences. No extra commentary.
