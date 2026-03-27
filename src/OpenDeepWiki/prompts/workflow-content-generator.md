# Workflow Document Generator

You are generating a workflow-focused wiki page for `{{repository_name}}`.

Target language: `{{language}}`
Catalog path: `{{catalog_path}}`
Catalog title: `{{catalog_title}}`

Workflow summary:
{{workflow_summary}}

Trigger points:
{{workflow_trigger_points}}

Compensation trigger points:
{{workflow_compensation_trigger_points}}

Request entities:
{{workflow_request_entities}}

Scheduler files:
{{workflow_scheduler_files}}

Executor files:
{{workflow_executor_files}}

Service files:
{{workflow_service_files}}

Evidence files:
{{workflow_evidence_files}}

Seed queries:
{{workflow_seed_queries}}

External systems:
{{workflow_external_systems}}

State fields:
{{workflow_state_fields}}

Workflow document preferences:
{{workflow_document_preferences}}

Requirements:

1. Treat this topic as a cross-module business workflow, not a module introduction.
2. Reconstruct the end-to-end path:
   - primary external trigger or API entry
   - request persistence
   - scheduler or polling stage
   - executor selection
   - execution logic
   - state transitions
   - failure handling and retry behavior
3. If compensation trigger points are present, describe them separately as retry or operational entry points.
4. Never treat retry, log, or operational controllers as the primary business trigger unless the evidence clearly proves that.
5. Read the evidence files and follow the seed queries before writing.
6. Include at least one Mermaid flowchart or sequence diagram that reflects the real code path.
7. Cite concrete source files and key methods. Keep code identifiers in original form.
8. If evidence is insufficient for any step, say so explicitly instead of fabricating.
9. Respect the workflow document preferences when they are provided.
10. If `Required Sections` are provided, you MUST create one dedicated Markdown section for each listed item and keep the section heading text unchanged.
11. Never omit or merge required sections. If evidence is incomplete, keep the section and explicitly mark unknown or pending points.
