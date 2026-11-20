# VeloxDev.Core.Extension — 事件管线与 transcript

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/` + `Agent/TrackedAIFunction.cs`。

---

## 一、三层各是什么，边界在哪

| 层 | 拥有什么 | 不拥有什么 |
|---|---|---|
| **MAF agent 中间件**（`AgentPipelineAgent`，`DelegatingAIAgent` 子类） | 「一次 run 何时发生、返回什么」。坐在框架**官方的 middleware 槽位**里，把一次 run 拆成事件发布出去 | 不发工具调用事件 —— 那是 `TrackedAIFunction` 的活 |
| **`AgentPipeline`**（`AgentPipeline.cs`） | 一条有序 stage 链 + 失败隔离。这是**本库自己的一层**，不是 MAF 的 | 不驱动 run。stage 只「观察」，通过叫不叫 `next` 决定下游看见什么（见 `IAgentPipelineStage` 的注释） |
| **`TrackedAIFunction`**（`TrackedAIFunction.cs`） | 工具调用这一侧的唯一事件源。它在 MAF 的 `DelegatingAIFunction` 槽位上，因此与 agent 中间件天然互补 | 不持有 pipeline 的所有权，只是往上面 publish |

**事件汇合点只有两个**：`AgentPipelineAgent`（run 级：turn / 文本 / 推理）与 `TrackedAIFunction`（工具级）。宿主直接订阅 pipeline 的 `StageFailed`（`AgentPipeline.StageFailed`）就能看到所有被隔离掉的异常。

**token 用量有两条来路，都要接**：非流式回合的 `AgentTurnCompleted.Usage`（`response.Usage`），以及流式回合里单独到达的 `UsageContent` → `AgentUsageReported`。`WorkflowAgentScope.TokenUsageStage` 同时收这两条；只接前一条，流式的回合就不花钱。

---

## 二、为什么「阶段抛异常不能让 run 失败」是硬约束

**工具调用的 publish 发生在 `TrackedAIFunction` 的 `try` 里面**（`TrackedAIFunction.InvokeCoreInnerAsync`）。那个 `catch` 把任何异常变成**工具的 error 结果**（`TrackedAIFunction.Error` 的信封 `{"status":"error","message":…}`）。于是：一个**只是在画界面**的 stage 抛异常，会让模型收到「你的工具失败了」，而工具其实成功了。

所以每个 stage 被 `try/catch` 单独包住（`AgentPipeline.InvokeAsync` 的 try/catch），失败**跳过链的剩余部分**并走 `StageFailed` 上报。

**推论**：写 stage 时不要指望异常能传出去；也不要指望「我不调 `next` 就等于 run 失败」——那只对下游 stage 生效。

---

## 三、索引传递，不是闭包游标

`InvokeAsync` 上方的注释点明了 `InvokeAsync(index, …)` 的写法不是随手写的：**一个 stage 发布第二个事件时，不能把外层链推进过头**。`Use(…)` 追加到 `_stages`，`next` 是 `next => InvokeAsync(index + 1, next, ct)`。

**写 stage 的后果**：stage 内再 `PublishAsync` 一个事件，那是一个**独立的整链遍历**，与当前这次互不干扰。想「改写事件给下游」就调 `next(改写后的事件)`；想「丢弃」就不调。

---

## 四、`AgentTranscript` 的两条规则

`AgentTranscript` 的类注释：**这两条规则属于库，不属于 demo 宿主的视图模型**。

| 规则 | 会出错的形态（同一段注释记载） |
|---|---|
| **片段只在「开着的、同角色的」条目上继续**（`Append`）。中间出现任何别的 —— 工具调用、推理块、用户 —— 就关掉它，答案在**新条目**里续写 | 只在「最后一条仍是 assistant 消息」时追加 ⇒ 一旦落了工具调用，这次回复之后的所有片段都进侧边日志、**从不渲染** |
| **工具调用是条目，不是分隔符**（`AgentTranscript` 类注释的第 2 条）。它是一行带名字与结果的记录 | 把工具调用当分隔符追加（空）文本 ⇒ **一列空白分隔线** |

**实现处**：`Append(role, fragment)`。`IsStreaming` 是 `Assistant or Reasoning`（`AgentTranscriptEntry.IsStreaming`），所以这两种角色才可能被续写。**它每次调用都复制整条答案**（`Text += fragment`），所以调用频率决定了代价 —— 由 `TextPipeline` 按千字节批量喂（见 §五），逐片段直接喂它是 O(答案) × 片段数。

**不是线程安全的，且刻意如此**（`AgentTranscript` 类注释）：一个 stage 喂它，在那一个被编组到的线程上。

**宿主侧的消费契约**（`Examples/Workflow/Common/Lib/ViewModels/Workflow/TreeViewModel.cs`，七家共用）：内容增长走 120 ms 节流（`QueueConversationRender`），而**新开一个条目会立刻整篇重渲**（`OnTranscriptEntriesChanged`）——`AgentTranscriptTests.ConversationMarkdown_WrapsReasoningInAChatPanel` 钉的就是后者，把新条目也并进节流会打破它（实测过）。`AgentLog` 由尾巴对齐同步（只动与前缀不同的那几条），不再是 `Clear` + 逐条 `Add`。

**`AgentLog` 只有一个写者：那条投影。** 错误走转录：`TreeViewModel.AppendAgentError(message)` → `AgentTranscript.AddError`，两条渲染路径各自加自己的前缀（行内 `[Error] …`、markdown `**错误：**`），所以**传裸消息**；同一条错误因此在七家都出现。`AppendAgentLog` 不碰 `AgentLog`，只喂 `AgentMessages` —— 它没有生产调用者，但 `AgentMessages` 是**有意留着的第三种形状**（按角色渲染的宿主可以绑它，见 `Src/.../Test/Examples/AgentTranscriptTests.cs` 的类注释）。MAUI 的发送失败提示调 `AppendAgentError`。

**它不是会话状态**：它只是「有序的角色 + 文本」记录。会话是 MAF 的 `AgentSession`。宿主样例读它做三件事 —— `ToMarkdown(AgentMarkdownOptions?)`、`ToPlainTextLines()`，以及从 `Entries` 拿结构化条目；见 `Examples/Workflow/Common/Lib/ViewModels/Workflow/TreeViewModel.cs` 的 `ToMarkdown` / `ToPlainTextLines` 调用点。`ToJson()` / `Restore()` 持久化的也只是这份**显示记录**（角色 / 文本 / 时间戳 / 工具名 / 详情 / 结局），恢复它不会恢复模型的记忆 —— 要续对话仍需持久化 `AgentSession`。`ToMarkdown` 默认把**连续的**工具调用攒成一个块；`ChatFences` 打开时改为每个工具一格折叠面板。

### 推理的渲染形状（`ToMarkdown` 的文档注释 + `AgentMarkdownOptions`）

推理默认走**围栏代码块**，不是加粗段落（段落与 `Assistant` 同形，消费端无法独立包裹）。`ChatFences` 打开时改走 `chat` 折叠面板，推理与工具载荷因此都不占阅读路径。形状由 `AgentMarkdownOptions`（与 `AgentTranscript` 同文件）决定，所有成员都有出厂默认值，**传 `null` 就是出厂形状**：

| 成员 | 默认 | 语义 |
|---|---|---|
| `ReasoningFence` | `"thinking"` | 围栏的 info string。**空串 = 关掉围栏**，退回旧的 `**思考：**\n\n{text}`；`ChatFences` 打开时不生效 |
| `ReasoningHeading` | `"**思考：**"` | 围栏**之上、之外**的标签；`null` = 无标签（放进围栏会被当字面代码）；`ChatFences` 打开时由面板标签替代 |
| `ChatFences` | `false` | 推理与工具调用改用 `chat[label][state]` 折叠面板（AvalonMarkdown 12 的方言）。开着的推理用 `thinking` 转；关掉后统一用 `done` 显示勾（**不要用 `thinking,static`** —— 冻结的灯泡光环看起来像停在半路）。工具按结局用 `done` / `error`，没有结局的（旧文档恢复）用 `tool,static` |
| `ToolDetailLimit` | `4000` | 每个工具面板携带的载荷上限（字符）。`0` = 不限。整篇文档每次重渲都会过桥，所以要有上限；截断处写明原长度 |

三条必须知道的实现细节：

- **围栏长度按正文里最长的连续反引号串 + 1 算**（`FenceLength`），下限 3。CommonMark 在遇到第一行「反引号数 ≥ 开围栏」时就闭合 —— 模型思考里写 ` ``` ` 是常态，固定 3 个会被内容自己提前闭合，把**后续整个对话**吞进代码块。`chat` 面板的正文里还有工具载荷的代码围栏，所以外层同样按 `FenceLength(body)` 加长。
- **info string 里的反引号被剔除**（`FenceInfo` 用于普通围栏，`ChatLabel` 用于面板标签）。它由宿主提供，一个反引号会并进开围栏、把开围栏拉得比闭围栏长，于是**永远闭合不了**。面板标签还不能含 `]` —— `chat` 围栏按 `]` 切分组，早闭合会让后面整段变成「组间有文本」的错误。
- **工具的「结局」徽标取 `AgentToolOutcome`，不靠标签里的字推断**：`Succeeded` → `done`，`Failed` / `Refused` → `error`。标签写成 `工具 {ToolName} · {Outcome}`，类别、人名与结局各归各的。

