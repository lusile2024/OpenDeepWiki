# Workflow Discovery Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add high-precision workflow discovery for .NET/C# repositories so OpenDeepWiki can generate first-class business-flow pages such as “容器托盘入库流程”, not just module pages.

**Architecture:** Keep the existing repository pipeline intact, but add a Roslyn-powered semantic analysis stage after catalog generation and before document generation. Persist workflow topic context separately from `CatalogItem`, merge discovered workflow leaf pages into the catalog tree, and route workflow pages through a dedicated prompt that reads evidence files and seed queries first. Keep Roslyn as the only .NET semantic engine in this iteration, while reserving an `IWorkflowSemanticProvider` abstraction for future LSP-backed multi-language support.

**Tech Stack:** ASP.NET Core 10, EF Core 10, Microsoft.CodeAnalysis (Roslyn), xUnit, Moq

**Spec:** `docs/superpowers/specs/2026-03-24-workflow-discovery-design.md`

**Git Note:** Do not create commits unless the user explicitly asks for them.

---

## Files To Modify

| File | Change |
|------|--------|
| `Directory.Packages.props` | Add Roslyn workspace package versions needed for semantic analysis |
| `src/OpenDeepWiki/OpenDeepWiki.csproj` | Add explicit package references for Roslyn workspace loading |
| `src/OpenDeepWiki.EFCore/MasterDbContext.cs` | Add `DocTopicContext` DbSet and model configuration |
| `src/OpenDeepWiki/Program.cs` | Register workflow discovery options and services |
| `src/OpenDeepWiki/appsettings.json` | Add `WorkflowDiscovery` defaults |
| `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs` | Integrate workflow discovery, topic-context lookup, workflow prompt routing, and incremental-update refresh |
| `src/EFCore/OpenDeepWiki.Sqlite/Migrations/SqliteDbContextModelSnapshot.cs` | Update snapshot for `DocTopicContext` |
| `src/EFCore/OpenDeepWiki.Postgresql/Migrations/PostgresqlDbContextModelSnapshot.cs` | Update snapshot for `DocTopicContext` |
| `src/OpenDeepWiki/prompts/workflow-content-generator.md` | Add workflow-specific document prompt |
| `tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj` | Keep as-is unless a direct Roslyn test dependency is actually required |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WikiGeneratorOptionsConfiguratorTests.cs` | Only touch if any workflow setting is merged into existing wiki options |

## Files To Create

| File | Responsibility |
|------|----------------|
| `src/OpenDeepWiki.Entities/Repositories/DocTopicContext.cs` | Persist per-document workflow or module topic context |
| `src/OpenDeepWiki/Services/Wiki/WorkflowDiscoveryOptions.cs` | Hold discovery thresholds and feature flags |
| `src/OpenDeepWiki/Services/Wiki/IWorkflowSemanticProvider.cs` | Language-agnostic semantic provider abstraction |
| `src/OpenDeepWiki/Services/Wiki/WorkflowSemanticGraph.cs` | Shared graph nodes, edges, enums, and graph payload types |
| `src/OpenDeepWiki/Services/Wiki/MsBuildWorkspaceBootstrap.cs` | Centralize `MSBuildLocator` registration and workspace setup |
| `src/OpenDeepWiki/Services/Wiki/RoslynWorkflowSemanticProvider.cs` | Build semantic graph from `.sln` or `.csproj` |
| `src/OpenDeepWiki/Services/Wiki/WorkflowCandidateExtractor.cs` | Cluster graph data into workflow candidates and score them |
| `src/OpenDeepWiki/Services/Wiki/WorkflowCatalogAugmenter.cs` | Merge workflow leaf pages into the catalog tree |
| `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs` | Upsert, read, and clear `DocTopicContext` records |
| `src/OpenDeepWiki/Services/Wiki/IWorkflowDiscoveryService.cs` | Discovery orchestrator contract |
| `src/OpenDeepWiki/Services/Wiki/WorkflowDiscoveryService.cs` | Semantic provider selection, graph building, extraction, and context assembly |
| `src/OpenDeepWiki/Services/Wiki/WorkflowDocumentRouteResolver.cs` | Decide whether a catalog page should use the workflow prompt |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowSemanticSampleBuilder.cs` | Build temporary `.sln/.csproj` samples for Roslyn tests |
| `tests/OpenDeepWiki.Tests/Services/Wiki/RoslynWorkflowSemanticProviderTests.cs` | Validate graph extraction from sample C# solutions |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCandidateExtractorTests.cs` | Validate clustering and scoring rules |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCatalogAugmenterTests.cs` | Validate catalog merge behavior |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowTopicContextServiceTests.cs` | Validate topic-context persistence |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDocumentRoutingTests.cs` | Validate workflow page routing to the dedicated prompt |
| `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDiscoveryServiceTests.cs` | Validate end-to-end discovery orchestration |
| `src/EFCore/OpenDeepWiki.Sqlite/Migrations/202603250900_AddDocTopicContexts.cs` | Sqlite migration for topic context storage |
| `src/EFCore/OpenDeepWiki.Sqlite/Migrations/202603250900_AddDocTopicContexts.Designer.cs` | Sqlite migration designer |
| `src/EFCore/OpenDeepWiki.Postgresql/Migrations/202603250900_AddDocTopicContexts.cs` | PostgreSQL migration for topic context storage |
| `src/EFCore/OpenDeepWiki.Postgresql/Migrations/202603250900_AddDocTopicContexts.Designer.cs` | PostgreSQL migration designer |

