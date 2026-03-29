# Workflow Profile 三层增强设计

## 背景

OpenDeepWiki 当前已经具备业务流发现与工作台草稿能力：

- `RoslynWorkflowSemanticProvider` 已能构建基础语义图。
- `RepositoryWorkflowProfile` 已具备 `analysis`、`chapterProfiles`、`lspAssist`、`acp` 等基础字段。
- `WorkflowLspAugmentService` 已能基于 Roslyn 图生成增强建议，但本质仍是 `roslyn-fallback`。
- `WorkflowChapterSliceBuilder` 与 `WorkflowDeepAnalysisService` 已能生成章节切片、分支任务草案和图种子。
- 管理端工作台已支持“增强当前草稿”和“发起 ACP 深挖”，但深挖仍是同步一次性生成结果，不是后台异步执行。

这意味着第一阶段“workflow discovery”已经基本落地，第二阶段“章节切片”已有雏形，但还没有完全实现这次确认的三层方案：

1. `Roslyn/MSBuild` 做主语义底座
2. `LSP` 做增强层
3. `ACP` 做异步编排层

## 目标

在不推翻现有实现的前提下，把当前系统补齐成真正可持续演进的三层架构：

- `Roslyn` 继续负责稳定的代码图、调用关系、状态变更和持久化节点识别。
- `LSP` 改造成 external-first 的增强层，负责补入口、补调用层级、补章节锚点、补继续下钻建议。
- `ACP` 改造成后台异步深挖执行层，负责会话排队、任务拆分、运行日志、artifact 回流和前端进度展示。
- `Profile Schema` 升级成“分析任务配置”，不再只是文档标题草稿。

## 非目标

本次不做以下事项：

- 不把 Roslyn 替换掉，也不让 LSP 成为主语义引擎。
- 不做完整通用的多语言工作流分析平台，仍优先服务当前 .NET/C# 仓库。
- 不实现真正远程多 agent 分布式调度，当前 ACP 先落为“后台 worker 执行 + 结构化任务/产物持久化”。
- 不改造现有 `CatalogItem` 基础 JSON 结构。

## 当前基线与缺口

### 已有能力

- 基础 workflow profile 持久化与管理端编辑
- Roslyn 语义图与候选流程发现
- Workflow 专用 prompt 与 topic context
- 草稿增强入口
- 章节切片与种子图生成
- 深挖会话、任务、artifact 基础表

### 当前缺口

#### 1. Schema 仍偏“草稿描述”

目前 profile 已有部分分析字段，但还缺少这类真正驱动深挖的配置：

- `entryRoots`
- `entryKinds`
- `analysis.allowedNamespaces`
- `analysis.stopNamespacePrefixes`
- `analysis.stopNamePatterns`
- chapter 级 `analysisMode`
- chapter 级 `includeFlowchart`
- chapter 级 `includeMindmap`
- `lspAssist.requestTimeoutMs`
- `lspAssist.enableDefinitionLookup`
- `lspAssist.enableReferenceLookup`
- `lspAssist.enablePrepareCallHierarchy`
- `acp.splitStrategy`

#### 2. LSP 仍未接外部服务

当前 `WorkflowLspAugmentService` 并没有真实调用外部语言服务器。它只是读取 Roslyn 图，再输出增强建议。因此：

- `Strategy` 实际只有 `roslyn-fallback`
- 没有 external LSP 初始化、超时、失败原因、诊断信息
- 没有 definition/reference/call hierarchy 的真实结果

#### 3. ACP 仍是同步模拟

当前 `CreateAnalysisSessionAsync` 直接同步跑完分析，然后把 session、tasks、artifacts 一次性写成 `Completed`：

- 没有 `Queued / Running / Failed / Cancelled`
- 没有分析队列
- 没有 worker
- 没有运行日志
- 没有 detail/logs 轮询接口
- 前端也没有运行态刷新和日志展示

## 架构决策

### 1. Roslyn 是底座，不退位

`Roslyn/MSBuild` 继续承担：

- 符号解析
- 业务节点分类
- 调用边和状态边抽取
- 章节切片基础图
- LSP fallback

原因：

- 当前 discovery、candidate、topic context、wiki generation 已依赖 Roslyn 图谱。
- Roslyn 更适合服务端批量任务，失败面更可控。
- LSP 只解决“补边”和“继续下钻”，不负责主图构建。

### 2. LSP 只做增强层

新增 external LSP client 后，`WorkflowLspAugmentService` 的职责改为：

