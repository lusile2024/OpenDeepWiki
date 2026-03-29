# Workflow LSP ACP Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把现有 workflow workbench 从 Roslyn-only augment + 同步深挖，升级成带 external LSP 增强、ACP 异步执行和运行态可视化的三层实现。

**Architecture:** 保留现有 workflow discovery 与 topic context 主链，只做增量改造。第一部分升级 profile schema 和前后端 DTO；第二部分实现 external-first 的 LSP augment；第三部分把 analysis session 改造成队列 + worker + logs 的后台异步执行链，并把运行态接到管理端工作台。

**Tech Stack:** ASP.NET Core 10, EF Core 10, Roslyn, stdio JSON-RPC, BackgroundService, Next.js, TypeScript.

---

## 基线说明

当前仓库已经存在这些实现，不重复重做：

- `RoslynWorkflowSemanticProvider`
- `WorkflowLspAugmentService`
- `WorkflowChapterSliceBuilder`
- `WorkflowDeepAnalysisService`
- `WorkflowTemplateAnalysisService`
- `repository-workflow-template-workbench.tsx`
- `workflow-analysis-panel.tsx`
- `WorkflowAnalysisSession / Task / Artifact`

本计划只写当前需要补的增量任务。

## Task 1: 升级 profile schema 与前后端类型

**Files:**
- Modify: `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/RepositoryWorkflowConfigRules.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowDeepAnalysisService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowChapterSliceBuilder.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Modify: `web/types/workflow-config.ts`
- Modify: `web/components/admin/repository-workflow-template-workbench.tsx`

- [ ] 补 `entryRoots`、`entryKinds`、namespace/stop pattern、chapter `analysisMode`、章节图开关、LSP 请求选项、ACP `splitStrategy`
- [ ] 在规则清洗中补默认值和去重逻辑
- [ ] 让现有服务在不填新字段时保持兼容
- [ ] 更新前端类型与当前草稿展示

## Task 2: 实现 external LSP client

**Files:**
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspOptions.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspProtocolModels.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/IWorkflowExternalLspClient.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/Lsp/WorkflowExternalLspClient.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Modify: `src/OpenDeepWiki/appsettings.json`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowExternalLspClientTests.cs`

- [ ] 建立最小 stdio JSON-RPC 通道
- [ ] 支持 `initialize`、`initialized`、`definition`、`references`、`prepareCallHierarchy`、`incomingCalls`、`outgoingCalls`
- [ ] 支持超时、取消、失败诊断、关闭配置
- [ ] 补对应后端测试

## Task 3: 重构 augment service 为 external-first

**Files:**
- Modify: `src/OpenDeepWiki/Services/Wiki/IWorkflowLspAugmentService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowSemanticGraph.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/RoslynWorkflowSemanticProvider.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowLspAugmentService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Modify: `src/OpenDeepWiki/Models/Admin/WorkflowAnalysisModels.cs`
- Modify: `web/types/workflow-config.ts`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowLspAugmentServiceTests.cs`

- [ ] 给语义节点补可用于 LSP 请求的位置数据
- [ ] 扩展 augment result，补 fallback reason / diagnostics / resolved definitions / resolved references
- [ ] external LSP 成功时优先用真实结果组装建议
- [ ] external LSP 失败时稳定退回 Roslyn fallback
- [ ] 更新 API DTO 和前端类型
- [ ] 补 augment 服务测试

## Task 4: 引入 analysis log 与队列服务

**Files:**
- Create: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisLog.cs`
- Modify: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisSession.cs`
- Modify: `src/OpenDeepWiki.Entities/Repositories/WorkflowAnalysisTask.cs`
- Modify: `src/OpenDeepWiki.EFCore/MasterDbContext.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisQueueModels.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/IWorkflowAnalysisQueueService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisQueueService.cs`
- Modify: `src/EFCore/OpenDeepWiki.Sqlite/Migrations/SqliteDbContextModelSnapshot.cs`
- Modify: `src/EFCore/OpenDeepWiki.Postgresql/Migrations/PostgresqlDbContextModelSnapshot.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisQueueServiceTests.cs`

- [ ] 给 session 补 `QueuedAt`、`CurrentTaskId`、`ProgressMessage`
- [ ] 新增 `WorkflowAnalysisLog`
- [ ] 实现会话入队、领取、幂等保护
- [ ] 补数据库模型和迁移
- [ ] 补队列测试

## Task 5: 把深挖改成后台 worker 执行

