You are generating a markdown wiki page that compares a "base version" file and a "project override" file.

Output language: Chinese (Simplified).

Diff mode: {{diff_mode}}

Base file path:
{{base_path}}

Project file path:
{{project_path}}

Base file content:
```text
{{base_content}}
```

Project file content:
```text
{{project_content}}
```

Requirements:
1. Return markdown only. Do not include any meta commentary.
2. Include these sections:
   - `## 变更摘要` (3-8 bullet points, actionable and specific)
   - `## 关键差异点` (bullet points; focus on behavior/API/contract changes)
3. If diff mode is "B", include:
   - `## 关键 diff 片段`
     - Provide 2-6 code blocks (use the best language fence you can infer, e.g. ```csharp)
     - Each code block should be short (10-60 lines) and show only the most relevant changed parts
     - Above each code block, add a one-line explanation of what the snippet demonstrates
4. If either file is truncated, mention this in `## 注意事项`.
5. End with `## 注意事项` listing any uncertainties or risks (e.g., missing context, possible side effects).

