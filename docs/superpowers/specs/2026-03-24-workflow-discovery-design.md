# Workflow 业务流程发现与文档生成设计

## 背景

OpenDeepWiki 当前主流程是“先生成目录，再按目录生成文档”，整体链路保持为：

`RepositoryProcessingWorker -> WikiGenerator.GenerateCatalogAsync -> WikiGenerator.GenerateDocumentsAsync`

现有设计已经能较好地产出模块说明类文档，但对 WMS、MES、ERP 这类强业务流系统存在明显短板：

- 目录阶段缺少“业务流程发现”能力，更多依赖目录树、README、入口文件和模型自行理解。
- 正文阶段虽然可以 `ListFiles / Grep / ReadFile`，但前提是目录中已经存在“容器托盘入库流程”这类流程页节点。
- `DocCatalog` 当前只表达树结构，不表达主题类型、流程证据、种子查询等生成前语义。

这会导致系统更容易输出：

- `Wcs 接口`
- `请求表`
- `定时任务`
- `Executor`

而不是一篇能把这些模块串起来的：

- `容器托盘入库流程`

## 目标

为 OpenDeepWiki 增加“业务流程发现与流程文档生成”能力，使系统可以从 .NET/C# 仓库中自动识别跨模块业务链路，并生成高质量流程型 Wiki 页面。

第一阶段直接覆盖高精度能力，包含：

- Roslyn 语义分析
- 调用图增强
- 流程候选聚类与打分
- 流程上下文持久化
- Catalog 自动增强
- Workflow 专用文档生成 Prompt

## 非目标

以下内容不纳入本阶段交付：

- 非 .NET 语言的完整语义发现实现
- 基于完整系统级控制流图的精确执行模拟
- 将所有现有模块页自动重写为流程页
- 前端复杂筛选器或流程可视化编辑器
- 用 LSP 取代 Roslyn 作为 .NET 主分析引擎

## 关键决策

### 1. 使用 Roslyn 作为 .NET 主语义引擎

本方案将 Roslyn 作为第一阶段唯一的 .NET 语义引擎。

原因：

- OpenDeepWiki 当前目标样本以 .NET/C# 为主，Roslyn 对符号、继承、接口实现、调用关系、类型引用、属性赋值等能力最直接。
- 我们需要的是“流程发现”，而不只是“跳转定义”。LSP 能提供符号与引用，但流程拼装、领域打分和上下文建模仍然需要自定义逻辑。
- C# 领域里，LSP 背后很多核心能力仍来自 Roslyn。直接接 Roslyn 更稳定、更可控，也更适合批处理型服务端任务。

### 2. 为未来多语言扩展预留 LSP Provider 接口

本方案不会在第一阶段真正接入 LSP Server，但会抽象出统一语义提供器接口，为未来 Java、Go、TypeScript 等多语言扩展保留接入点。

换言之：

- 第一阶段实现：`RoslynWorkflowSemanticProvider`
- 预留扩展位：`LspWorkflowSemanticProvider`

### 3. 不直接扩展 Catalog JSON 结构

现有 `CatalogItem` JSON 结构仅包含：

- `title`
- `path`
- `order`
- `children`

为避免冲击 catalog 存储、翻译流程和树接口，本方案不在第一阶段直接往 `CatalogItem` 中塞复杂 metadata，而是新增独立的 `DocTopicContext` 持久化表，保存流程页的生成前语义上下文。

### 4. 维持现有主处理链路

保持现有大流程不变，仅在目录生成后、正文生成前插入“流程发现与目录增强”步骤：

`GenerateCatalogAsync -> DiscoverWorkflowsAsync -> MergeWorkflowCatalogAsync -> GenerateDocumentsAsync`

## 总体架构

```text
RepositoryProcessingWorker
  -> WikiGenerator.GenerateCatalogAsync
      -> 原有 Catalog 生成
      -> WorkflowDiscoveryService.DiscoverAsync
          -> RoslynWorkflowSemanticProvider
          -> WorkflowGraphBuilder
          -> WorkflowCandidateExtractor
      -> WorkflowCatalogAugmenter.MergeAsync
      -> WorkflowTopicContextService.UpsertAsync
  -> WikiGenerator.GenerateDocumentsAsync
      -> 普通节点: content-generator.md
      -> Workflow 节点: workflow-content-generator.md
```

## 核心组件设计

### 1. `IWorkflowSemanticProvider`

职责：从仓库工作区抽取统一语义图谱，不关心具体语言实现。

建议接口：

```csharp
public interface IWorkflowSemanticProvider
{
    Task<WorkflowSemanticGraph> BuildGraphAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default);

    bool CanHandle(RepositoryWorkspace workspace);
}
```

第一阶段只实现：

- `RoslynWorkflowSemanticProvider`

后续可扩展：

