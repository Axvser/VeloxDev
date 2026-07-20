# 编译机制

## 概述

`WorkflowCompiler` 是一个基于图拓扑的编译执行引擎。它将工作流节点图编译为有序可执行计划，按 BFS/DFS 顺序链式驱动节点执行，并内置路由分支、错误重定向、生命周期钩子等机制。

---

## 快速开始

```csharp
// 1. 创建编译器
var compiler = new WorkflowCompiler();

// 2. 编译（FromNode 默认: 从 Controller 出发沿 Slot 连接正向遍历）
var results = compiler.Compile(controllerNode, CompileMode.BFS);
var plan = results[0]; // FromNode 始终返回单个结果

// 3. 执行
var context = NetworkFlowContext.Create("seed-payload");
await plan.ExecuteAsync(context);
```

---

## 四维编译配置

编译器提供四个正交配置维度，组合出 **24 种编译策略**：

```
CompileMode     Direction       Scope           CycleHandling
─────────────────────────────────────────────────────────────
BFS             Forward         FromNode        Throw
DFS             Reverse         Omni            Trim
                                                Allow
```

### CompileMode — 遍历算法

| 值      | 行为                                                                                      | 适用场景                             |
| ------- | ----------------------------------------------------------------------------------------- | ------------------------------------ |
| `BFS` | 广度优先，层级展开。同层邻居按`ICompileTimePriority` 升序排序。路由节点子节点插队到队首 | 需要按拓扑层级执行的场景（默认）     |
| `DFS` | 深度优先前序，父节点→子节点递归。同层邻居按优先级排序                                    | 需要优先完成单条链路后再处理其他分支 |

### CompileDirection — 边的方向

| 值          | 行为                           | 适用场景             |
| ----------- | ------------------------------ | -------------------- |
| `Forward` | 沿`Slots[].Targets` 正向遍历 | 从入口到出口（默认） |
| `Reverse` | 沿`Slots[].Sources` 反向遍历 | 从出口逆向追溯到入口 |

### CompileScope — 起点选择

| 值           | 行为                                    | 适用场景                             |
| ------------ | --------------------------------------- | ------------------------------------ |
| `FromNode` | 从指定节点出发向外辐射                  | 从 Controller 开始执行（默认，直觉） |
| `Omni`     | 自动发现图中所有边界点                  | 全图扫描、自动发现所有孤立子图       |
|              | Forward+Omni: 找入度为 0 的节点作为起点 |                                      |
|              | Reverse+Omni: 找出度为 0 的节点作为起点 |                                      |

**注意**：`Omni` 模式返回 `IReadOnlyList<CompilationResult>`（每个入口一个独立结果），`FromNode` 返回单元素列表。

### CycleHandling — 环路策略

| 值        | 行为                                                                     | 适用场景               |
| --------- | ------------------------------------------------------------------------ | ---------------------- |
| `Throw` | 检测到环路即抛异常                                                       | 严格无环图（默认）     |
| `Trim`  | BFS/DFS 自然跳过已访问节点，不保留环路元数据                             | 容忍环路但无需熔断     |
| `Allow` | 保留环路元数据（`IsLoopEntry`/`LoopTailId`），执行时每节点只执行一次 | 需要环路信息的外部工具 |

---

## 深度与执行顺序

### `CompiledItem.Depth`（深度豁免）

路由节点（`ICompileTimeRouter`，如 BoolSelector、EnumSelector）**不递增深度**：

```
Controller    LoadSeed    BoolSelector(router)    joinHot
Depth: 0      1           1                       1 ← 未递增!
```

子节点的 Depth 等于路由节点自身的 Depth，不受路由「分叉」影响。

### `CompiledItem.Order`（执行序）

Order 是编译结果中的 0-based 顺序位置。**路由节点的子节点插队到 BFS 队首**：

```
BFS 队列: [Aggregate, joinHot(插队), joinCold(插队)]
→ 出队顺序: joinHot → joinCold → Aggregate
```

---

## 计算结果

### `CompilationResult`

```csharp
public sealed class CompilationResult
{
    public IReadOnlyList<CompiledItem> Items { get; }  // 有序执行计划
    public CompileMode Mode { get; }
    public CompileDirection Direction { get; }
    public CompileScope Scope { get; }
    public bool HasCycle { get; }
    public CycleHandling CycleHandling { get; }

    // 按序执行，结果链自动传递
    public Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default);
}
```

### `CompiledItem`

```csharp
public sealed class CompiledItem
{
    public int Id { get; }            // 唯一标识
    public int Order { get; }         // 执行顺序
    public int Depth { get; }         // BFS/DFS 深度（路由豁免）
    public IWorkflowNodeViewModel Node { get; }  // 实际节点
  
    // 错误重定向
    public int? ErrorRedirectId { get; set; }
    public int MaxRetries { get; set; }
  
    // 路由元数据
    public bool IsLoopEntry { get; }          // CycleHandling.Allow 时标记环入口
    public int? LoopTailId { get; }           // 环尾部节点 ID
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel>? RouteTable { get; }
    public Dictionary<object, HashSet<int>>? BranchExclusiveItems { get; }
}
```

---

## 执行机制

### 结果链传递

节点执行后，`item.Node.WorkResult` 自动传递给下一节点作为 `parameter`：

```csharp
Controller → LoadSeed → BoolSelector → joinHot → Aggregate
  WorkResult  WorkResult  WorkResult   WorkResult  WorkResult
  "seed"      "loaded"    "routed"     "hot#1"     "aggregated"
```

### 路由分支跳过

`ICompileTimeRouter` 节点在运行时调用 `GetCurrentRouteKey()`，编译器预先计算了每个分支的「独占下游节点」。未选中分支的独占节点在 `ExecuteAsync` 中被跳过（不执行 `WorkCommand`，但仍发送生命周期通知）。

### 错误重定向

```csharp
plan.Items[0].ErrorRedirectId = 3; // 节点 0 失败时跳转到节点 3

// 错误处理器收到 ErrorContext
// 重定向目标异步执行，结果链入 currentParam
// 重定向目标被加入 skippedItems，主循环中不会重复执行
```

### 生命周期钩子

实现 `ICompileTimeSink` 的节点收到：

| 事件              | 时机                   |
| ----------------- | ---------------------- |
| `BeforeExecute` | 节点执行前             |
| `AfterExecute`  | 节点执行后（含被跳过） |
| `OnError`       | 节点失败时             |
| `OnCompleted`   | 整条链执行完毕         |

### 取消

`ExecuteAsync` 接受 `CancellationToken`。取消后：

- `OperationCanceledException` **不会**被捕获，立即中止整条链
- 已取消节点后的所有节点**不会被执行**
- 当前节点的 `UnsubscribeError` 在 rethrow 前执行

---

## 扩展接口

### `ICompileTimeRouter` — 编译时路由

```csharp
public interface ICompileTimeRouter
{
    // 编译时：返回枚举值 → 下游节点的映射表
    IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable();
  
    // 执行时：返回当前选中的路由 key
    object? GetCurrentRouteKey();
}
```

### `ICompileTimePriority` — 编译优先级

```csharp
public interface ICompileTimePriority
{
    int CompilePriority { get; set; }  // 越小越先，默认 0
}
```

### `ICompileTimeSink` — 执行生命周期

```csharp
public interface ICompileTimeSink
{
    void OnExecutionEvent(ExecutionContext context);
}
```