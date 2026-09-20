# VeloxDev.Core.Extension — 事件管线与 transcript

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/`（7 个文件）+ `Agent/TrackedAIFunction.cs`。

---

## 一、三层各是什么，边界在哪

| 层 | 拥有什么 | 不拥有什么 |
|---|---|---|
| **MAF agent 中间件**（`AgentPipelineAgent.cs:29`，`DelegatingAIAgent` 子类） | 「一次 run 何时发生、返回什么」。坐在框架**官方的 middleware 槽位**里，把一次 run 拆成事件发布出去 | 不发工具调用事件 —— 那是 `TrackedAIFunction` 的活 |
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

`AgentTranscript.cs:120-146` 的注释说得很直白：**这两条规则原本是 demo 宿主的视图模型代码，实现错了**，而且因为它就是宿主代码所以测不到、反复回归。所以它被移进库里。

| 规则 | 旧实现的缺陷（同一段注释记载） |
|---|---|
| **片段只在「开着的、同角色的」条目上继续**（`:208-221`）。中间出现任何别的 —— 工具调用、推理块、用户 —— 就关掉它，答案在**新条目**里续写 | 旧宿主只在「最后一条仍是 assistant 消息」时追加，所以一旦落了工具调用，这次回复之后的所有片段都进了侧边日志、**从不渲染** |
| **工具调用是条目，不是分隔符**（`:137-141`）。它是一行带名字与结果的记录 | 旧 markdown 路径会追加工具调用行的（空）文本，于是产生**一列空白分隔线** |

**唯一实现处**：`Append(role, fragment)`（`:208`）。`IsStreaming` 是 `Assistant or Reasoning`（`:69`），所以这两种角色才可能被续写。

**不是线程安全的，且刻意如此**（`:144`）：一个 stage 喂它，在那一个被编组到的线程上。

**别把它当会话状态**：它只是「有序的角色 + 文本」记录。会话是 MAF 的 `AgentSession`。宿主样例读它只做两件事 —— `ToMarkdown()`（`:237`）与 `ToPlainTextLines()`；见 `Examples/Workflow/Common/Lib/ViewModels/Workflow/TreeViewModel.cs:196`、`:200`。`ToMarkdown` 会把**连续的**工具调用攒成一个块（`:240-249`），理由同样是为了让每个 markdown 宿主不必自己写渲染器。

---

## 五、编组：`Post` + await，不是 fire-and-forget

`PipelineDispatch.cs` 的 `RunAsync(SynchronizationContext?, Action)` 会 **await** 这次 post。两点理由：

1. 片段按顺序落地（fire-and-forget 会乱序）；
2. 测试不需要真的调度器。

同一个模式在 `TrackedAIFunction.RunOnContextAsync`（`:104`）里复现，注释说明了为什么**不能**用阻塞式 `Send`：工具体自己可能在 UI 线程上，阻塞式投递会**死锁**。（对比：`Agent/Skills/SkillScope.cs:RunOnUI` 用的就是阻塞 `Send`，因为技能刷新是纯 UI 侧操作、不在工具体内。）

**同步上下文解析是延迟的** —— `ToolPipeline.MarshalTo` 是 `Func<SynchronizationContext?>`（`WorkflowAgentToolkit.cs:214` 传入 `() => _scope.UIContext`）。理由：宿主完全可能在 toolkit 已经建好**之后**才调 `WithSynchronizationContext`。

---

## 六、扩展点：加一个 stage

**官方做法**

1. 实现 `IAgentPipelineStage`（`AgentPipeline.cs:16`），或直接用 `DelegateAgentPipelineStage`（`:26`）/ `Use(handler)` 重载（`:62`）。
2. `pipeline.Use(stage)` 追加。顺序即执行顺序。
3. 挂在 scope 的 pipeline 上：`WorkflowAgentScope.Pipeline`（`WorkflowAgentScope.cs:1383` 一带）返回的是**新建的组合**：`TextPipeline → SharedTools → CreateToolkit().CreateAccountingStage()`。想接在最后，就用 `WithPipeline(this AIAgent, …)`（`AgentPipelineAgent.cs:187`）或 `UseAgentPipeline(AIAgentBuilder, …)`（`:182`）。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在 stage 里抛异常来中断 run | 异常被 `InvokeAsync` 吞成 `StageFailed`（`AgentPipeline.cs:103-106`）；在工具路径上它已经被外层 `catch` 变成工具错误了 |
| 在 stage 里 `await Task.Run(...)` 或 `ConfigureAwait(false)` 之后改 transcript | 片段会落到线程池线程上；`AgentTranscript` 不是线程安全的（`AgentTranscript.cs:144`），且 `ObservableCollection` 的绑定会被跨线程改 |
| 自己 `new ToolPipeline(...)` 给子系统用 | 会得到**另一本预算账本**。共享同一个 `WorkflowAgentToolkit.Tools`（`WorkflowAgentToolkit.cs:214`）才是官方做法 —— `CreateContextProviders()` 就是这么把同一个引用递下去的 |
| 想「抑制」某个事件却只在 stage 里 `return` | 不调 `next` 只对**下游**生效，`AgentPipeline` 自己的 `StageFailed` / 上游观察者仍然看得见 |
| 往 pipeline 里塞「工具过滤」 | 工具过滤在 `CreateTools` 的 `.Where(IsToolEnabled)`（`WorkflowAgentToolkit.cs:47`）与 `CheckBudget`（`:255`）两处。stage 层过滤会绕过预算记账与脏标记 |

---

## 七、死面

- `Agent/Pipelines/` 下 7 个文件**全部**在仓库内有真实调用者（`TextPipeline`、`ToolPipeline`、`AgentPipelineAgent` 由 `WorkflowAgentScope.Pipeline` 组装；`AgentTranscript` 由 demo 的 `TreeViewModel` 消费）。这个目录没有死面。
- 唯一值得留意的是**事件种类里没有「交互」类**：`RequestSelection` / `RequestConfirmation` 走的是工具返回 + 宿主 handler，不走事件管线。想在 UI 上看到它们，订阅 `WorkflowAgentScope.ToolCalled` 或让工具自己回调。