**`ToPlainTextLines()` 一行不动，也不给它对称的 options**：它的「一个条目一行」是一份**契约**而非形状（`ToPlainTextLines` 的文档注释），宿主靠 `Count ==` + `SequenceEqual` 同步、再按前缀解析回角色；推理在那儿早就是 `[Thinking] …` 行。
---

## 五、编组：`Post` + await，不是 fire-and-forget

`PipelineDispatch.cs` 的 `RunAsync(SynchronizationContext?, Action)` 会 **await** 这次 post。两点理由：

1. 片段按顺序落地（fire-and-forget 会乱序）；
2. 测试不需要真的调度器。

**片段是攒够了才 post 的，不是逐片段 post**：`TextPipeline` 持有开着的那条条目的尾巴，每满 `FlushThreshold`（1024 字符）或遇到边界（新回合、工具调用、回合结束）才写一次。所以 dispatcher 往返从「每 token 一次」降到「每千字节一次」——实测 5,000 个 8 字节片段：post 5,000 → 39 次，该段分配 45 KB/片段 → 1.5 KB/片段。边界处**必须先落地再处理**：工具调用会在下游把开着的条目关掉，攒着的尾巴晚一步就会落进工具调用之后新开的那条。**这正是 `WorkflowAgentScope.Pipeline` 里 TextPipeline 必须排第一的原因**。

