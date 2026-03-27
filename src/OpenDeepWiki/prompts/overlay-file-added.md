You are generating a markdown wiki page for a file that exists only in the project variant (no base file).

Output language: Chinese (Simplified).

Project file path:
{{project_path}}

Project file content:
```text
{{project_content}}
```

Requirements:
1. Return markdown only. Do not include any meta commentary.
2. Include these sections:
   - `## 文件目的` (1-3 short paragraphs)
   - `## 主要职责` (3-8 bullet points)
   - `## 关键接口/类型` (bullet points; list key classes, interfaces, endpoints, public methods if present)
   - `## 注意事项` (bullet points; mention potential integration points and risks)