- `LspWorkflowSemanticProvider`

### 2. `RoslynWorkflowSemanticProvider`

职责：使用 `MSBuildWorkspace` 打开 `.sln` 或 `.csproj`，抽取语义节点与边。

需要识别的主要节点类型：

- `Controller`
- `Endpoint`
- `BackgroundService`
- `HostedService`
- `DbContext`
- `DbSet`
- `Entity`
- `RequestEntity`
- `Executor`
- `ExecutorFactory`
- `Handler`
- `Service`
- `Repository`
- `ExternalClient`
- `StatusEnum`

需要识别的主要边类型：

- `Invokes`
- `Implements`
- `RegisteredBy`
- `Reads`
- `Writes`
- `Queries`
- `Dispatches`
- `UpdatesStatus`
- `ConsumesEntity`
- `ProducesEntity`

### 3. `WorkflowGraphBuilder`

职责：将 Roslyn 提取到的符号、调用、赋值、注册信息组织成可查询的统一图结构。

建议数据结构：

```csharp
public sealed class WorkflowSemanticGraph
{
    public List<WorkflowGraphNode> Nodes { get; init; } = [];
    public List<WorkflowGraphEdge> Edges { get; init; } = [];
}

public sealed class WorkflowGraphNode
{
    public string Id { get; init; } = string.Empty;
    public WorkflowNodeKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public string? MetadataJson { get; init; }
}

public sealed class WorkflowGraphEdge
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public WorkflowEdgeKind Kind { get; init; }
    public string? EvidenceJson { get; init; }
}
```

### 4. `WorkflowCandidateExtractor`

职责：从语义图谱中抽取“业务流程候选项”，并做聚类、命名、打分与过滤。

第一阶段仅识别具备以下最小形态的链路：

- 至少一个触发节点
- 至少一个持久化节点
- 至少一个调度或轮询节点
- 至少一个执行节点

推荐候选项结构：

```csharp
public sealed class WorkflowTopicCandidate
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public double Score { get; init; }
    public List<string> Actors { get; init; } = [];
    public List<string> TriggerPoints { get; init; } = [];
    public List<string> RequestEntities { get; init; } = [];
    public List<string> SchedulerFiles { get; init; } = [];
    public List<string> ExecutorFiles { get; init; } = [];
    public List<string> ServiceFiles { get; init; } = [];
    public List<string> EvidenceFiles { get; init; } = [];
    public List<string> SeedQueries { get; init; } = [];
    public List<string> ExternalSystems { get; init; } = [];
    public List<string> StateFields { get; init; } = [];
}
```

### 5. `WorkflowCatalogAugmenter`

职责：将流程候选项合并到现有 `CatalogRoot` 中。

策略：

- 若顶层不存在“核心业务流程”，则创建。
- 每个流程候选项作为该目录下的叶子节点。
- Path 采用稳定 slug，例如：
  - `business-workflows/container-pallet-inbound`
  - `business-workflows/outbound-task-dispatch`

合并后目录示例：

```json
{
  "items": [
    { "title": "概览", "path": "overview", "order": 0, "children": [] },
    {
      "title": "核心业务流程",
      "path": "business-workflows",
      "order": 90,
      "children": [
        {
          "title": "容器托盘入库流程",
          "path": "business-workflows/container-pallet-inbound",
          "order": 0,
          "children": []
        }
      ]
    }
  ]
}
```

### 6. `WorkflowTopicContextService`

职责：为流程页保存生成前上下文，供正文生成阶段使用。

建议新增实体：`DocTopicContext`

```csharp
public class DocTopicContext : AggregateRoot<string>
{
    public string BranchLanguageId { get; set; } = string.Empty;
    public string CatalogPath { get; set; } = string.Empty;
    public string TopicKind { get; set; } = string.Empty; // Module / Workflow
    public string ContextJson { get; set; } = string.Empty;
}
```

`ContextJson` 至少包含：

- `name`
- `summary`
- `actors`
- `triggerPoints`
- `requestEntities`
- `schedulerFiles`
- `executorFiles`
- `serviceFiles`
- `evidenceFiles`
- `seedQueries`
- `externalSystems`
- `stateFields`

## Roslyn 语义分析范围

### 1. 解决方案加载

优先级：

1. `.sln`
2. 主 `.csproj`
3. 所有可编译项目聚合

要求：

- 支持大型仓库的容错加载
- 记录项目加载失败但不中断整体流程
- 对无法加载语义模型的项目回退到语法级分析

### 2. 符号抽取

需重点识别：

- 继承 `ControllerBase` 的类
- 带 `[ApiController]`、`[Route]` 的类
- 继承 `BackgroundService` 的类
- 实现 `IHostedService` 的类
- 名称匹配 `*Executor`, `*Handler`, `*Factory`, `*Service`, `*Repository` 的类
- `DbContext` 子类及其 `DbSet<T>`
- 状态枚举和状态属性