同一个模式在 `TrackedAIFunction.RunOnContextAsync` 里复现，注释说明了为什么**不能**用阻塞式 `Send`：工具体自己可能在 UI 线程上，阻塞式投递会**死锁**。（对比：`Agent/Skills/SkillScope.cs` 的 `RunOnUI` 用的就是阻塞 `Send`，因为技能刷新是纯 UI 侧操作、不在工具体内。）

**同步上下文解析是延迟的** —— `ToolPipeline.MarshalTo` 是 `Func<SynchronizationContext?>`（`WorkflowAgentToolkit.Tools` 传入 `() => _scope.UIContext`）。理由：宿主完全可能在 toolkit 已经建好**之后**才调 `WithSynchronizationContext`。

---

## 六、扩展点：加一个 stage

**官方做法**

1. 实现 `IAgentPipelineStage`（`AgentPipeline.cs`），或直接用 `DelegateAgentPipelineStage` / `Use(handler)` 重载。
2. `pipeline.Use(stage)` 追加。顺序即执行顺序。
3. 挂在 scope 的 pipeline 上：`WorkflowAgentScope.Pipeline`（首次读时组装并缓存进 `_pipeline`）返回的是**新建的组合**：`TextPipeline → SharedTools → CreateToolkit().CreateAccountingStage()`。**这个顺序是承重的**（TextPipeline 攒着尾巴，必须在工具 stage 关掉开着的那条之前落地，见 §五），不是「谁先被读到」的问题。想接在最后，就用 `WithPipeline(this AIAgent, …)` 或 `UseAgentPipeline(AIAgentBuilder, …)`。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在 stage 里抛异常来中断 run | 异常被 `InvokeAsync` 吞成 `StageFailed`（`InvokeAsync` 的 catch）；在工具路径上它已经被外层 `catch` 变成工具错误了 |
| 在 stage 里 `await Task.Run(...)` 或 `ConfigureAwait(false)` 之后改 transcript | 片段会落到线程池线程上；`AgentTranscript` 不是线程安全的（`AgentTranscript` 类注释），且 `ObservableCollection` 的绑定会被跨线程改 |
| 自己 `new ToolPipeline(...)` 给子系统用 | 会得到**另一本预算账本**。共享同一个 `WorkflowAgentToolkit.Tools`才是官方做法 —— `CreateContextProviders()` 就是这么把同一个引用递下去的 |
| 想「抑制」某个事件却只在 stage 里 `return` | 不调 `next` 只对**下游**生效，`AgentPipeline` 自己的 `StageFailed` / 上游观察者仍然看得见 |
| 往 pipeline 里塞「工具过滤」 | 工具过滤在 `CreateTools` 的 `.Where(IsToolEnabled)`（`WorkflowAgentToolkit.CreateTools`）与 `CheckInvocation` 两处。stage 层过滤会绕过预算记账与脏标记 |

---

## 七、死面

- `Agent/Pipelines/` 下的文件在仓库内都有真实调用者，没有死面（`TextPipeline`、`ToolPipeline`、`AgentPipelineAgent` 由 `WorkflowAgentScope.Pipeline` 组装；`AgentTranscript` 由 demo 的 `TreeViewModel` 消费）。
- `AgentMarkdownOptions` 与 `AgentTranscript` **同文件**（同一文件承载多个公开类型是本目录既有做法，`AgentEvent.cs`、`AgentPipeline.cs` 都是）。
- **`AgentMarkdownOptions` 的生产消费者只有一个**：`Examples/Workflow/Common/Lib/ViewModels/Workflow/TreeViewModel.cs` 的 `ConversationMarkdownOptions`（`ChatFences = true`，只对绑 `ConversationMarkdown` 的 Avalonia 生效）。其余成员仍走默认值；测试里 `AgentTranscriptTests.cs` 也显式构造它。**形状回归没有编译期信号** —— 守卫在 `VeloxDev.Core.Extension.Test/Agent/Pipelines/AgentTranscriptTests.cs` 的测试里。
- **事件种类里没有「交互」类**：`RequestSelection` / `RequestConfirmation` 走的是工具返回 + 宿主 handler，不走事件管线。想在 UI 上看到它们，订阅 `WorkflowAgentScope.ToolCalled` 或让工具自己回调。
