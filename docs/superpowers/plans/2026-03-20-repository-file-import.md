# Repository File Import Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ZIP archive upload and server local-directory import as first-class repository sources for wiki generation, alongside the existing Git URL flow.

**Architecture:** Extend the repository domain with explicit source metadata so the system can distinguish Git, archive, and local-directory repositories. Keep the current processing pipeline (`RepositoryProcessingWorker -> RepositoryAnalyzer -> WikiGenerator`) intact, but make workspace preparation branch by source type. Git keeps clone/pull and incremental behavior; archive and local-directory sources prepare isolated workspaces via extract/copy or optional link mode and run full generation only.

**Tech Stack:** ASP.NET Core 10, EF Core, Next.js App Router, xUnit, FsCheck

---

## Files to Modify

| File | Change |
|------|--------|
| `src/OpenDeepWiki.Entities/Repositories/Repository.cs` | Add source-type metadata for Git/archive/local-directory repositories |
| `src/OpenDeepWiki/Models/RepositorySubmitRequest.cs` | Keep Git submit contract stable and add DTOs for archive/local-directory submit flows |
| `src/OpenDeepWiki/Services/Repositories/RepositoryService.cs` | Add new submit methods, validation, and repository creation for archive/local sources |
| `src/OpenDeepWiki/Services/Repositories/RepositoryAnalyzer.cs` | Prepare workspaces by source type, including archive extract and local-directory copy/link |
| `src/OpenDeepWiki/Services/Repositories/IRepositoryAnalyzer.cs` | Keep workspace contract aligned with new source metadata |
| `src/OpenDeepWiki/Program.cs` | Register any new services/options needed for file import |
| `src/OpenDeepWiki/appsettings.json` | Add local import options defaults if needed |
| `web/types/repository.ts` | Add source-type request/response types |
| `web/lib/repository-api.ts` | Add API calls for archive and local-directory submit flows |
| `web/components/repo/repository-submit-form.tsx` | Add source-type switch and source-specific form fields |
| `web/components/repo/repository-not-found.tsx` | Keep Git submit call aligned with the updated API types |
| `tests/OpenDeepWiki.Tests/Services/Repositories/*` | Add failing tests for submit validation and workspace preparation |

---

## Chunk 1: Domain And Service Contract

### Task 1: Add failing tests for repository source metadata and submit rules

**Files:**
- Create: `tests/OpenDeepWiki.Tests/Services/Repositories/RepositorySourceSubmitTests.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Repositories/RepositorySourceSubmitTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover these behaviors:
- Git submissions still require `GitUrl`
- Archive submissions require a stored source path and default branch
- Local-directory submissions require a local source path and default branch
- Local-directory submissions reject paths outside the configured allowlist

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositorySourceSubmitTests
```

Expected: FAIL because source metadata and new submit behaviors do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Implement:
- `RepositorySourceType` enum in the repository entity
- DTOs for archive/local submit
- validation logic in `RepositoryService`

- [ ] **Step 4: Run test to verify it passes**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositorySourceSubmitTests
```

Expected: PASS

---

### Task 2: Add failing tests for workspace preparation by source type

**Files:**
- Create: `tests/OpenDeepWiki.Tests/Services/Repositories/RepositoryAnalyzerSourceTests.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Repositories/RepositoryAnalyzerSourceTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover these behaviors:
- Archive source extracts into an isolated working directory
- Local-directory source copies into an isolated working directory by default
- Local-directory source can use link mode when enabled
- Non-Git sources report non-incremental workspaces

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositoryAnalyzerSourceTests
```

Expected: FAIL because `RepositoryAnalyzer` only knows how to clone/pull Git repositories.

- [ ] **Step 3: Write minimal implementation**

Implement source-specific workspace preparation helpers in `RepositoryAnalyzer`.

- [ ] **Step 4: Run test to verify it passes**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositoryAnalyzerSourceTests
```

Expected: PASS

---

## Chunk 2: Backend Endpoints And Options

### Task 3: Add archive and local-directory submit endpoints

**Files:**
- Modify: `src/OpenDeepWiki/Services/Repositories/RepositoryService.cs`
- Modify: `src/OpenDeepWiki/Models/RepositorySubmitRequest.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Modify: `src/OpenDeepWiki/appsettings.json`

- [ ] **Step 1: Add failing tests for endpoint-facing service methods**

Extend `RepositorySourceSubmitTests` to cover:
- archive submit stores metadata and returns a pending repository
- local submit stores metadata and returns a pending repository
- Git submit remains backward compatible

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositorySourceSubmitTests
```

Expected: FAIL because the service methods and options are incomplete.

- [ ] **Step 3: Implement the service methods and options**

Add:
- `SubmitArchiveAsync`
- `SubmitLocalDirectoryAsync`
- controlled storage for uploaded ZIP files
- allowlist and import-mode options for local directories

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositorySourceSubmitTests
```

Expected: PASS

---

### Task 4: Keep processing-worker behavior safe for non-Git sources

**Files:**
- Modify: `src/OpenDeepWiki/Services/Repositories/RepositoryProcessingWorker.cs`
- Modify: `src/OpenDeepWiki/Services/Repositories/IRepositoryAnalyzer.cs`

- [ ] **Step 1: Add a failing test for non-Git incremental behavior**

Extend `RepositoryAnalyzerSourceTests` or add a new test to assert:
- archive/local sources do not attempt incremental update logic based on Git commits

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositoryAnalyzerSourceTests
```

Expected: FAIL because the current workspace contract always assumes Git commit semantics.

- [ ] **Step 3: Implement the minimal worker/analyzer adjustments**

Update workspace metadata so non-Git sources are treated as full-generation only.

- [ ] **Step 4: Run the targeted test to verify it passes**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter RepositoryAnalyzerSourceTests
```

Expected: PASS

---

## Chunk 3: Frontend Submission Flow

### Task 5: Add source-type form handling and API clients

**Files:**
- Modify: `web/types/repository.ts`
- Modify: `web/lib/repository-api.ts`
- Modify: `web/components/repo/repository-submit-form.tsx`
- Modify: `web/components/repo/repository-not-found.tsx`

- [ ] **Step 1: Add the failing frontend tests if practical**

If an existing test harness can cover the form:
- add tests for source-type switching and request payload selection

If no practical harness exists:
- document manual verification steps inside the implementation notes and keep backend tests authoritative

- [ ] **Step 2: Implement the minimal frontend changes**

Add:
- source selector for `Git / ZIP / Local Directory`
- file picker for ZIP submit
- path input for local-directory submit
- Git-only branch discovery
- default `main` branch for archive/local sources

- [ ] **Step 3: Run frontend verification**

Run:
```bash
cd web && npm run lint
```

Expected: PASS

---

## Chunk 4: End-To-End Verification

### Task 6: Run focused backend verification

**Files:**
- Solution: `OpenDeepWiki.sln`

- [ ] **Step 1: Run the repository-source tests**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter "RepositorySourceSubmitTests|RepositoryAnalyzerSourceTests"
```

Expected: PASS

- [ ] **Step 2: Run the full backend test project if focused tests pass**

Run:
```bash
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj
```

Expected: PASS or known unrelated failures captured in notes

---

### Task 7: Run build/lint verification

**Files:**
- Solution: `OpenDeepWiki.sln`
- Frontend: `web/`

- [ ] **Step 1: Build backend**

Run:
```bash
dotnet build OpenDeepWiki.sln
```

Expected: PASS

- [ ] **Step 2: Lint frontend**

Run:
```bash
cd web && npm run lint
```

Expected: PASS