- external LSP 成功时，优先使用：
  - `definition`
  - `references`
  - `prepareCallHierarchy`
  - `incomingCalls`
  - `outgoingCalls`
- external LSP 失败时，记录失败原因并退回 Roslyn fallback

LSP 的结果不直接写文档，只输出结构化增强结果：

- 推荐入口
- 推荐 root symbols
- 推荐 must-explain symbols
- 推荐章节锚点
- 调用层级边
- 诊断信息

### 3. ACP 先落为“异步编排层”

这里的 ACP 不要求一步做到真实分布式 agent 网络。当前版本以“后台异步 worker + 结构化任务模型”落地：

- 创建分析会话时只落库并入队
- `WorkflowAnalysisWorker` 后台拉取 `Queued` 会话
- `WorkflowAnalysisExecutionService` 执行 discovery、slice、deep analysis
- 执行过程中分步更新 task/session/log/artifact
- 前端轮询 detail 与 logs，看到真实进度

这样已经能满足“主线集中、分支可拆、结果可回放、失败可补跑”。

## 目标数据模型

### Profile Schema 增量

在保留现有字段的基础上，补以下字段。

#### `RepositoryWorkflowProfile`

新增：

- `EntryRoots: List<string>`
- `EntryKinds: List<string>`

#### `WorkflowProfileAnalysisOptions`

新增：

- `AllowedNamespaces: List<string>`
- `StopNamespacePrefixes: List<string>`
- `StopNamePatterns: List<string>`

#### `WorkflowChapterProfile`

新增：

- `AnalysisMode`
- `IncludeFlowchart`
- `IncludeMindmap`

建议枚举：

- `Standard`
- `Deep`

#### `WorkflowLspAssistOptions`

新增：

- `RequestTimeoutMs`
- `EnableDefinitionLookup`
- `EnableReferenceLookup`
- `EnablePrepareCallHierarchy`

#### `WorkflowAcpOptions`

新增：

- `SplitStrategy`

### LSP 增强结果模型

扩展 `WorkflowLspAugmentResult`：

- `FallbackReason`
- `LspServerName`
- `Diagnostics`
- `ResolvedDefinitions`
- `ResolvedReferences`

### ACP 运行态模型

保留现有三张表，并补一张运行日志表：

- `WorkflowAnalysisSession`
- `WorkflowAnalysisTask`
- `WorkflowAnalysisArtifact`
- `WorkflowAnalysisLog`

其中：

#### `WorkflowAnalysisSession`

新增：

- `QueuedAt`
- `CurrentTaskId`
- `ProgressMessage`

规范状态：

- `Queued`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

#### `WorkflowAnalysisTask`

规范状态：

- `Pending`
- `Running`
- `Completed`
- `Failed`
- `Skipped`

#### `WorkflowAnalysisLog`

新增字段：

- `AnalysisSessionId`
- `TaskId`
- `Level`
- `Message`
- `CreatedAt`

## 后端组件设计

### 1. External LSP Client

新增目录：

- `src/OpenDeepWiki/Services/Wiki/Lsp/`

新增组件：

- `WorkflowExternalLspOptions`
- `WorkflowExternalLspProtocolModels`
- `IWorkflowExternalLspClient`
- `WorkflowExternalLspClient`

实现原则：

- 使用 stdio + JSON-RPC 最小实现
- 不把协议逻辑写进 augment service
- 支持超时、取消、进程退出和失败诊断
- 允许全局关闭 external LSP

### 2. WorkflowLspAugmentService 重构

新增两层路径：

- `TryExternalAugmentAsync(...)`
- `BuildRoslynFallbackAugment(...)`

执行策略：

1. profile 或系统配置关闭 external LSP：直接 fallback
2. external LSP 成功：返回 `external-lsp`
3. external LSP 失败：记录 `FallbackReason`，返回 `roslyn-fallback`

### 3. ACP 队列与执行

新增服务：

- `WorkflowAnalysisQueueModels`
- `IWorkflowAnalysisQueueService`
- `WorkflowAnalysisQueueService`
- `IWorkflowAnalysisExecutionService`
- `WorkflowAnalysisExecutionService`
- `WorkflowAnalysisWorker`

职责拆分：

- `WorkflowTemplateAnalysisService` 只负责创建 session/task 并入队
- `WorkflowAnalysisQueueService` 负责领取/入队/幂等保护
- `WorkflowAnalysisExecutionService` 负责真正执行
- `WorkflowAnalysisWorker` 负责循环拉取与状态切换

## API 设计

保留已有接口，新增：