### 3. 调用图增强

调用图不追求完整程序级 call graph，而是服务于流程发现。

增强点：

- 记录控制器动作调用了哪些 service
- 记录定时任务/后台服务调用了哪些仓储和执行器
- 记录 factory / switch / 字典映射将请求类型分发到哪个 executor
- 记录 executor 进一步调用哪些 domain service
- 记录对状态字段的赋值与保存点

### 4. DI 注册增强

扫描：

- `Program.cs`
- `Startup.cs`
- 常见扩展方法注册类

识别：

- `AddScoped`
- `AddSingleton`
- `AddTransient`
- `AddHostedService`

这能帮助补齐“接口 -> 实现类”关系，以及“后台任务属于系统运行链路”的信号。

## 流程发现规则

### 1. 流程入口识别

下列任一信号都可作为入口候选：

- HTTP Controller Action
- Minimal API Endpoint
- 消息消费者入口
- 外部系统接入类，命名中包含 `Wcs`、`Mes`、`Erp`、`Callback`、`Notify`

### 2. 请求持久化识别

下列任一信号都可作为持久化节点：

- `DbSet<...Request...>`
- `Insert/Add` 到请求实体
- `SaveChangesAsync`
- Repository/DAO 对请求表的写入

### 3. 调度识别

下列任一信号都可作为调度节点：

- `BackgroundService.ExecuteAsync`
- `IHostedService`
- Timer / Scheduler / Cron / Job
- 轮询查询请求表的代码

### 4. 分发识别

下列任一信号都可作为分发节点：

- `ExecutorFactory`
- `IExecutor`
- `switch(request.Type)`
- `Dictionary<string, Func<...>>`
- 依据请求类型解析实现类

### 5. 执行识别

下列任一信号都可作为执行节点：

- `*Executor`
- `*Handler`
- `*Service`
- 实际业务方法链路的下游调用

### 6. 状态流转识别

下列信号用于补充流程解释和异常处理：

- `Status = Pending/Processing/Completed/Failed`
- `UpdateTimestamp`
- `RetryCount`
- `ErrorMessage`
- 失败分支与补偿逻辑

## 候选流程聚类与打分

### 聚类主线

聚类优先按“共享请求实体 / 共享类型枚举 / 共享入口关键字 / 共享 executor”聚合。

例如：

- `ContainerPalletInboundRequest`
- `ContainerPalletInboundExecutor`
- `ScanInboundRequestsAsync`
- `WcsInboundController`

若它们通过调用关系和实体访问形成连通子图，则归为同一流程候选。

### 打分维度

建议基础分数来源：

- 存在外部入口：+15
- 存在请求表写入：+20
- 存在后台轮询：+20
- 存在 executor 分发：+20
- 存在状态流转：+10
- 证据文件数超过 4：+10
- 关键词高度一致：+10

惩罚项：

- 仅有模块名相似但无调用或实体关联：-20
- 只有 service 相互调用，缺少触发/落库/调度链路：-15
- 证据过少且名称泛化：-15

只有超过阈值的候选项才会进入 Catalog。

## 正文生成设计

### 1. 普通页与流程页分流

`GenerateDocumentsAsync` 在读取 catalog 叶子节点后，增加一次上下文查询：

- 若 `DocTopicContext.TopicKind == Workflow`，走 `GenerateWorkflowDocumentContentAsync`
- 否则继续走现有 `GenerateDocumentContentAsync`

### 2. 新增 Prompt：`workflow-content-generator.md`

目标是强制生成结构化流程页，而非普通模块页。

流程页必须包含：

- 标题与概述
- 参与角色
- 触发入口
- 请求落库
- 调度与扫描
- 分发与 executor 选择
- 核心执行步骤
- 状态迁移
- 异常与补偿
- 关键源码定位
- Mermaid 时序图或流程图

### 3. Workflow Prompt 输入

除了现有仓库信息外，还要额外注入 `DocTopicContext`。

重点字段：

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
- `state_fields`

正文生成器应优先：

- 读取 `evidence_files`
- 执行 `seed_queries`
- 再根据结果补充 `ListFiles` / `Grep` / `ReadFile`

### 4. 文档生成目标

以 WMS 例子为例，流程文档应该自然回答：

1. Wcs 从哪里进入系统
2. 请求写入了哪张表
3. 哪个后台任务负责扫表
4. 如何根据请求类型分发到 executor
5. 入库 executor 具体调用了哪些服务
6. 状态如何变化，失败如何处理

## 与现有系统的集成点

### 修改 `WikiGenerator`

需要增加的方法或扩展点：

- `DiscoverAndMergeWorkflowCatalogAsync`
- `GenerateWorkflowDocumentContentAsync`
- `TryLoadTopicContextAsync`

