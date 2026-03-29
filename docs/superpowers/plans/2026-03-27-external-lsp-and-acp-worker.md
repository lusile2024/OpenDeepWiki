# External LSP Client And ACP Async Worker Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把业务流增强从 Roslyn fallback 升级成真实 external LSP client，并把 ACP 深挖从同步接口升级成后台异步 worker + 页面实时进度轮询。

**Architecture:** `WorkflowLspAugmentService` 改成“external LSP first, Roslyn fallback”编排层；真实 LSP 通信通过独立的 stdio client 服务完成。ACP 深挖改成“创建会话只入队，后台 worker 执行、逐步写状态/日志/artifact，前端按运行态轮询详情与日志”。

**Tech Stack:** ASP.NET Core, EF Core, BackgroundService, Roslyn semantic graph, external LSP over stdio/JSON-RPC, Next.js, TypeScript.

---

## Chunk 1: External LSP 基础设施

### Task 1: 为 external LSP client 建立后端传输层

**Files:**
- Modify: `src/OpenDeepWiki/OpenDeepWiki.csproj`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspOptions.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspProtocolModels.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspClient.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/IWorkflowExternalLspClient.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowExternalLspClientTests.cs`

- [ ] **Step 1: 给后端项目补 LSP transport 所需依赖**

在 `src/OpenDeepWiki/OpenDeepWiki.csproj` 增加用于 stdio JSON-RPC 的包引用。优先使用轻量 transport 包，不直接把 protocol 逻辑写死在 `WorkflowLspAugmentService` 里。

- [ ] **Step 2: 新增 external LSP 运行时配置**

在 `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspOptions.cs` 定义：
- `Enabled`
- `Command`
- `Arguments`
- `WorkingDirectoryMode`
- `InitializeTimeoutMs`
- `RequestTimeoutMs`
- `MaxConcurrentRequests`
- `TracePayloads`

要求支持按配置关闭 external LSP，关闭时直接走 Roslyn fallback。

- [ ] **Step 3: 新建最小 LSP 协议 DTO**

在 `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspProtocolModels.cs` 定义当前实现必需的最小请求/响应结构：
- `initialize`
- `initialized`
- `textDocument/definition`
- `textDocument/references`
- `textDocument/prepareCallHierarchy`
- `callHierarchy/incomingCalls`
- `callHierarchy/outgoingCalls`

不要一次性引入不需要的完整协议面。

- [ ] **Step 4: 实现 external LSP client 接口**

在 `src/OpenDeepWiki/Services/Wiki/Lsp/IWorkflowExternalLspClient.cs` 和 `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspClient.cs` 中实现：
- 启动外部 LSP 进程
- 发送 `initialize/initialized`
- 按文件 URI + 行列发起 definition / references / call hierarchy 请求
- 超时、取消、进程退出、响应为空时返回结构化失败结果

要求该服务不直接依赖业务流 profile，只负责“给定代码位置，返回 LSP 解析结果”。

- [ ] **Step 5: 在 DI 中注册 external LSP client**

在 `src/OpenDeepWiki/Program.cs` 中注册 options 与 `IWorkflowExternalLspClient`。

- [ ] **Step 6: 为 external LSP client 写最小可替换测试**

在 `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowExternalLspClientTests.cs` 中写针对 fake process / fake transport 的测试，至少覆盖：
- 配置关闭时不启动进程
- 初始化失败时返回失败结果
- 请求超时时返回失败结果

- [ ] **Step 7: 运行定向测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --filter WorkflowExternalLspClientTests`

Expected: 新增 external LSP client 测试通过。

### Task 2: 把 augment 服务升级成 external-first 编排层

**Files:**
- Modify: `src/OpenDeepWiki/Services/Wiki/IWorkflowLspAugmentService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigRules.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowLspAugmentServiceTests.cs`