---

## Chunk 1: Roslyn Semantic Foundation

### Task 1: Add failing tests for Roslyn semantic graph extraction

**Files:**
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowSemanticSampleBuilder.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/RoslynWorkflowSemanticProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover these scenarios using a temporary sample solution generated at runtime:
- `WcsInboundController` writes a `ContainerPalletInboundRequest` through a repository or DbContext-backed service
- `InboundRequestScanWorker : BackgroundService` scans pending requests
- `InboundExecutorFactory` dispatches to `ContainerPalletInboundExecutor`
- `ContainerPalletInboundExecutor` updates request status to `Processing` and `Completed`

Use assertions that expect graph nodes and edges for:
- `Controller`
- `RequestEntity`
- `BackgroundService`
- `ExecutorFactory`
- `Executor`
- `Writes`
- `Queries`
- `Dispatches`
- `UpdatesStatus`

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RoslynWorkflowSemanticProviderTests
```

Expected: FAIL because the semantic provider, graph types, and sample builder do not exist yet.

- [ ] **Step 3: Add the minimal Roslyn workspace scaffolding**

Implement these files and types:
- `Directory.Packages.props`
  - Add `Microsoft.Build.Locator`
  - Add `Microsoft.CodeAnalysis.CSharp.Workspaces`
  - Add `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `src/OpenDeepWiki/OpenDeepWiki.csproj`
  - Add matching `PackageReference` entries
- `src/OpenDeepWiki/Services/Wiki/IWorkflowSemanticProvider.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowSemanticGraph.cs`
- `src/OpenDeepWiki/Services/Wiki/MsBuildWorkspaceBootstrap.cs`
- `src/OpenDeepWiki/Services/Wiki/RoslynWorkflowSemanticProvider.cs`

Shape the APIs like this:

```csharp
public interface IWorkflowSemanticProvider
{
    bool CanHandle(RepositoryWorkspace workspace);
    Task<WorkflowSemanticGraph> BuildGraphAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default);
}
```

`MsBuildWorkspaceBootstrap` must:
- call `MSBuildLocator.RegisterDefaults()` exactly once
- create a reusable `MSBuildWorkspace`
- log workspace diagnostics instead of throwing on every warning

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RoslynWorkflowSemanticProviderTests
```

Expected: PASS

---

### Task 2: Add graph-enrichment tests for DI registration and call-path linking

**Files:**
- Modify: `tests/OpenDeepWiki.Tests/Services/Wiki/RoslynWorkflowSemanticProviderTests.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/RoslynWorkflowSemanticProvider.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowSemanticGraph.cs`

- [ ] **Step 1: Extend the failing tests**

Add assertions for:
- `Program.cs` or equivalent registration producing `RegisteredBy` edges for hosted services and executor implementations
- method-call linking from controller action to service, service to repository, worker to factory, factory to executor
- `Status = Processing` and `Status = Completed` assignments producing `UpdatesStatus` evidence

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RoslynWorkflowSemanticProviderTests
```

