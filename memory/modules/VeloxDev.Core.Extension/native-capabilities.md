# VeloxDev.Core.Extension — 框架原生能力

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:1642`（「框架自带的能力 provider」区）。
> 宿主接线：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`（`WithTodoTracking` / `WithAgentModes`）。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/Agent/Workflow/AgentCapabilityProvidersTests.cs`。

**这三条不是本仓库实现的**。它们全是 `Microsoft.Agents.AI` 自带的 `AIContextProvider`，本科目只做「挂上去」这一件事。所以本文件记的不是「怎么实现」，而是**「挂的时候哪里会咬人」**。

---

## 一、三条是什么，各贡献什么

| 挂法 | 框架类型 | 模型因此多出来的工具 | 贡献的文本 |
|---|---|---|---|
| `WithTodoTracking()` | `TodoProvider` | `todos_add` / `todos_complete` / `todos_remove` / `todos_get_remaining` / `todos_get_all` | 1569 字符的 `## Todo Items` 块，**外加一条 message** |
| `WithAgentModes(options)` | `AgentModeProvider` | `mode_set` / `mode_get` | 636 字符的 `## Agent Mode` 块，逐条列出各模式的 instructions |
| `WithContextCompaction(cw, out)` | `Compaction.CompactionProvider` | 无 | 无（它只裁剪消息历史） |

上面两列的长度与工具名是在 1.22.0 上**实测**的（跑一次真 agent，记录 `ChatOptions.Tools` 与系统消息），不是读文档推的。

**模式里唯一值得当场用的是 `plan`**：能先勘察再说要改什么、而不改。宿主接的是 `AgentHelper.AgentModes`。

**压缩需要宿主给两个整数** —— 模型的上下文窗口与单次输出上限。这两个是**部署的事实，不是本 scope 的事实**：策略用它们的差当输入预算（`maxContextWindowTokens - maxOutputTokens`），再按 0.5 / 0.8 两个比例分两阶段动手（先摘要旧工具结果，再丢最旧的轮次）。所以 demo **故意没挂它**（`AgentHelper.cs` 的接线注释写明了理由）—— 猜一个窗口大小会导致在错误的时刻压缩。知道自家模型的宿主自己加这一行。

---

## 二、最承重的一条：`Compaction` 整个命名空间是 `[Experimental]`

`Microsoft.Agents.AI.Compaction` 下的**每一个**类型都带 `[Experimental("MAAI001")]`：`CompactionProvider`、`CompactionStrategy`、全部六个策略、`CompactionTrigger(s)`。1.22.0 里共有 **51 个** `[Experimental]` 类型（另含 `LoopAgent`、`LoopEvaluator`、`FileAccessProvider`、`BackgroundAgentsProvider`、`AgentFileStore`）。

**本模块的处理方式是把引用圈进一个 `#pragma`**（`WorkflowAgentScope.cs:1715` 起的那段），于是：

- 诊断只落在**本库编译时**，宿主不会因为引用 `VeloxDev.Core.Extension` 而吃到 `MAAI001`。
- `WithContextCompaction(int, int)` 收两个整数、**不对外暴露 `CompactionStrategy`**。宿主因此永远不必写出那个实验类型名 —— 这是刻意的：若签名收 `CompactionStrategy`，每个调用方都得自己 `#pragma`。

**要核这条，去看 `WorkflowAgentScope.cs:1715` 的 ctor 与它的 `#pragma`，别看 commit。**

**反面**：`TodoProvider`、`AgentModeProvider`、`TodoItem`、`ChatClientAgent` 全部是 **stable**。所以「MAF 的实验面」不能一概而论 —— 挂之前先核具体类型。

---

## 三、刻意不做的三件事（都是「看着能编译，但是错的」的反面）

| 没做的事 | 为什么 |
|---|---|
| **不用 `TrackedAIFunction` 包它们的工具** | 这 7 个工具既不读也不写工作流树，所以没有东西需要编组到宿主线程、没有东西该计入调用预算、也没有东西该标脏。与 `ResetToolCallLimit` 同一种处理，理由同一条（`WorkflowAgentToolkit.cs:203`） |
| **不把 `TodoProvider` / `AgentModeProvider` 的 `StateKeys` 改成每 scope 一个** | 它们的 key 是框架写死的常量（实测就是 `TodoProvider` / `AgentModeProvider`）。框架在同一个 agent 上撞 key 时会抛，**那正是想要的失败**：一个 agent 不该挂两个待办 provider |
| **不在 `CreateContextProviders()` 之外自己 new 一遍** | 同一个 provider 挂两次 = 同一套工具与文本各送两遍。框架的并集**不按名去重**（`WorkflowAgentContextProvider.cs:15-19`）。`WithTodoTracking` 把实例存在字段里，`CreateContextProviders()` 复用它 —— 所以 `scope.Todo` 与组合里那一个是**同一个对象** |

`WithContextCompaction` 的 state key 反而是**每 scope 一个**（`$"{nameof(CompactionProvider)}:{StateDiscriminator}"`），因为框架允许不同 agent 用不同压缩策略在同一个 session 里共存，此时必须能分开存。

---

## 四、顺序：压缩排最前，待办与模式排最后

`CreateContextProviders()`（`WorkflowAgentScope.cs:1764` 起）的固定顺序：

```
[ Compaction?, self, Skills, MCP, Todo?, AgentMode?, …宿主工厂 ]
```

- **`Compaction` 排最前**：它重写的是消息历史，排在它之后的切片应该看到**裁剪过的**版本而不是完整的。
- **`Todo` / `AgentMode` 排在 `Skills` / `MCP` 之后**：前两者是框架的**行为脚手架**，后两者是**上下文来源**。这样提示读起来始终是「先上下文、后指令」，与宿主调用 `With*` 的先后无关 —— 这正是固定顺序这条规则存在的理由。
- **宿主的 `WithContextProvider` 工厂仍然排最后**，宿主工厂顺序不变。

**没挂任何一条时，组合仍然恰好是 `[self, Skills, MCP]`** —— 三个子系统那几条既有断言（`WorkflowAgentContextProviderTests.cs` 的 `ComposedProviders...`）依赖这一点，改顺序前先看它们。

---

## 五、加第四条原生能力时要动的地方

1. `WorkflowAgentScope.cs` 的「框架自带的能力 provider」区加一个 `private AIContextProvider? _xxx;` 字段 + 一个 `public WorkflowAgentScope WithXxx(...)` 方法，方法里**只 new 一次**并存进字段。
2. 同一个文件 `CreateContextProviders()` 里按语义选位置插入（改历史的排最前，纯贡献文本/工具的排 `_mcpProvider` 之后）。
3. `BumpVersion()` —— `Version` 的文档写的是「everything this scope can be configured with」，挂一个 provider 确实改变了模型看到的东西。
4. **核它是不是 `[Experimental]`**：是的话把引用圈进 `#pragma warning disable MAAI001`，并且别让它出现在公开签名里。
5. 测试：`AgentCapabilityProvidersTests.cs` 里加。**走真 agent + 记录型 `IChatClient`**，不要用 `AIContextProvider.InvokingContext` 的公开构造函数 —— 那一个是 `[Experimental]`，用它会把 `MAAI001` 引进测试工程。