- [ ] **Step 1: 扩充 augment 结果模型**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentModels.cs` 增加：
- `FallbackReason`
- `LspServerName`
- `Diagnostics`
- `ResolvedDefinitions`
- `ResolvedReferences`

保留现有 `Strategy` 字段，用于区分 `external-lsp`、`roslyn-fallback`、`disabled`。

- [ ] **Step 2: 为 profile 的 LSP 配置补必要字段**

在 `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigModels.cs` 的 `WorkflowLspAssistOptions` 中补：
- `RequestTimeoutMs`
- `EnableDefinitionLookup`
- `EnableReferenceLookup`
- `EnablePrepareCallHierarchy`

并在 `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigRules.cs` 中完成规范化和默认值。

- [ ] **Step 3: 重构 WorkflowLspAugmentService**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentService.cs` 中拆成两层：
- `TryExternalAugmentAsync(...)`
- `BuildRoslynFallbackAugment(...)`

逻辑要求：
- external LSP 成功时，优先使用 LSP definition/reference/call hierarchy 结果构建 root symbol、must explain、call hierarchy edges
- external LSP 失败时，记录 `FallbackReason`，退回现有 Roslyn 逻辑
- 不允许因为 external LSP 失败而让整个工作台报错

- [ ] **Step 4: 更新工作台消息和风险说明**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs` 中把当前固定写死的“Roslyn fallback”提示改成根据实际 strategy 动态输出。

- [ ] **Step 5: 为 augment 编排层补测试**

在 `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowLspAugmentServiceTests.cs` 覆盖：
- external LSP 成功路径
- external LSP 失败后 fallback 到 Roslyn
- profile 显式关闭 LSP 时只走 fallback

- [ ] **Step 6: 运行定向测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --filter WorkflowLspAugmentServiceTests`

Expected: augment 编排测试通过。

## Chunk 2: ACP 后台异步执行

### Task 3: 为分析会话补运行态、日志和队列模型