Expected: FAIL because the first-pass provider only loads the workspace and basic nodes.

- [ ] **Step 3: Implement graph enrichment**

Enhance `RoslynWorkflowSemanticProvider` to:
- inspect base types and implemented interfaces
- inspect invocation expressions and resolved method symbols
- inspect `AddHostedService`, `AddScoped`, `AddTransient`, `AddSingleton`
- inspect assignment expressions that target common status members such as `Status`, `State`, `RequestStatus`

Do not try to build a whole-program call graph. Limit enrichment to:
- symbol-resolved direct calls
- DI registrations
- status assignments
- `switch`/factory dispatch detection

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RoslynWorkflowSemanticProviderTests
```

Expected: PASS

---

## Chunk 2: Workflow Candidates And Persistence

### Task 3: Add failing tests for workflow clustering, naming, and scoring

**Files:**
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCandidateExtractorTests.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowCandidateExtractor.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/IWorkflowDiscoveryService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowDiscoveryService.cs`

- [ ] **Step 1: Write the failing tests**

Cover these behaviors:
- a connected chain `入口 -> 请求表 -> 后台任务 -> executor -> service` becomes one workflow candidate
- a group with only service-to-service calls is rejected as a workflow
- a candidate with clear external signals gets a higher score than one without them
- the generated workflow name prefers request/entity/executor keywords over generic `Service` naming

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowCandidateExtractorTests
```

Expected: FAIL because extraction and scoring do not exist yet.

- [ ] **Step 3: Implement the extractor and discovery orchestration**

Implement:
- `WorkflowCandidateExtractor`
- `WorkflowDiscoveryService`

`WorkflowDiscoveryService` should:
- select the first semantic provider where `CanHandle == true`
- build the semantic graph
- call `WorkflowCandidateExtractor`
- assemble `WorkflowTopicCandidate` objects with:
  - `Name`
  - `Summary`
  - `Actors`
  - `TriggerPoints`
  - `RequestEntities`
  - `SchedulerFiles`
  - `ExecutorFiles`
  - `ServiceFiles`
  - `EvidenceFiles`
  - `SeedQueries`
  - `StateFields`

`SeedQueries` should be concrete search strings such as:
- `ContainerPalletInboundRequest`
- `InboundRequestScanWorker`
- `ContainerPalletInboundExecutor`

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowCandidateExtractorTests
```

Expected: PASS

---

### Task 4: Add topic-context persistence and catalog merge support

**Files:**
- Create: `src/OpenDeepWiki.Entities/Repositories/DocTopicContext.cs`
- Modify: `src/OpenDeepWiki.EFCore/MasterDbContext.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowCatalogAugmenter.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowTopicContextServiceTests.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCatalogAugmenterTests.cs`
- Create: `src/EFCore/OpenDeepWiki.Sqlite/Migrations/202603250900_AddDocTopicContexts.cs`
- Create: `src/EFCore/OpenDeepWiki.Sqlite/Migrations/202603250900_AddDocTopicContexts.Designer.cs`
- Modify: `src/EFCore/OpenDeepWiki.Sqlite/Migrations/SqliteDbContextModelSnapshot.cs`
- Create: `src/EFCore/OpenDeepWiki.Postgresql/Migrations/202603250900_AddDocTopicContexts.cs`
- Create: `src/EFCore/OpenDeepWiki.Postgresql/Migrations/202603250900_AddDocTopicContexts.Designer.cs`
- Modify: `src/EFCore/OpenDeepWiki.Postgresql/Migrations/PostgresqlDbContextModelSnapshot.cs`

- [ ] **Step 1: Write the failing tests**

`WorkflowTopicContextServiceTests` should cover:
- upsert by `(BranchLanguageId, CatalogPath)`
- clearing all topic contexts for a branch language
- reading workflow context back as typed data

`WorkflowCatalogAugmenterTests` should cover:
- creating a top-level `核心业务流程` folder when missing
- appending workflow leaf pages in stable order
- keeping the rest of the catalog unchanged

