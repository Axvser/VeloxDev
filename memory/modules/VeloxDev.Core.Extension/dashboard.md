# VeloxDev.Core.Extension — Dashboard（宿主面板投影）

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Dashboard/`（5 个文件）。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/Agent/Dashboard/AgentDashboardViewModelTests.cs`。

---

## 一、第一件该知道的事：这一层在仓库里没有 UI 消费者

`AgentDashboardViewModel` 的全部引用都在**它自己的测试**里（`AgentDashboardViewModelTests.cs:66` 起 14 处 `Create(scope)`）。三个 demo 都不用它 —— Avalonia / Blazor / Jalium 是**各自**绑 `McpScope.Status`（`McpStatusViewModel`）建自己的 MCP 面板：

- `Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml.cs:53` `McpStatusPanel.DataContext = helper.Mcp.Status;`
- `Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:24` `=> (_session?.Tree.GetHelper() as AgentHelper)?.Mcp.Status;`
- `Examples/Workflow/Jalium/Demo/MainWindow.cs:600` `_mcpStatus = helper.Mcp.Status;`

**推论**：`Agent/Dashboard/` 是**有测试、无宿主**的一片。它想做的是「把系统工具 / 技能工具 / MCP 服务器 / MCP 工具 / 技能统一成同一种可开关的行」，但各家 demo 现在各自拼面板、也没有统一开关 UI。动它之前先确认你要解决的是哪个问题 —— 改这里不会影响任何一个 demo 的显示。

---

## 二、行是**投影**，不是第二个真相源

`AgentMemberViewModel.cs:8-17` 把这一层的核心约束写死了：

- `IsEnabled` 的写入**穿透到拥有它的 scope**（`ApplyToScope`，`:68`）；scope 起源的变化**回灌**进来（`SetFromScope`，`:73`），回灌时**不回响**。
- **为什么不能直写 scope 自己已经发布的绑定标志**（技能的 `SkillStatusViewModel.IsEnabled`、MCP 的 `McpStatusViewModel.IsEnabled`）：那样会**绕过 scope 的 `Version` 自增**，于是按 `Version` 缓存的 prompt provider 会继续送出宿主以为已经关掉的文本。这就是 `SkillMemberViewModel` 与同类行不直写那个属性的原因。
- `_applying` 标志（`:33`）只需要一个：写入是同步的，且 scope 会吞掉同值写，所以不存在第二次跳跃的窗口。

派生属性：`IsOff`、**`IsReachable = IsEnabled && Note.Length == 0`**（「开着但被宿主策略挡住」是存在的状态，`Note` 就是 `GateNote`）、`HasBeenCalled`、`ActivityText`（未调用 / N 次）。

---

## 三、两层「重新装载」的选择是相反的，理由也是相反的

| 调用点 | 用的方法 | 为什么 |
|---|---|---|
| 服务器行自己的 `ReloadAsync`（`McpMemberViewModel.cs:176`） | **`AddAsync`** | 注释：`LoadAsync` 会重置**整个状态列表**，把别的服务器的行从面板底下抽走 |
| 面板的 `ReloadAllServersAsync`（`AgentDashboardViewModel.cs:94`） | **`LoadAsync`** | 注释：聚合动作的**目的**就是重建整组，而且它自己会重新 track 每一行的状态 |

同一个决策，两个层级给出相反答案 —— 照抄任一处到另一处都会破坏行为。

**服务器行的两个操作刻意都留着，因为代价不同**（`McpMemberViewModel.cs:46-52`）：

- 复选框 → `SetServerEnabled`，**只把工具从模型的工具集里摘掉，连接保持**，即时、可逆。
- `UnloadAsync` / `ReloadAsync` → **拆掉进程/传输**，重连要再付一次代价。

`CanUnload` / `CanReload`（`:105`、`:108`）是这两个操作的前置条件。

---

## 四、几个「为什么要这样写」的细节