**Files:**
- Modify: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisSession.cs`
- Modify: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisTask.cs`
- Create: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisLog.cs`
- Modify: `src/OpenDeepWiki.EFCore/MasterDbContext.cs`
- Modify: `src/OpenDeepWiki/Models/Admin/WorkflowAnalysisModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisQueueModels.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/IWorkflowAnalysisQueueService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisQueueService.cs`
- Modify: `src/EFCore/OpenDeepWiki.Sqlite/Migrations/*`
- Modify: `src/EFCore/OpenDeepWiki.Postgresql/Migrations/*`
- Test: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisQueueServiceTests.cs`

- [ ] **Step 1: 为分析会话补充状态字段**

在 `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisSession.cs` 中增加或规范：
- `Status` 允许 `Queued / Running / Completed / Failed / Cancelled`
- `QueuedAt`
- `StartedAt`
- `CompletedAt`
- `LastActivityAt`
- `CurrentTaskId`
- `ProgressMessage`

- [ ] **Step 2: 为分析任务补充逐步执行状态**

在 `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisTask.cs` 中确保支持：
- `Pending / Running / Completed / Failed / Skipped`
- `StartedAt / CompletedAt / ErrorMessage`

- [ ] **Step 3: 新增分析日志实体**

在 `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisLog.cs` 创建分析日志表，字段至少包括：
- `AnalysisSessionId`
- `TaskId`
- `Level`
- `Message`
- `CreatedAt`

用途是前端轮询“最近日志”，不要复用仓库级 `RepositoryProcessingLog`。

- [ ] **Step 4: 注册 DbSet 和迁移**

在 `src/OpenDeepWiki.EFCore/MasterDbContext.cs` 注册新实体，并生成 SQLite / PostgreSQL 迁移。

- [ ] **Step 5: 新建队列模型和入队服务**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisQueueModels.cs`、`IWorkflowAnalysisQueueService.cs`、`WorkflowAnalysisQueueService.cs` 中定义：
- 入队 payload
- 出队 payload
- 幂等检查
- 取消扩展点

要求“创建深挖会话”只负责落库和入队，不同步执行分析。

- [ ] **Step 6: 为队列服务补测试**

在 `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisQueueServiceTests.cs` 中覆盖：
- 新会话能入队
- 已完成会话不能重复入队
- 运行中会话不会被重复领取

- [ ] **Step 7: 运行定向测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --filter WorkflowAnalysisQueueServiceTests`

Expected: 队列状态测试通过。

### Task 4: 实现分析执行 worker 和运行日志

**Files:**
- Create: `src/OpenDeepWiki/Services/Wiki/IWorkflowAnalysisExecutionService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisExecutionService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisWorker.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisExecutionServiceTests.cs`

- [ ] **Step 1: 提取分析执行服务**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisExecutionService.cs` 中实现：
- 读取会话与草稿
- 准备 workspace
- 运行 discovery
- 运行 deep analysis
- 分步更新 task 状态
- 逐步写 artifact
- 写分析日志

要求 worker 与 controller/service 不共享 DbContext。

- [ ] **Step 2: 创建 WorkflowAnalysisWorker**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisWorker.cs` 中仿照 `RepositoryProcessingWorker` 实现轮询：
- 拉取 `Queued` 会话
- 切换为 `Running`
- 调用 execution service
- 完成后置为 `Completed`
- 失败后置为 `Failed`

- [ ] **Step 3: 改造 CreateAnalysisSessionAsync**

在 `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs` 中把 `CreateAnalysisSessionAsync` 从“同步执行并立即完成”改成：
- 创建 `Queued` session
- 创建 `Pending` tasks
- 写首条日志
- 入队
- 返回 session detail

- [ ] **Step 4: 在 Program 中注册 worker 和执行服务**

在 `src/OpenDeepWiki/Program.cs` 注册：
- `IWorkflowAnalysisQueueService`
- `IWorkflowAnalysisExecutionService`
- `WorkflowAnalysisWorker`

- [ ] **Step 5: 为执行服务补测试**

在 `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisExecutionServiceTests.cs` 中覆盖：
- queued -> running -> completed 状态流转
- 执行失败时进入 failed
- artifact 和日志落库

- [ ] **Step 6: 运行定向测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --filter WorkflowAnalysisExecutionServiceTests`

Expected: 状态流转与 artifact 持久化测试通过。

## Chunk 3: ACP 管理端 API 与前端实时进度

### Task 5: 提供分析日志与实时详情接口

**Files:**
- Modify: `src/OpenDeepWiki/Endpoints/Admin/AdminRepositoryEndpoints.cs`
- Modify: `src/OpenDeepWiki/Models/Admin/WorkflowAnalysisModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Test: `tests/OpenDeepWiki.Tests/Endpoints/Admin/AdminRepositoryWorkflowAnalysisEndpointsTests.cs`

- [ ] **Step 1: 为分析详情 DTO 增加运行态字段**

在 `src/OpenDeepWiki/Models/Admin/WorkflowAnalysisModels.cs` 中补充：
- `currentTaskId`
- `progressMessage`
- `runningTaskCount`
- `pendingTaskCount`
- `recentLogs`

- [ ] **Step 2: 新增获取分析日志接口**

在 `src/OpenDeepWiki/Endpoints/Admin/AdminRepositoryEndpoints.cs` 增加：
- `GET /api/admin/repositories/{id}/workflow-template/sessions/{sessionId}/analysis-sessions/{analysisSessionId}/logs`

支持 `since` 和 `limit` 参数，仿照仓库处理日志接口。

- [ ] **Step 3: 改造获取分析详情接口**

让现有 analysis detail 接口返回最新任务进度和最近日志摘要，避免前端必须一次性全量刷新所有数据。

- [ ] **Step 4: 补接口测试**

在 `tests/OpenDeepWiki.Tests/Endpoints/Admin/AdminRepositoryWorkflowAnalysisEndpointsTests.cs` 覆盖：
- 创建会话返回 queued
- detail 接口在运行中返回 progressMessage
- logs 接口返回按时间排序的日志

- [ ] **Step 5: 运行定向测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --filter AdminRepositoryWorkflowAnalysisEndpointsTests`

Expected: 管理端分析接口测试通过。

### Task 6: 前端接入 ACP 实时轮询与运行日志

**Files:**
- Modify: `web/lib/admin-api.ts`
- Modify: `web/types/workflow-config.ts`
- Modify: `web/components/admin/repository-workflow-template-workbench.tsx`
- Modify: `web/components/admin/workflow-analysis-panel.tsx`
- Test: `web/components/admin/repository-workflow-template-workbench.tsx` (lint)
- Test: `web/components/admin/workflow-analysis-panel.tsx` (lint)
- Test: `web/lib/admin-api.ts` (lint)
- Test: `web/types/workflow-config.ts` (lint)

- [ ] **Step 1: 补 analysis log API 客户端**

在 `web/lib/admin-api.ts` 中新增：
- `getRepositoryWorkflowAnalysisSessionLogs(...)`

并更新 detail 类型映射。

- [ ] **Step 2: 扩充前端类型**

在 `web/types/workflow-config.ts` 中为分析会话增加：
- `currentTaskId`
- `progressMessage`
- `recentLogs`
- `pendingTaskCount`
- `runningTaskCount`

- [ ] **Step 3: 为运行中会话增加自动轮询**

在 `web/components/admin/repository-workflow-template-workbench.tsx` 中新增：
- 当 `status` 为 `Queued` 或 `Running` 时，按固定间隔轮询 detail / logs
- 完成、失败、取消时自动停止轮询
- 页面刷新后恢复已选会话的轮询状态

- [ ] **Step 4: 在面板中展示进度与日志**

在 `web/components/admin/workflow-analysis-panel.tsx` 中增加：
- 任务进度条
- 当前执行 task
- 最近日志列表
- 运行中 badge / 自动刷新提示

日志面板默认展开，不自动隐藏。

- [ ] **Step 5: 跑前端定向 lint**

Run: `cd "D:\VSWorkshop\OpenDeepWiki\web" && npx eslint components/admin/repository-workflow-template-workbench.tsx components/admin/workflow-analysis-panel.tsx lib/admin-api.ts types/workflow-config.ts`

Expected: 相关前端文件 lint 通过。

## Chunk 4: 集成验证与回归

### Task 7: 端到端验证 external LSP + ACP 异步深挖

**Files:**
- Modify: `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`
- Test: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowTopicContextServiceTests.cs`

- [ ] **Step 1: 确认文档生成链消费异步分析产物**

检查并必要时补充 `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs` 与 `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`，确保：
- 运行完成后的最新 analysis session 能回流到正式 workflow doc 生成链
- 运行中不会污染历史已完成快照

- [ ] **Step 2: 写 topic context 回流测试**

在 `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowTopicContextServiceTests.cs` 覆盖：
- 仅消费 `Completed` analysis session
- 多会话场景优先最近完成结果

- [ ] **Step 3: 跑后端编译**

Run: `dotnet msbuild "D:\VSWorkshop\OpenDeepWiki\src\OpenDeepWiki\OpenDeepWiki.csproj" /target:Compile`

Expected: 后端编译通过。

- [ ] **Step 4: 跑后端测试**

Run: `dotnet test "D:\VSWorkshop\OpenDeepWiki\tests\OpenDeepWiki.Tests\OpenDeepWiki.Tests.csproj" --no-restore`

Expected: 新增相关测试通过；若有历史失败，单独记录，不混入本次变更。

- [ ] **Step 5: 手工验证工作台**

手工验证顺序：
1. 创建 workflow template session
2. 点击“增强当前草稿”，确认 strategy 能显示 `external-lsp` 或 fallback 原因
3. 点击“发起 ACP 深挖”，确认先进入 `Queued/Running`
4. 观察前端自动刷新任务数、当前任务、最近日志
5. 完成后确认 artifact 回流到正式 workflow 文档重建

- [ ] **Step 6: 提交代码**

```bash
git add src/OpenDeepWiki/OpenDeepWiki.csproj src/OpenDeepWiki/Program.cs src/OpenDeepWiki/Services/Wiki src/OpenDeepWiki/Endpoints/Admin/AdminRepositoryEndpoints.cs src/OpenDeepWiki/Models/Admin src/OpenDeepWiki.EFCore src/OpenDeepWiki.Entities web/lib/admin-api.ts web/types/workflow-config.ts web/components/admin/repository-workflow-template-workbench.tsx web/components/admin/workflow-analysis-panel.tsx tests/OpenDeepWiki.Tests
git commit -m "feat: add external workflow lsp and async analysis worker"
```