- [ ] **Step 2: Run the targeted tests to verify they fail**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter "WorkflowTopicContextServiceTests|WorkflowCatalogAugmenterTests"
```

Expected: FAIL because the entity, DbSet, service, and augmenter do not exist yet.

- [ ] **Step 3: Implement persistence, model configuration, and migrations**

Add `DocTopicContext` with at least:

```csharp
public class DocTopicContext : AggregateRoot<string>
{
    public string BranchLanguageId { get; set; } = string.Empty;
    public string CatalogPath { get; set; } = string.Empty;
    public string TopicKind { get; set; } = string.Empty;
    public string ContextJson { get; set; } = string.Empty;
}
```

Update `MasterDbContext` to:
- expose `DbSet<DocTopicContext>`
- add a unique index on `(BranchLanguageId, CatalogPath)`

Generate migrations for both providers with:

```bash
dotnet ef migrations add AddDocTopicContexts --project src/EFCore/OpenDeepWiki.Sqlite/OpenDeepWiki.Sqlite.csproj --startup-project src/OpenDeepWiki/OpenDeepWiki.csproj --context OpenDeepWiki.Sqlite.SqliteDbContext --output-dir Migrations
dotnet ef migrations add AddDocTopicContexts --project src/EFCore/OpenDeepWiki.Postgresql/OpenDeepWiki.Postgresql.csproj --startup-project src/OpenDeepWiki/OpenDeepWiki.csproj --context OpenDeepWiki.Postgresql.PostgresqlDbContext --output-dir Migrations
```

Rename the generated files to the exact filenames listed above if the scaffolded timestamp differs.

- [ ] **Step 4: Run the targeted tests to verify they pass**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter "WorkflowTopicContextServiceTests|WorkflowCatalogAugmenterTests"
```

Expected: PASS

---

## Chunk 3: Generator Integration And Routing

### Task 5: Integrate workflow discovery into full catalog generation

**Files:**
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowDiscoveryOptions.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Modify: `src/OpenDeepWiki/appsettings.json`
- Modify: `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDiscoveryServiceTests.cs`

- [ ] **Step 1: Write the failing integration-style tests**

Cover these behaviors:
- after normal catalog generation, workflow discovery runs and injects workflow pages
- discovered workflow pages persist their topic contexts
- when discovery returns no candidates, the original catalog is preserved

Avoid live AI calls. Mock or stub:
- `IPromptPlugin`
- `AgentFactory`
- semantic provider output

- [ ] **Step 2: Run the targeted tests to verify they fail**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDiscoveryServiceTests
```

Expected: FAIL because `WikiGenerator` does not yet run discovery or persist topic contexts.

- [ ] **Step 3: Implement the generator integration**

Add `WorkflowDiscoveryOptions` with defaults such as:
- `Enabled = true`
- `MinimumScore = 45`
- `TopWorkflowCount = 8`
- `MaxEvidenceFilesPerWorkflow = 20`
- `FallbackToSyntaxOnly = true`

Register everything in `Program.cs`:
- `WorkflowDiscoveryOptions`
- `IWorkflowDiscoveryService`
- `IWorkflowSemanticProvider`
- `WorkflowCatalogAugmenter`
- `WorkflowTopicContextService`
- `WorkflowDocumentRouteResolver`

In `WikiGenerator.GenerateCatalogAsync`:
- load the AI-produced catalog as JSON
- call a new helper such as `DiscoverAndMergeWorkflowCatalogAsync`
- merge the workflow pages into the catalog tree
- overwrite the catalog with the augmented structure
- clear and upsert topic contexts for the current `BranchLanguage`

- [ ] **Step 4: Run the targeted tests to verify they pass**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDiscoveryServiceTests
```

Expected: PASS

---

### Task 6: Route workflow pages to a dedicated prompt and refresh them during incremental updates

**Files:**
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowDocumentRouteResolver.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDocumentRoutingTests.cs`

- [ ] **Step 1: Write the failing routing tests**

Cover these behaviors:
- a page with `TopicKind = Workflow` uses `workflow-content-generator.md`
- a page without topic context uses the existing `content-generator.md`
- workflow prompt variables include:
  - `workflow_name`
  - `workflow_summary`
  - `actors`
  - `trigger_points`
  - `request_entities`
  - `scheduler_files`
  - `executor_files`
  - `service_files`
  - `evidence_files`
  - `seed_queries`
- incremental update reruns workflow discovery when changed files include C# sources
- incremental update regenerates workflow pages after discovery refresh

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDocumentRoutingTests
```

