# VeloxDev.Core.Extension — 事件管线与 transcript

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/`（7 个文件）+ `Agent/TrackedAIFunction.cs`。

---

## 一、三层各是什么，边界在哪

| 层 | 拥有什么 | 不拥有什么 |
|---|---|---|
| **MAF agent 中间件**（`AgentPipelineAgent.cs:33`，`DelegatingAIAgent` 子类） | 「一次 run 何时发生、返回什么」。坐在框架**官方的 middleware 槽位**里，把一次 run 拆成事件发布出去 | 不发工具调用事件 —— 那是 `TrackedAIFunction` 的活 |
| **`AgentPipeline`**（`AgentPipeline.cs:46`） | 一条有序 stage 链 + 失败隔离。这是**本库自己的一层**，不是 MAF 的 | 不驱动 run。stage 只「观察」，通过叫不叫 `next` 决定下游看见什么（`:16-23`） |
| **`TrackedAIFunction`**（`TrackedAIFunction.cs:29`） | 工具调用这一侧的唯一事件源。它在 MAF 的 `DelegatingAIFunction` 槽位上，因此与 agent 中间件天然互补 | 不持有 pipeline 的所有权，只是往上面 publish |

**事件汇合点只有两个**：`AgentPipelineAgent`（run 级：turn / 文本 / 推理）与 `TrackedAIFunction`（工具级）。宿主直接订阅 pipeline 的 `StageFailed`（`AgentPipeline.cs:68`）就能看到所有被隔离掉的异常。

---

## 二、为什么「阶段抛异常不能让 run 失败」是硬约束

`AgentPipeline.cs:70-83` 写明了理由，且理由本身依赖另一处实现细节：

**工具调用的 publish 发生在 `TrackedAIFunction` 的 `try` 里面**（`TrackedAIFunction.cs:49-85`）。那个 `catch` 把任何异常变成**工具的 error 结果**（`:130` 的信封 `{"status":"error","message":…}`）。于是：一个**只是在画界面**的 stage 抛异常，会让模型收到「你的工具失败了」，而工具其实成功了。

所以每个 stage 被 `try/catch` 单独包住（`AgentPipeline.cs:94-106`），失败**跳过链的剩余部分**并走 `StageFailed` 上报。「上报而不是吞掉」也是有意的：一条静默停止上报的管线比从来没有这个 stage 更糟。

**推论**：写 stage 时不要指望异常能传出去；也不要指望「我不调 `next` 就等于 run 失败」——那只对下游 stage 生效。

---

## 三、索引传递，不是闭包游标

`AgentPipeline.cs:87-88` 的注释点明了 `InvokeAsync(index, …)` 的写法不是随手写的：**一个 stage 发布第二个事件时，不能把外层链推进过头**。`Use(…)` 追加到 `_stages`，`next` 是 `next => InvokeAsync(index + 1, next, ct)`。

**写 stage 的后果**：stage 内再 `PublishAsync` 一个事件，那是一个**独立的整链遍历**，与当前这次互不干扰。想「改写事件给下游」就调 `next(改写后的事件)`；想「丢弃」就不调。

---

## 四、`AgentTranscript` 的两条规则（这是这个类型存在的全部理由）

`AgentTranscript.cs:138-165` 的注释说得很直白：**这两条规则原本是 demo 宿主的视图模型代码，实现错了**，而且因为它就是宿主代码所以测不到、反复回归。所以它被移进库里。

| 规则 | 旧实现的缺陷（同一段注释记载） |
|---|---|
| **片段只在「开着的、同角色的」条目上继续**（`:227-241`）。中间出现任何别的 —— 工具调用、推理块、用户 —— 就关掉它，答案在**新条目**里续写 | 旧宿主只在「最后一条仍是 assistant 消息」时追加，所以一旦落了工具调用，这次回复之后的所有片段都进了侧边日志、**从不渲染** |
| **工具调用是条目，不是分隔符**（`:156-160`）。它是一行带名字与结果的记录 | 旧 markdown 路径会追加工具调用行的（空）文本，于是产生**一列空白分隔线** |

**唯一实现处**：`Append(role, fragment)`（`:227`）。`IsStreaming` 是 `Assistant or Reasoning`（`:69`），所以这两种角色才可能被续写。

**不是线程安全的，且刻意如此**（`:163`）：一个 stage 喂它，在那一个被编组到的线程上。

**别把它当会话状态**：它只是「有序的角色 + 文本」记录。会话是 MAF 的 `AgentSession`。宿主样例读它做三件事 —— `ToMarkdown(AgentMarkdownOptions?)`（`:269`）、`ToPlainTextLines()`（`:341`），以及从 `Entries` 拿结构化条目；见 `Examples/Workflow/Common/Lib/ViewModels/Workflow/TreeViewModel.cs:196`、`:200`。`ToMarkdown` 会把**连续的**工具调用攒成一个块（`:276-288`），理由同样是为了让每个 markdown 宿主不必自己写渲染器。

### 推理的渲染形状（`:249-269` 的文档注释 + `AgentMarkdownOptions`）

推理走**围栏代码块**，不是加粗段落 —— 段落的形状与 `Assistant` **完全同形**，只差标签，消费端拿到扁平字符串后无法把它独立包裹成一块。形状由 `AgentMarkdownOptions`（`:123`，与 `AgentTranscript` 同文件）决定，两个成员都有出厂默认值，**传 `null` 就是出厂形状**：

| 成员 | 默认 | 语义 |
|---|---|---|
| `ReasoningFence` | `"thinking"` | 围栏的 info string。**空串 = 关掉围栏**，退回旧的 `**思考：**\n\n{text}` |
| `ReasoningHeading` | `"**思考：**"` | 围栏**之上、之外**的标签；`null` = 无标签（放进围栏会被当字面代码） |

两条必须知道的实现细节：

- **围栏长度按正文里最长的连续反引号串 + 1 算**（`FenceLength`，`:400`），下限 3。CommonMark 在遇到第一行「反引号数 ≥ 开围栏」时就闭合 —— 模型思考里写 ` ``` ` 是常态，固定 3 个会被内容自己提前闭合，把**后续整个对话**吞进代码块。
- **info string 里的反引号被剔除**（`FenceInfo`，`:416`）。它由宿主提供，一个反引号会并进开围栏、把开围栏拉得比闭围栏长，于是**永远闭合不了**。