推荐接入点：

1. 在 `GenerateCatalogAsync` 末尾：
   - 读取原始 catalog
   - 调用 `WorkflowDiscoveryService`
   - 合并流程节点
   - 持久化增强后的 catalog
   - 保存 `DocTopicContext`

2. 在 `GenerateDocumentsAsync` 中：
   - 仍以 catalog 叶子节点为驱动
   - 但对 workflow 叶子节点走专门分支

### 修改 `Program.cs`

注册：

- `IWorkflowSemanticProvider`
- `IWorkflowDiscoveryService`
- `IWorkflowCatalogAugmenter`
- `IWorkflowTopicContextService`

### 数据库迁移

新增：

- `DocTopicContexts` 表

索引建议：

- `(BranchLanguageId, CatalogPath)` 唯一索引

## 文件改动范围

### 新增文件

- `src/OpenDeepWiki.Entities/Repositories/DocTopicContext.cs`
- `src/OpenDeepWiki/Services/Wiki/IWorkflowSemanticProvider.cs`
- `src/OpenDeepWiki/Services/Wiki/RoslynWorkflowSemanticProvider.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowSemanticGraph.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowDiscoveryService.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowCandidateExtractor.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowCatalogAugmenter.cs`
- `src/OpenDeepWiki/Services/Wiki/WorkflowTopicContextService.cs`
- `src/OpenDeepWiki/prompts/workflow-content-generator.md`

### 修改文件

- `src/OpenDeepWiki/Services/Wiki/WikiGenerator.cs`
- `src/OpenDeepWiki/Program.cs`
- `src/OpenDeepWiki.EFCore/*` 中的上下文与迁移配置

### 新增测试

- `tests/OpenDeepWiki.Tests/Services/Wiki/RoslynWorkflowSemanticProviderTests.cs`
- `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCandidateExtractorTests.cs`
- `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowCatalogAugmenterTests.cs`
- `tests/OpenDeepWiki.Tests/Services/Wiki/WorkflowDocumentRoutingTests.cs`

## 测试策略

### 1. 语义提取测试

验证：

- 可正确识别 Controller / BackgroundService / Executor / DbContext / Entity
- 可提取接口实现和方法调用关系
- 可提取状态赋值点

### 2. 候选流程提取测试

构造小型样本仓库，验证：

- `入口 -> 请求表 -> 后台任务 -> executor -> service` 能识别为单一流程
- 只有 service 链路但没有调度和持久化的情况不会误判为流程

### 3. Catalog 增强测试

验证：

- 顶层新增“核心业务流程”
- 叶子节点 path 稳定
- 不会破坏原有 catalog 树结构

### 4. 文档路由测试

验证：

- workflow 节点会使用 workflow prompt
- 普通节点仍使用原有 content prompt

### 5. 真实样本验收

建议使用 `WmsServerV4Dev` 做人工验收，目标最少覆盖一条流程：

- `容器托盘入库流程`

验收标准：

- 目录中出现该流程页
- 文档能明确写出“Wcs -> 请求表 -> 定时任务 -> executor -> 入库执行”
- 文中给出具体源码位置
- 文中带流程图或时序图

## 风险与缓解

### 1. Roslyn 加载大型解决方案过慢

缓解：

- 先按主项目与可引用项目裁剪
- 对失败项目降级为语法分析
- 记录并缓存图谱结果

### 2. 流程候选过多或噪声过高

缓解：

- 使用最小链路约束
- 引入阈值打分
- 限制默认最多注入若干条高分流程

### 3. Prompt 仍然写偏成模块说明

缓解：

- workflow prompt 强制结构模板
- 先喂 `DocTopicContext`
- 强制先读 `evidence_files`

### 4. 与现有 catalog/translation 逻辑耦合

缓解：

- 不修改 `CatalogItem` 基础结构
- 用独立 `DocTopicContext` 保存流程上下文

## 里程碑

### M1：流程发现底座

- `DocTopicContext`
- Roslyn 语义提取
- 图谱结构

### M2：流程候选与目录增强

- 聚类与打分
- Catalog 合并
- TopicContext 持久化

### M3：流程文档生成

- workflow prompt
- 文档生成分流
- 真实仓库验收

## 最终结论

本设计采用“Roslyn 主引擎，LSP 预留扩展位”的路线：

- 对当前 .NET/WMS 目标仓库，Roslyn 能以最小系统复杂度提供最高精度。
- 对未来多语言扩展，统一的 `IWorkflowSemanticProvider` 接口为 LSP 接入保留了自然扩展位。
- 通过“流程发现 -> 流程上下文持久化 -> Catalog 增强 -> Workflow 专用文档生成”四段式链路，OpenDeepWiki 将从“模块文档系统”升级为“模块文档 + 业务流程文档”的双维度系统。