Expected: FAIL because all pages currently route through the standard content prompt and incremental update never refreshes workflow metadata.

- [ ] **Step 3: Implement routing and incremental refresh**

In `WikiGenerator.GenerateDocumentsAsync`:
- resolve workflow vs module pages before dispatching each leaf document
- route workflow pages to a new `GenerateWorkflowDocumentContentAsync`

In `WikiGenerator.IncrementalUpdateAsync`:
- if `changedFiles` contain any `*.cs`, `*.csproj`, or `*.sln`, rerun the same workflow discovery helper used by full generation
- refresh `DocTopicContext`
- regenerate all workflow pages after catalog refresh

Do not try to implement fine-grained affected-workflow selection in this iteration. Regenerate all workflow pages for correctness.

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDocumentRoutingTests
```

Expected: PASS

---

## Chunk 4: Workflow Prompt And Verification

### Task 7: Add the workflow-specific prompt and validate evidence-first behavior

**Files:**
- Create: `src/OpenDeepWiki/prompts/workflow-content-generator.md`
- Modify: `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs`
- Modify: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDocumentRoutingTests.cs`

- [ ] **Step 1: Write the failing prompt-variable assertions**

Extend `WorkflowDocumentRoutingTests` to assert that workflow generation:
- reads `evidence_files` first
- carries `seed_queries` into the prompt variables
- requires Mermaid sequence or flow output
- requires sections for trigger, persistence, scheduling, dispatch, execution, status, and failure handling

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDocumentRoutingTests
```

Expected: FAIL because the workflow prompt file and prompt-building logic do not exist yet.

- [ ] **Step 3: Implement the prompt and prompt loading**

Create `workflow-content-generator.md` with hard requirements for:
- H1 title and overview
- actors
- trigger entry
- request-table or persistence section
- scheduler or polling section
- executor dispatch section
- state transitions
- exception and compensation section
- source-linked code examples
- Mermaid sequence or flow diagram

Make `GenerateWorkflowDocumentContentAsync` load this prompt and build the user message from `DocTopicContext`.

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter WorkflowDocumentRoutingTests
```

Expected: PASS

---

### Task 8: Run focused and full verification

**Files:**
- Solution: `OpenDeepWiki.sln`

- [ ] **Step 1: Run all workflow-focused tests**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter "RoslynWorkflowSemanticProviderTests|WorkflowCandidateExtractorTests|WorkflowTopicContextServiceTests|WorkflowCatalogAugmenterTests|WorkflowDiscoveryServiceTests|WorkflowDocumentRoutingTests"
```

Expected: PASS

- [ ] **Step 2: Run the full test project**

Run:

```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj
```

Expected: PASS, or document any unrelated pre-existing failures before touching workflow code again.

- [ ] **Step 3: Build the backend solution**

Run:

```bash
dotnet build OpenDeepWiki.sln
```

Expected: PASS

- [ ] **Step 4: Perform one manual workflow acceptance pass on a real WMS repository**

Use `WmsServerV4Dev` or an equivalent `.NET` sample and verify:
- catalog contains `核心业务流程`
- one page appears for `容器托盘入库流程`
- generated content explicitly documents:
  - `Wcs -> 请求表 -> 定时任务 -> executor -> 入库执行`
- the page includes concrete source references and a Mermaid flow or sequence diagram

Document any mismatch before closing the work.

---

## Notes For The Implementer

- Prefer adding new workflow classes under `src/OpenDeepWiki/Services/Wiki/` rather than making `WikiGenerator.cs` even more monolithic.
- Do not add LSP execution code in this iteration. Only keep the `IWorkflowSemanticProvider` abstraction clean enough that an LSP-backed provider can be added later.
- Keep the workflow discovery path deterministic and testable. AI should only be used when generating the final workflow page, not when deciding whether a workflow exists.
- If Roslyn cannot load a project, log the failure and continue with the remaining projects. Do not fail the whole repository generation because one project file is malformed.
- Do not mutate the existing `CatalogItem` JSON schema for workflow metadata. `DocTopicContext` is the only persistence layer for this extra semantics in this iteration.

---

Plan complete and saved to `docs/superpowers/plans/2026-03-25-workflow-discovery-implementation.md`. Ready to execute?