- `GET /api/admin/repositories/{id}/workflow-template/sessions/{sessionId}/analysis-sessions/{analysisSessionId}/logs`

扩展 detail DTO，增加：

- `currentTaskId`
- `progressMessage`
- `runningTaskCount`
- `pendingTaskCount`
- `recentLogs`

## 前端设计

### 工作台行为

#### 1. 分析会话自动轮询

当 analysis session 状态为：

- `Queued`
- `Running`

前端定时轮询：

- detail
- logs

当状态变为：

- `Completed`
- `Failed`
- `Cancelled`

自动停止轮询。

#### 2. 分析面板新增运行态信息

展示：

- 当前进度文案
- 当前运行 task
- pending/running/completed 数量
- 最近日志
- 自动刷新提示

## 与现有链路的关系

### 目录与文档生成链

本次改动不重写主生成链。仍保持：

- `GenerateCatalogAsync`
- `GenerateDocumentsAsync`

但 `WorkflowTopicContextService` 继续消费 `Completed` 的 deep analysis 结果，把章节种子回流到正式 workflow 文档生成。

### 工作台链路

保留现有：

- 建会话
- 多轮生成草稿
- 增强当前草稿
- 创建深挖会话

只把最后一步从同步即时完成改为异步后台执行。

## 实施顺序

### Phase 1

- 补 profile schema 增量字段
- 补前后端 DTO/类型
- 补规则清洗与默认值

### Phase 2

- 实现 external LSP client
- 重构 augment service 为 external-first
- 补 diagnostics / fallback 信息

### Phase 3

- 实现 analysis queue / worker / execution service / log
- 改造 API
- 改造前端轮询与运行态展示

## 验证策略

- 后端单测覆盖：
  - external LSP client
  - augment fallback 策略
  - analysis queue
  - execution service
  - admin analysis endpoints
- 前端 lint 覆盖工作台相关文件
- `dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj`
- `dotnet build OpenDeepWiki.sln`

## 后续优化建议

以下建议用于指导下一阶段优化，不改变本设计当前的主决策：

### 1. planner 继续保持 deterministic，AI 只做 hint

当前 `planner` 负责最终任务图生成，因此应继续保持代码侧 deterministic 行为。后续如果引入 AI，只建议增加 `AI planner hint`，用于补：

- 建议章节
- 建议 `branchTopic`
- 建议 `mustExplainSymbols`
- 建议章节优先级

最终是否进入任务图，仍由规则校验与 coverage validator 决定。

### 2. LSP 的第一目标是补强 profile，而不是只返回建议

从工作台使用体验看，仅把 LSP 结果展示成建议还不够，后续应优先补：

- 自动回填 `chapterProfiles`
- 自动回填 `analysis.rootSymbolNames`
- 自动回填 `analysis.mustExplainSymbols`
- 自动记录 `callHierarchyEdges`

否则用户会看到 `lspAssist.enabled = true`，但 profile 仍可能没有章节和关键方法配置。

### 3. ACP 默认入口应面向整条业务流

当前产品语义应明确区分两种操作：

- 默认 ACP 深挖：针对当前已选业务流执行整条主线与支线分析
- 章节级 ACP 深挖：作为补跑或聚焦分析入口

章节下拉不应成为默认主路径，而应作为“局部重跑工具”存在。

### 4. references 应作为重型补充查询

在 external LSP 默认策略中，建议维持：

- `definition` 优先
- `callHierarchy` 优先
- `references` 默认关闭

原因是 `references` 更适合作为补边或查漏工具，而不是主链定位工具。对于 solution 内跨项目调用，优先验证 `definition/callHierarchy` 是否已经足够，只有确有缺口时再启用 references。

### 5. ACP 结果需要更强的运行态与审计能力

真实模型接入后，后续建议补充：

- artifact 级来源标记
- fallback 原因分类
- 当前模型名与请求类型
- AI 耗时与重试信息
- 缺项回补任务轨迹

这样才能持续判断“LSP 与 ACP 的增强是否真的提升了产物质量和覆盖率”。

## 最终结论

本次不是继续讨论“要不要上三层方案”，而是把现有实现补全成真正的三层闭环：

- `Roslyn` 负责主语义
- `LSP` 负责增强
- `ACP` 负责异步编排
- `LLM` 负责最终组织成文

设计完成后，代码应表现为：

- profile 可表达“分析到哪、哪些方法必须讲透”
- augment 可说明“external-lsp 成功还是 fallback”
- 深挖会话可真正排队、执行、跟踪和回放
- 工作台可看到真实进度，而不是一次性静态结果