**`ToPlainTextLines()` 一行不动，也不给它对称的 options**：它的「一个条目一行」是一份**契约**而非形状（`:334-339` 的注释），宿主靠 `Count ==` + `SequenceEqual` 同步、再按前缀解析回角色；推理在那儿早就是 `[Thinking] …` 行。纯文本宿主没有歧义要解，加 options 只会多一个面。

---

## 五、编组：`Post` + await，不是 fire-and-forget

`PipelineDispatch.cs` 的 `RunAsync(SynchronizationContext?, Action)` 会 **await** 这次 post。两点理由：

1. 片段按顺序落地（fire-and-forget 会乱序）；
2. 测试不需要真的调度器。

同一个模式在 `TrackedAIFunction.RunOnContextAsync`（`:104`）里复现，注释说明了为什么**不能**用阻塞式 `Send`：工具体自己可能在 UI 线程上，阻塞式投递会**死锁**。（对比：`Agent/Skills/SkillScope.cs:RunOnUI` 用的就是阻塞 `Send`，因为技能刷新是纯 UI 侧操作、不在工具体内。）

**同步上下文解析是延迟的** —— `ToolPipeline.MarshalTo` 是 `Func<SynchronizationContext?>`（`WorkflowAgentToolkit.cs:242` 传入 `() => _scope.UIContext`）。理由：宿主完全可能在 toolkit 已经建好**之后**才调 `WithSynchronizationContext`。

---

## 六、扩展点：加一个 stage

**官方做法**

1. 实现 `IAgentPipelineStage`（`AgentPipeline.cs:16`），或直接用 `DelegateAgentPipelineStage`（`:26`）/ `Use(handler)` 重载（`:62`）。
2. `pipeline.Use(stage)` 追加。顺序即执行顺序。
3. 挂在 scope 的 pipeline 上：`WorkflowAgentScope.Pipeline`（`WorkflowAgentScope.cs:1526` 的 getter，首次读时在 `:1541-1546` 组装并缓存进 `_pipeline`）返回的是**新建的组合**：`TextPipeline → SharedTools → CreateToolkit().CreateAccountingStage()`。想接在最后，就用 `WithPipeline(this AIAgent, …)`（`AgentPipelineAgent.cs:187`）或 `UseAgentPipeline(AIAgentBuilder, …)`（`:182`）。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在 stage 里抛异常来中断 run | 异常被 `InvokeAsync` 吞成 `StageFailed`（`AgentPipeline.cs:103-106`）；在工具路径上它已经被外层 `catch` 变成工具错误了 |
| 在 stage 里 `await Task.Run(...)` 或 `ConfigureAwait(false)` 之后改 transcript | 片段会落到线程池线程上；`AgentTranscript` 不是线程安全的（`AgentTranscript.cs:163`），且 `ObservableCollection` 的绑定会被跨线程改 |
| 自己 `new ToolPipeline(...)` 给子系统用 | 会得到**另一本预算账本**。共享同一个 `WorkflowAgentToolkit.Tools`（`WorkflowAgentToolkit.cs:242`）才是官方做法 —— `CreateContextProviders()` 就是这么把同一个引用递下去的 |
| 想「抑制」某个事件却只在 stage 里 `return` | 不调 `next` 只对**下游**生效，`AgentPipeline` 自己的 `StageFailed` / 上游观察者仍然看得见 |
| 往 pipeline 里塞「工具过滤」 | 工具过滤在 `CreateTools` 的 `.Where(IsToolEnabled)`（`WorkflowAgentToolkit.cs:75`）与 `CheckBudget`（`:283`）两处。stage 层过滤会绕过预算记账与脏标记 |

---

## 七、死面

- `Agent/Pipelines/` 下 7 个文件**全部**在仓库内有真实调用者（`TextPipeline`、`ToolPipeline`、`AgentPipelineAgent` 由 `WorkflowAgentScope.Pipeline` 组装；`AgentTranscript` 由 demo 的 `TreeViewModel` 消费）。这个目录没有死面。
- 文件计数仍是 7：`AgentMarkdownOptions` 与 `AgentTranscript` **同文件**，没新增文件（同一文件承载多个公开类型是本目录既有做法，`AgentEvent.cs`、`AgentPipeline.cs` 都是）。
- **`AgentMarkdownOptions` 有一个成员在仓库内无人显式构造**（`ReasoningFence` —— demo 全走默认）。这是**有意**的默认值面而不是死面：它的存在意义就是「宿主不改任何东西也拿到围栏」，仓库里没有第二个消费者是正常的。但它同时意味着**形状回归不会有编译期信号** —— 守卫在 `VeloxDev.Core.Extension.Test/Agent/Pipelines/AgentTranscriptTests.cs` 的 8 条测试里。
- 唯一值得留意的是**事件种类里没有「交互」类**：`RequestSelection` / `RequestConfirmation` 走的是工具返回 + 宿主 handler，不走事件管线。想在 UI 上看到它们，订阅 `WorkflowAgentScope.ToolCalled` 或让工具自己回调。