**Files:**
- Create: `src/OpenDeepWiki/Services/Wiki/IWorkflowAnalysisExecutionService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisExecutionService.cs`
- Create: `src/OpenDeepWiki/Services/Wiki/WorkflowAnalysisWorker.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`
- Modify: `src/OpenDeepWiki/Program.cs`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowAnalysisExecutionServiceTests.cs`

- [ ] `CreateAnalysisSessionAsync` 改成只创建 queued session + pending tasks
- [ ] worker 负责领取 queued session 并执行
- [ ] 执行过程中逐步写入状态、任务完成数、当前任务、progressMessage、artifact、logs
- [ ] 仅 completed session 回流 topic context 快照
- [ ] 补 execution service 测试

## Task 6: 扩展分析 API 与前端轮询

**Files:**
- Modify: `src/OpenDeepWiki/Endpoints/Admin/AdminRepositoryEndpoints.cs`
- Modify: `src/OpenDeepWiki/Models/Admin/WorkflowAnalysisModels.cs`
- Modify: `src/OpenDeepWiki/Services/Wiki/WorkflowTemplateAnalysisService.cs`
- Modify: `web/lib/admin-api.ts`
- Modify: `web/types/workflow-config.ts`
- Modify: `web/components/admin/repository-workflow-template-workbench.tsx`
- Modify: `web/components/admin/workflow-analysis-panel.tsx`
- Create: `tests/OpenDeepWiki.Tests/Services/Wiki/AdminRepositoryWorkflowAnalysisEndpointsTests.cs`

- [ ] 新增 analysis logs 接口
- [ ] detail DTO 增加运行态与最近日志
- [ ] 前端对 `Queued/Running` 会话自动轮询 detail 和 logs
- [ ] 面板增加进度、当前 task、日志列表和运行中提示
- [ ] 补接口测试

## Task 7: 跑验证

**Files:**
- Solution: `OpenDeepWiki.sln`

- [ ] 运行 workflow 相关新增测试
- [ ] 运行 `dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj`
- [ ] 运行 `dotnet build OpenDeepWiki.sln`
- [ ] 运行前端定向 lint

## 待优化事项

以下事项不阻塞当前三期实现闭环，但建议作为下一轮优化 backlog 持续推进：

- [ ] 让 `LSP augment` 在成功时真正回填 `chapterProfiles`、`analysis.rootSymbolNames`、`analysis.mustExplainSymbols`，避免 profile 仍停留在“只开了 LSP 开关但章节配置为空”的状态。
- [ ] 把 `ACP 深挖` 的默认交互改成“针对当前已选业务流执行整条主线和支线分析”；章节下拉保留为“单章节补跑/聚焦重跑”，而不是主入口。
- [ ] 在前端分析面板中补更强的运行态可视化，包括当前执行任务、最近日志、并行任务分布、AI 生成结果与基础种子的关系说明，而不是只给一次性提示。
- [ ] 给 `WorkflowAnalysisTaskRunner` 增加更细粒度的运行诊断，例如 prompt 加载失败、AI 未配置、响应解析失败、fallback 原因分类，以及 artifact 级别的来源标记展示。
- [ ] 为 `external LSP` 增加“definition + callHierarchy 优先、references 按需启用”的执行策略配置，并把 references 作为重型补充查询而不是默认路径。
- [ ] 增加 cross-project 场景验证，确认在 solution 内跨项目符号跳转时，`definition/callHierarchy` 足以覆盖主链定位；仅在确有必要时再启用 references 补边。
- [ ] 补 `coverage validator` 的缺项回补链路，让缺失章节、缺失 `mustExplainSymbols`、缺图等问题可以只重跑单章或单分支，而不是整条业务流重建。
- [ ] 为 ACP 结果增加“整条业务流视图”和“单章节视图”的统一切换，确保整流分析与章节聚焦不会在 UI 语义上互相干扰。
- [ ] 在任务编排层保留 deterministic planner 为最终任务图生成器，后续如要引入 `AI planner hint`，只允许它补建议章节、建议 branch topic 和 must-explain symbols，不直接接管最终任务拆分。
- [ ] 给 `task_runner` 的 AI 输出增加审计信息落库，例如模型名、请求类型、是否命中 fallback、AI 生成耗时，方便后续评估真实模型接入收益。

---

Plan complete and saved to `docs/superpowers/plans/2026-03-27-workflow-lsp-acp-implementation.md`.