| 细节 | 位置 | 理由（代码注释里的） |
|---|---|---|
| 工具行读 `GetServerTools`（**未过滤**的）而不是 `LoadedTools` | `McpMemberViewModel.cs:140-147` | 被关掉的服务器**仍要显示**宿主能重新打开哪些工具；同时保留已有行，免得一次状态刷新把宿主刚翻的开关丢掉 |
| 工具行有自己的开关，服务器行只改 `Note` | `:34-38` | 关工具比关服务器窄：服务器继续提供**其余**工具；服务器被关时工具行只是被标注「所在服务器被宿主关闭」 |
| `HasError` 与 `IsFailedState` 故意分开 | `:79-84` | 错误消息可以挂在**仍然连着**的服务器上；存活/错误的汇总必须数**状态**，否则一个服务器会落进两个桶 |
| 折叠箭头是**存储属性**而不是计算属性；行用普通按钮开合而不用 `Expander` 头 | `:86-92` | 只读属性依赖兄弟标志抬起的 `PropertyChanged`；而开关必须坐在服务器行自己身上，`Expander` 头里的交互子元素会和头的自身 toggle 抢事件 |
| `Rebuild()` 用 `CreateAllTools()` 而不是 `CreateTools()` | `AgentDashboardViewModel.cs:177-182` | 面板必须列出**被关掉的**工具，否则永远关不回去。这与 `CreateAllTools` 本身存在的理由是同一条 |
| `OnToolCalled` 整个包在 `try/catch` 里 | `:109-115` | 它跑在 `TrackedAIFunction` 的 `try` 内部，那里的 `catch` 会把异常变成**工具的错误结果** —— 面板抛一次异常，模型就会被告知工具失败。注释：「面板不得破坏工具调用」 |
| `Post` 在当前已是 UI 上下文时**同步执行** | `:137-142` | 避免一次多余的排队 |
| `QueueRebuild` 合并 | `:148` | 一串 `Changed` 事件只花一次重建 |
| `McpStatusText` 数**互斥的状态** | `:80-85` | 注释：数「有错误消息」会让一个服务器同时进两个桶 |

**`AllMembers()`（`:125`）的顺序是契约**：SystemTools → SkillTools → 每个 MCP 服务器（行 + 它的工具）→ Skills。`RecordCall`（`:117`）按**名字**（`OrdinalIgnoreCase`）在**这个序列里取第一个匹配**。所以两个不同来源的同名工具，计数永远落在先出现的那个上。

---

## 五、扩展点

**加一种可开关的成员**

1. 继承 `AgentMemberViewModel`（`AgentMemberViewModel.cs:18`），实现 `ApplyToScope(bool)`。
2. 若要接 scope 起源的变化，加一个 `Sync(...)` 并调 `SetFromScope(...)`（照 `McpMemberViewModel.cs:128`）。
3. **不要**直写 scope 已经暴露的绑定标志 —— 见第二节。
4. 在 `AgentDashboardViewModel` 里加一个 `ObservableCollection<…>`、在 `Rebuild()`（`:166`）里加一个 `RebuildXxx()`、在 `AllMembers()`（`:125`）里把新集合**按你要的匹配优先级**插进序列、在 `Dispose` 里退订。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在行里直写 `SkillStatusViewModel.IsEnabled` / `McpStatusViewModel.IsEnabled` | scope 的 `Version` 不自增 ⇒ 缓存的 prompt 继续送已关掉的内容（`AgentMemberViewModel.cs:11-15`） |
| 在 `SetFromScope` 里回写 scope | 死循环；`_applying` 只挡得住一跳 |
| 让 `OnToolCalled` 里可能抛异常的代码裸奔 | 它跑在 `TrackedAIFunction` 的 try 内，异常会变成模型的「工具失败」（`:110-114`） |
| 用 `CreateTools()` 建面板的工具行 | 被关掉的工具不会出现，于是关不回去（`:179-181`） |
| 在服务器行用 `LoadAsync` 重连 | 会重置整个状态列表，抽掉别的服务器的行（`McpMemberViewModel.cs:181-183`） |
| 忘了在 `Dispose` 里退订 | 面板活过了 scope，`Changed`/`ToolCalled` 继续打进来 |

---

## 六、死面

- 本目录**全部 5 个文件都有调用者**（在模块内部）+ 测试覆盖，**不存在零调用者成员**。
- 但**整个目录在仓库里没有 UI 消费者**（见第一节）。这是本模块最容易被误判的一处：看见 demo 里有 MCP 状态面板就以为它在用 Dashboard，其实 demo 绑的是 `McpScope.Status` 那个更薄的对象。
