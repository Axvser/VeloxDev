# WorkflowSystem 扩展

> 配套阅读：`architecture.md`（本目录）；平台差异见 `adapters/<平台>.md`；连接/视图层的**逐平台做法**见
> `skills/veloxdev-create-workflow/references/new-adapter.md` 与 `references/view-layer.md`。本文**不重复**平台差异。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 一种新节点 | `[WorkflowBuilder.NodeAttribute<THelper>]` + 重写 `THelper.ReceiveAsync` | `Templates/WorkflowBuilder.cs:28`；`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:123` |
| 一种新插槽 | `[WorkflowBuilder.SlotAttribute<THelper>]` | `Templates/WorkflowBuilder.cs:40` |
| 一种新连线 | `[WorkflowBuilder.LinkAttribute<THelper>(Type? slotType)]`；或在 TreeHelper 重写 `CreateLink` 统一换 | `Templates/WorkflowBuilder.cs:49`；`Templates/Helpers/TreeHelper.cs:171` |
| 自定义整棵树 | `[WorkflowBuilder.TreeAttribute<THelper>(Type? virtualLinkType, Type? virtualSlotType)]` | `Templates/WorkflowBuilder.cs:13` |
| 连接规则 | 重写 `TreeHelper<T>.ValidateConnection(sender, receiver)`（默认 `true`） | `Templates/Helpers/TreeHelper.cs:296` |
| 节点默认坐标/尺寸 | `[DefaultAnchor(h, v, layer)]` / `[DefaultSize(w, h)]`，可加在不带 `[WorkflowBuilder.*]` 的子类上 | `Templates/NodeLayoutAttributes.cs:12,33` |
| 分支路由节点 | 实现 `ICompileTimeRouter`（`GetRouteTable` + `ResolveRouteKey`） | `CompilerEx/Compile/Contracts/ICompileTimeRouter.cs` |
| 读编译身份 | 实现 `ICompileTimeAware`（`CompileContext.Order/ChainIndex/Offset/InputNodes`） | `CompilerEx/Compile/Contracts/ICompileTimeAware.cs` |
| 运行期读上下文 | 实现 `IRuntimeAware`（`AttachRuntimeContext`） | `CompilerEx/Runtime/Contracts/IRuntimeAware.cs` |
| 重定向（回退到更早的 Order 重跑） | 实现 `IRedirectable`（`ResolveRedirectAsync`） | `CompilerEx/Runtime/Contracts/IRedirectable.cs` |
| 运行要能**中途存盘、之后再接着跑** | 配 `RuntimeContext.CheckpointStore`（`IExecutionCheckpointStore`，Core 自带 `InMemoryCheckpointStore`，落盘用 `Core.Extension` 的 `FileCheckpointStore`）；恢复把 store 里的 `ExecutionCheckpoint` 传给 `RuntimeEngine.RunAsync(graph, context, ct, resumeFrom)`。**图必须是同一张**（指纹不符会拒），序列化往返过的图恢复不了 | `CompilerEx/Runtime/Contracts/IExecutionCheckpointStore.cs`；[compiler-execution.md](compiler-execution.md) §十一 |
| 运行要能暂停 / 观察 / 重试 / 收结构化错误 / 失败后补偿 | **不是实现接口，是往会话插一个对象**：`RuntimeContext` 的 `ExecutionGate` / `Observer` / `RetryPolicy` / `ErrorSink` / `Compensation`。五个契约都随库带默认实现（`ManualExecutionGate`、`ExponentialBackoffRetry`…）与委托适配器，不配置就什么都没有 | `CompilerEx/Runtime/Model/RuntimeContext.cs`（「可选能力」一节）；语义见 [compiler-execution.md](compiler-execution.md) §十 |
| 数量可变的端口集合 | `[VeloxProperty] [SlotSelectors(typeof(...))] public partial SlotEnumerator<TSlot> X { get; set; }` | `SelectorEx/SlotEnumerator.cs:13`；`Src/Core/VeloxDev.Core/AI/SlotSelectorsAttribute.cs:38` |
| 自定义空间索引 | 实现 `ISpatialBoundsProvider`（`Bounds` + `INotifyPropertyChanged`）/ `ISpatialMap<T>` | `Interfaces/WorkflowSystem/ISpatialBoundsProvider.cs`、`ISpatialMap.cs:12` |
| 网格装饰器 / 小地图 | 实现 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` | `Interfaces/WorkflowSystem/IWorkflowGridDecorator.cs:15`、`IWorkflowMinimapOverlay.cs:18` |
| 让连线可被命中 / 自定义它的曲线 | **不实现接口，是发布**：连线视图画完调 `link.PublishCurve(LinkCurve, this)`；形状归视图（Core 不假定贝塞尔），判定归 Core | `GUI/Interaction/LinkHitTestEx.cs`、`GUI/Interaction/LinkCurve.cs` |
| 让悬停能看见 / 让连线响应 hover 外观 | **外观是宿主的**：订**那条线自己的** `((IInputEvents)link.GetHelper()).Input.PointerEntered` / `PointerExited` 自己画。路由保证「离开的先收 Exited、进入的后收 Entered」，互斥不用记账 | `GUI/Events/Input/IInputEvents.cs`、示例见 `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/LinkView.xaml.cs` |
| 改连线的命中/删除策略 | 取 `WorkflowInput.For(tree)`（**只有这一个位置**）改 `AutoDelete` / `HitRadius`，或读 `PointerTarget` / `HoveredLink`；要什么事件就在组件上订标准输入 | `GUI/Events/WorkflowInput.cs` |
| **否决某一次**连线动作（而不是全局关开关） | 订标准输入（如 `KeyDown`）并在 `e.Handle.PreventDefault` 里拒绝这一次 —— 框架那一手（Delete 删线）就不执行；`StopPropagation` 则是「到此为止、祖先一个都收不到」。**两个标志都不设 = 一切照旧** | `GUI/Events/WorkflowEventHandle.cs` |
| **把节点/插槽/树的动作也接成标准事件** | 取该组件的 Helper 并按**能力接口**转型：`((IWorkflowNodeEvents)node.GetHelper())`、`IWorkflowSlotEvents`、`IWorkflowTreeEvents`。已实现五对：`Moving/Moved`、`Resizing/Resized`、`Deleting/Deleted`（node）、`ChannelChanging/Changed`（slot）、`Connecting/Connected`（tree） | `GUI/Events/{Node,Slot,Tree}/IWorkflow*Events.cs` |
| **把模型事件交给宿主，按平台族分三种**（2026-10-03 用户定，**不要 hub**） | ①**有附加属性的四家**（WPF/Avalonia/WinUI/MAUI）：附加属性 + 绑定一个 sink 对象 —— `behaviors:WorkflowEvents.Node="{Binding NodeEvents}"`（`.Slot` / `.Tree` 同形）；②**Razor**（类 XAML）：沿用订阅；③**无标记语言的两家**（WinForms/Jalium）：**适配器基类提供 `protected virtual OnXxx(args)` 钩子**，宿主重写即得 | `GUI/Events/IWorkflow*EventSink.cs`、`GUI/Events/WorkflowEventRelay.cs`；WPF 参考实现 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowEvents.cs` |
| **连线的右键菜单** | **归模板**（2026-10-03 用户定，推翻原先「菜单是 demo 策略」那条）：条目声明在 `workflow-tree-view` 条目里，用户改模板增删；无标记语言的两家由 `WorkflowTreeView` 基类的 `OnBuildLinkMenu` 钩子给出 | [item-template-specifications.md](../specifications/item-template-specifications.md) §五 |
| 右键菜单的请求与开合 | Core **没有**菜单事件了（2026-10-04 起）：**适配器从自己的 `PointerPressed(Right, link)` 里弹**（模板只声明条目，见 `WorkflowSurfaceBehavior.LinkMenuKey`）；宿主想否决，就在链上更靠前的一级（连线自己）订同一个事件并置 `PreventDefault` —— 顺序由「目标先于祖先」保证。开合由适配器自己置 `WorkflowInput.IsSuspended` | 七家适配器的 `WorkflowSurfaceBehavior` / `WorkflowTreeView` 基类 |
| **让菜单不活得比它的线久**（Delete 键外的删除也算：Undo、Agent 改树） | 订**既有的** `tree.GetHelper().LinkRemoved`（Delete/Undo/Agent 改树都会发），比对是不是自己那份菜单指着的那条，是就关掉并放开挂起。**接线落点是适配层**（WPF/Avalonia/WinUI/MAUI 四家：`WorkflowSurfaceBehavior.LinkMenuKey` 那个附着行为；Razor：组件参数 `LinkMenu` + RenderFragment；无标记语言两家 WinForms/Jalium：基类），模板不参与 | `Templates/Helpers/TreeHelper.cs`（`OnLinksChanged`） |

> ⚠ **别把连线视图做成吃掉整块画布的命中面**（给它加背景、或让容器接指针）—— 那会吞掉画布手势。命中面必须仍然只是**画出来的那道描边**；hub 也是按发布的那条曲线判距的。
> ⚠ **组件落位的事件里，`Anchor` 必须是完整落位 —— 图层（`Anchor.Layer`）跟着走。**（2026-10-03 用户定）
> 典型是 `NodeMoveEventArgs.From/To`：宿主拿 `To` 自己落位、或存 `From` 以后撤销时，**不能**把节点的图层悄悄抹成 0。
> 造值一律 `new Anchor(x, y, 源.Layer)`，不要 `new Anchor(x, y, 0)`（`StandardMove:103` 就是这么写的，测试钉住 layer=7 往返）。
> 例外只有两处：**指针位置**（`PointerEventArgs.Position` 的 `Layer` 取来源视图的图层，但喂给 `SetPointerCommand` 的那个 anchor 仍是 0）；以及指针变成**虚拟连线终点**时 ——
> 那一处由 `StandardSetPointer` 统一取**起点那一端**的图层（七家适配器交上来的锚带不带图层都不影响结果）。
> ⚠ **模型层事件经「能力接口」暴露，不加进 `IWorkflowXxxViewModelHelper`** —— 往那个接口加成员会打断每一个实现者。
> `NodeHelper<T>` / `SlotHelper<T>` / `TreeHelper<T>` 已实现能力接口，自定义 Helper 继承即得；订阅时要转型。
> `Connecting` 与 `ValidateConnection` 是**复合**关系：事件先问（可逐次否决），校验器再问（Helper 的规则），任一说不就不连。
> ⚠ **命中还要避开节点框（2026-10-03 起）**：卡片是不透明的，压在它下面的那段线**没画出来**，因此也不应答 —— `LinkHitTestEx.HitTestVisibleLinks` 在返回之前查一次「这点上面盖着哪张卡」。**端口是例外**：离这条线自己端口 `DefaultHitRadius` 以内仍然算命中（端口就画在卡片边缘上，停在端口上就是冲着这条线去的）。这条修的是「在节点上操作，命中的却是它下面那条线」。
> ⚠ **别把命中门改回 `IsRenderReady()`**（要求锚点已测量）。Jalium 按设计从不写 `slot.Anchor`，那样会让它整家连线静默失效 —— 用「曲线有没有被发布」当门。
> ⚠ **Delete 需要一条「焦点路由」，而它和命中是两件事**（2026-10-03 实测踩过，七家里五家缺）。Delete 是键盘事件：它只会沿着**焦点所在的元素**往上冒泡。所以适配器必须①能持有焦点、②在悬停到连线上时**把焦点收到自己身上/那个可视对象上**。只做①不做②的话「悬停（不点）后按 Delete」没有任何路由，而且**不报错**。完整 demo 掩盖了这个缺口 —— 它们有窗口级预览兜底（`MainWindow.OnPreviewWindowKeyDown`），生成出来的工程没有。
> - 各家已落地的形态：WPF/Avalonia「焦点给画线的控件，控件不可聚焦时**退回宿主**」、WinUI/WinForms「焦点给宿主/画布」、Razor「表面根 `tabindex` + 悬停 `FocusAsync(preventScroll: true)`」、MAUI「焦点给交互源」、Jalium「`Focusable` + 悬停收焦点，并用 `RequestBringIntoView` 吃掉那次『把整块画布卷进视口』」。
> - **取焦点一定会带来「平台把画布卷进视口」的连带效应**，各家都要挡：WPF/Avalonia/Jalium 是吃掉 `RequestBringIntoView`，Razor 是 `preventScroll`。
> - ⚠ **`UserControl` / `Control` 的 `Focusable` 默认是 `false`**（WPF、Avalonia 都是）。所以「退回宿主」这条兜底曾经是**空的** —— 焦点落不到宿主上，键也就不来。WPF/Avalonia 的适配器现在在 Attach 里自己开 `control.Focusable = true`，**不要删**。WinUI 没有这个属性（用 `Focus(FocusState)`，宿主本来就能拿焦点）。

> 🧪 **怎么验这一类东西：`Src/Verification/agent-ui-harness.ps1`。** 「悬停一下再按 Delete」用构建验不了、读代码也读不出来 —— 上面那五家的缺口正是这么漏的。这个脚本用真实输入驱动桌面 demo 并截图：
> ```
> powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/agent-ui-harness.ps1 `
>   -Exe <demo.exe> -Actions "drag:687,399,757,433; wait:600; move:724,414; wait:700; probe:724,414; key:Delete; wait:900; probe:724,414"
> ```
> 坐标是**窗口相对**（直接照着截图读），`probe` 报一个像素的颜色，所以可以断言状态而不是靠看。两个坑都踩过且已修：进程必须 **DPI aware**（否则三套坐标互相错位，输入看着像「没到」）；`INPUT` 结构必须是 **40 字节**（并集按最大成员算），小一号时 `SendInput` **静默**返回 0，所有按键凭空消失 —— 脚本现在会报 REJECTED。
>
> 2026-10-03 补的动作（验缩放/按住拖拽要用）：`wheel:<x>,<y>,<delta>`（一次滚轮，120 = 上/放大，配 `keydown:Control` 就是 Ctrl+滚轮缩放）、`keydown:` / `keyup:`（按住不放）、`down:` / `up:`（按下/松开鼠标左键，中间可以 `move`+`shot` 抓**拖拽途中**的样子 —— `drag:` 是一口气做完的，抓不到中态）。
> ⚠ **PowerShell 的成员解析不区分大小写**：C# 里同时有一个 `WHEEL` 常量和一个 `Wheel()` 方法时，`[Ui]::Wheel(...)` 会解析到那个**常量**上并报 `MethodNotFound`（方法明明在）。常量因此一律带 `_FLAG` 后缀（`WHEEL_FLAG` / `KEYUP_FLAG`）。
> ⚠ **`Add-Type` 用的是老编译器（C# 5）**，表达式体成员（`=>` 方法）编译不过 —— 脚本里的 C# 一律写完整方法体。
> ⚠ **`SetCursorPos` 挪动的指针不一定派发 `PointerMoved`**（WinUI 的老毛病，见 §二·4 附的实测）。所以「悬停有没有生效」这类断言会**偶发假阴性**：同一串动作第一次打不中、第二次打中。探到反例时先在同一个点上多打几次再下结论。
>
> **Razor 那一家它够不着，用 `Src/Verification/agent-web-harness.ps1`**（CDP 驱动浏览器）：Blazor 是页，`SendInput` 打进的是浏览器外壳而不是文档，而这一家的 Delete 恰恰由页面自己的 `@onkeydown` 处理。那套的动作里 `eval`（在页面里跑 JS，断言直接是数字）与 `cursor:<js>`（把指针移到**页面自己算出来**的点）比像素探针强得多 —— 打一条画在带变换的 SVG 里、又被卡片压住的曲线时，猜坐标必然打偏。
>
> ⚠ **验证时的进程陷阱**：`pkill -f <项目名>` 只杀 `dotnet run` 的包装进程，**子进程会活着继续占端口** —— 之后每一次「重新构建再测」都是**旧二进制**在回答（我因此白查了好几轮 Razor 语法）。要按进程名杀（或按端口 PID）。
| 给 AI 工具面加工具 | 在 `WorkflowAgentToolkit` 加 `[AgentCommand]` 方法，并用 `WorkflowToolCategory` 分类 | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs`；`WorkflowToolCategory.cs:1` |

> `[SlotSelectors]` **不在** `WorkflowSystem` 命名空间下，它在 `Src/Core/VeloxDev.Core/AI/SlotSelectorsAttribute.cs:38`（`VeloxDev.AI`）。别去 WorkflowSystem 目录里找。
> `AgentContext` / `AgentCommandParameter` 同样在 `Src/Core/VeloxDev.Core/AI/`（`AgentContextAttribute.cs:4`、`AgentCommandParameterAttribute.cs:10`）。它们是 AI 工具面的**注释面**：不加不报错，只是 Agent 读不到你的参数说明。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条，走捷径都能编译通过，有些跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 直接改 `node.Anchor.Horizontal`、或对 `node.Anchor` 返回的对象做原地修改 | getter 返回的是按 `Layout.Scale` 折叠后的**临时对象**，写进去就被丢掉 | 走 `node.Anchor = ...` setter（存世界原值），或调 `MoveCommand`/`SetAnchorCommand` | `StandardEx/WorkflowNodeEx.cs`（`StandardSetAnchor` 上方注释）；`Templates/ViewModels/NodeDefaultViewModel.cs:46` |
| 2 | 自己 `Submit` 包住一个内部已经 `Submit` 的操作（典型：`SetSelector`） | 产生嵌套 undo 项，Ctrl+Z 语义崩坏 | 直接调，让内部提交 | `SelectorEx/SlotEnumerator.cs:287`；AI 工具面 `SetEnumSlotCollection` 的文档明写「Do NOT wrap it in another Submit here」 |
| 3 | 期望 `Move` / `SetAnchor` / `SetSize` 能撤销 | 它们按设计**不入栈** | 需要可撤销就自己提供 `IWorkflowActionPair` 并走 `tree.GetHelper().Submit(...)` | `StandardEx/WorkflowNodeEx.cs:74,93,110` |
| 4 | 依赖 `WorkflowGuard.Fail` 在 Release 里挡错误输入 | `[Conditional("DEBUG")]` 把**整个调用点连同消息参数**编译掉。Release 是彻底空操作 | 在调用方自己判断并处理；把它当 debug 断言，不当运行时防御 | `WorkflowGuard.cs:12-21`；`StandardEx/WorkflowSlotEx.cs:19` |
| 5 | 手动往 `Helper.VisibleItems` 里塞/删元素 | 空间索引与实际可见集脱钩，之后查询与渲染都不一致 | 改 `Viewport`（同步虚拟化）或 `MarkDirty()`（下一 tick 虚拟化 + 广播布局） | `GUI/Virtualization/WorkflowSpatialEx.cs:118`；`Templates/Helpers/TreeHelper.cs:63,332` |
| 6 | 在 `Virtualize` 之外自己写一套「哪些节点可见」的副本 | 副本会漂；`BroadcastVisibleItemLayout` 只对权威 `VisibleItems` 重发 `Anchor`/`Size` | 只用 `VisibleItems` + `VisibleItemAdded/Removed` 事件 | `Templates/Helpers/TreeHelper.cs:86-103` |
| 7 | 在适配器里重新实现坐标换算（`ToWorld`/`ClampScrollOffset`/`SlotAnchorFrom*`） | 七家以前各自内联过，行为各不相同；`WorkflowSurfaceMath` 是收敛后的唯一实现 | 调 `WorkflowSurfaceMath` 的静态方法 | `GUI/Math/WorkflowSurfaceMath.cs:17` |
| 8 | 手写一个 ViewModel 但不在构造函数里调 `InitializeWorkflow()` | Helper 永远不会 `Install`，命令与事件全都不工作，且**不报错** | 构造函数第一行 `InitializeWorkflow();` | `Templates/ViewModels/NodeDefaultViewModel.cs:31` |
| 9 | 在 `InitializeWorkflowCore` 里用 `CreateSlotCommand` 挂初始插槽 | 那时 `node.Parent == null`；`StandardCreateSlot` 的幂等守卫也会把重复派发吞掉，队列里留下幽灵 undo 项 | 按生成器的方式：直接 `field.Parent = this; Slots.Add(field);` | `Writers/WorkflowWriter.cs:1657-1659` 及其上方注释；`StandardEx/WorkflowNodeEx.cs:28` |
| 10 | `[WorkflowBuilder.NodeAttribute]` 的类里手写 `Helper` 属性 / `GetHelper()` / `InitializeWorkflow()` / `SetHelper()` | 生成器会再生成一份，直接编译冲突（这条**会**报错，算运气好的） | 只写业务成员，这些留给生成器 | `Writers/WorkflowWriter.cs:467-527` |
| 11 | `[VeloxProperty]` 的 slot 成员手写一个名字对不上的后备字段 | 生成器按 `属性名 → _camelCase`（属性形式）或**字段原名**（字段形式）引用，名字对不上 → 生成的初始化代码引用到不存在的成员 | 属性形式：`[VeloxProperty] public partial T X { get; set; }` → 生成器认 `_x`；字段形式：`[VeloxProperty] private T x;` | `Base/Analizer.cs:408-417`；`Writers/WorkflowWriter.cs:1555-1560,1582-1590` |
| 12 | 直接改 `CanvasLayout.ActualOffset` / `ActualSize` | 唯一写者是 `Update()`；直接写会被下一次 `Update()` 覆盖，且视图拿到的 `ActualOffset` 与几何脱钩 | 只写 `NegativeOffset` / `PositiveOffset` / `OriginSize` / `Scale` 四者，让 `Update()` 重算 | `GUI/GeometryModels/CanvasLayout.cs:104,123-128` |
| 13 | 顺序颠倒：先读 `ActualOffset` 再写 `Scale`，或忘了 `EnsureNegativeCover` | 折叠负侧内容越出固定负偏移，深缩放（`Scale ≲ 0.4`）下连线**永久截断** —— 这是七家适配器共同的老根因 | 写完 `Scale` → `EnsureNegativeCover(tree)` → 才读 `ActualOffset`/`ActualSize` | `GUI/Math/WorkflowSurfaceMath.cs:438` 及其上方注释 |
| 14 | 用 `SlotDefaultViewModel` 当模板抄默认 `Channel` | 生成器造的 slot 体默认 `MultipleBoth`，`SlotDefaultViewModel` 默认 `OneBoth`。抄错会让「第二根连线自动删掉第一根」 | 按需求显式设 `Channel` | `Writers/WorkflowWriter.cs:1043`；`Templates/ViewModels/SlotDefaultViewModel.cs:36` |
| 15 | 新写一个 slot 实现，anchor 字段用 `new()` 而不是 `new(double.NaN, double.NaN, 0)` | `Anchor` 类本身默认 `0d`，但**只有 slot 约定用 NaN 表示「未测量」**；用 0 会让连线在测量落地前先在原点画一帧再跳走（闪烁），且 node 的 `[DefaultAnchor]` 默认位置会被误判为已就绪 | slot 的 anchor 字段默认 NaN。参考 `Writers/WorkflowWriter.cs:1045-1046`（生成器版）与 `Templates/ViewModels/SlotDefaultViewModel.cs:38`（默认实现版）；node 的默认值是 0 或 `[DefaultAnchor]`（`Writers/WorkflowWriter.cs:779`），**不要照抄到 slot 上** | `GUI/GeometryModels/Anchor.cs:11`；`GUI/Rendering/WorkflowSlotUpdateGate.cs:7-8` |
| 16 | 手写 `Targets`/`Sources` 来建连接 | 会漏掉 `LinksMap`、两端 slot 的状态位、以及 undo 项 | 只用两阶段 `SendConnection` / `ReceiveConnection`（**没有**公开的「连这两个节点」API） | `StandardEx/WorkflowTreeEx.cs:105,139`；`Templates/Helpers/TreeHelper.cs:302,306` |
| 17 | 用值相等比较节点 / 用 `IWorkflowNodeViewModel` 当字典键但没给比较器 | 节点/插槽是引用身份；`GroupData` 的 Key 就是节点引用 | 用 `WorkflowReferenceEqualityComparer<T>.Instance` | `GUI/Virtualization/WorkflowSpatialEx.cs:338`；`CompilerEx/Runtime/Model/RuntimeContext.cs:58` |
| 18 | 深拷贝 `CanvasLayout` 以外的几何记录后当状态保存 | `Anchor`/`Size` 的折叠临时对象带 `_owner`/`_collapseScale`；序列化靠它们回写原值 | 需要克隆走 `Clone()`；需要保存走序列化钩子 | `GUI/GeometryModels/Anchor.cs:32-34,64-104` |
| 19 | 在 `ReceiveAsync` 里想读写「共享变量」，却发现 `context` 上没有 `Set`/`TryGet` | `ReceiveAsync` 收的是 `ITaskContext`，它**只有**继承来的 `Data`/`Sender`/`Receiver`/`IsCompilePhase`（`Interfaces/WorkflowSystem/ITaskContext.cs:14`、`IContext.cs:12`）。`Set`/`TryGet` 在 `IRuntimeContext` 上（`CompilerEx/Runtime/Model/RuntimeContext.cs:345,352`） | 实现 `IRuntimeAware` 拿 `IRuntimeContext`（`CompilerEx/Runtime/Contracts/IRuntimeAware.cs`），或把状态放在 `Data` 里往下传 | 同上 |
| 19b | 在 `ReceiveAsync` 里改共享状态，并指望它只属于自己这条分支 | 扇出分支**现在是并发交错的**（2026-09-27 起，`RunParallelAsync` 用 `Task.WhenAll`）：`Data`、重定向旗标、日志由每分支的 `BranchRuntimeContext` 隔离，但**黑板 `Set`/`TryGet` 仍然共享** —— 按契约它就是全跑共享的，两个分支写同一个键是**竞态** | 分支私有的东西放 `Data` 或自己的字段；`Set`/`TryGet` 只放「整个运行共享」的状态 | `CompilerEx/Runtime/Model/BranchRuntimeContext.cs`；[compiler-execution.md](compiler-execution.md) §一/§三 |
| 20 | 在适配器里 `new` 一个自己的 Tree/Node | 视图必须绑定到**已经 `InitializeWorkflow()` 过**的组件；适配器只负责视图池化与几何 | 组件由用户 ViewModel 层提供，适配器只消费 | `Templates/ViewModels/*.cs` 的构造即 `InitializeWorkflow()` |
| 21 | `[SlotSelectors]` 标了却还想让 Agent 用 `PatchNodeProperties` 改它 | 工具面**主动拒绝**并指向专用工具 | 用 `SetEnumSlotCollection` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/ComponentPatcher.cs:127-134` |
| 22 | 连线视图在悬停时取键盘焦点（为了让 Delete 生效），却不拦平台随之而来的「把焦点元素滚进视口」 | 连线视图的框往往是**整块画布大小** ⇒ 焦点一落上去，滚动容器就把画布跳一段。三家机制不同：Avalonia 是 `ScrollViewer.BringIntoViewOnFocusChange`（默认 true）、WPF 是 `RequestBringIntoView`、Jalium 是平台的安全区/软键盘事件分支延迟发的 `BringIntoView`（**极小概率**：需「表面持有焦点 + 该事件 + 其后一次布局」同时成立） | 在**该视图自己**身上吃掉这条请求（`AddHandler(RequestBringIntoViewEvent, …, e => e.Handled = true)`；Jalium 那种要按 `TargetObject == this` 收窄）。**不要**关掉整块画布的自动滚进视口 —— 节点卡里输入框的同类请求仍该生效 | 七家非 Trimmed demo 的实测见各自 `adapters/<平台>.md` §五；Avalonia 的因果 A/B 与 Jalium 的 IL 级机制链都记在那里 |
| 23 | 在节点里 `Warn(...)` 一句，以为它像 `Error(...)` 一样会停住整轮（或反过来：`Error(...)` 一句以为只是记个日志） | 两档不同（2026-09-27 定）：`Warn` **完全不影响控制流**（值照传、连 `IRedirectable` 都不问）；`Error` 与未捕获的异常是**停止** —— 没人能处理就结束整轮（`CurrentOrder = -1`、`EndedWithError`）。而全仓生产代码没有一个 `IRedirectable` 实现者，所以「有没有人处理」的答案目前恒为「没有」 | 只是想说句话 → `Log`；想记一笔让宿主看得到但不打断 → `Warn`/`WarnAsync`；这一步失败、整轮该停 → `Error`/`ErrorAsync`；想改路线而不是停 → 实现 `IRedirectable` | `CompilerEx/Runtime/RuntimeEngine.cs` 的 `RunExecuteAsync` / `DriveAsync`；[compiler-execution.md](compiler-execution.md) §六 |
| 24 | 写自研节点时不实现 `ICompileTimeAware` / `IRuntimeAware` | 三个契约**都不在** `IWorkflowNodeViewModel` 的继承链上，Core 的 `NodeDefaultViewModel` 也不实现、生成器也不补 ⇒ **静默降级**：`Order` 恒 `-1`、多输入汇合点拿不到 `GroupData`、重定向重跑里不会被驱动。只有 demo 的节点实现了它们 | 需要编译身份/汇合聚合/重定向就显式实现；照着 demo 的节点写 | `Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:10`；[compiler-execution.md](compiler-execution.md) §四 |
| 25 | 自己写一个 `IRuntimeContext` 实现（或从别处拿一个），指望暂停 / 观察 / 重试 / 错误 sink / 补偿照旧生效 | 那五个是 `RuntimeContext` 的**具体类成员**，引擎按 `context as RuntimeContext`（扇出内透过 `BranchRuntimeContext.Session`）取 —— 换掉实现就**静默**一个也拿不到。与 `MaxParallelBranches`、`LogWriter` 同样的取舍（给 `IRuntimeContext` 加成员会破坏每个外部实现） | 要用这几条就 `new RuntimeContext(...)` 并把对象插上去；自定义实现只在确实不需要它们时用 | `CompilerEx/Runtime/RuntimeEngine.cs` 的 `Session(IRuntimeContext)`；`CompilerEx/Runtime/Model/RuntimeContext.cs` 的「可选能力」一节 |

---

## 三、步骤清单

### 3.1 加一个新节点类型（最常见的路径）

生成器会补全命令、`Helper` 属性、生命周期三件套，**你只写业务**。

1. **建文件**：任意 `.cs`，类必须 `partial`，加特性：
   ```csharp
   [WorkflowBuilder.NodeAttribute<MyNodeHelper>(workSemaphore: 1)]
   public partial class MyNodeViewModel { ... }
   ```
   `THelper` 约束是 `where T : IWorkflowNodeViewModelHelper, new()`（`Templates/WorkflowBuilder.cs:29`）—— **必须有公开无参构造**。
2. **槽位声明**（`[VeloxProperty] public partial TSlot X { get; set; }` 或 `[VeloxProperty] public partial SlotEnumerator<TSlot> X`）—— 类型决定生成器怎么挂它（`Writers/WorkflowWriter.cs:1564-1619`）。
3. **可选**：`[DefaultAnchor(...)]` / `[DefaultSize(...)]`，会被烘进后备字段初始化器（`Writers/WorkflowWriter.cs:1360,1387`）。写成子类、由基类带 `[WorkflowBuilder.NodeAttribute]` 时，默认值走 `InitializeWorkflowCore` 覆写（`:1390`）——**基类必须有公开无参构造**。
4. **写 Helper**：`partial class MyNodeHelper : NodeHelper<MyNodeViewModel>`（或 `NodeHelper`），重写 `ReceiveAsync`（返回 `null` 就是静默断链）与 `AccessAsync`（默认 `true`）。父类在 `Templates/Helpers/NodeHelper.cs:19`。
5. **给 AI 面加注释**（可选但强烈建议）：类与方法加 `[AgentContext(...)]`、命令参数加 `[AgentCommandParameter(typeof(T))]`。
6. **验证**：`Move`/`SetAnchor`/`SetSize` 不撤销是正常的；`CreateSlotCommand`/`DeleteCommand` 撤销正常才算挂对。

**联动清单**：见 §四。

### 3.2 加一个新插槽类型

1. `[WorkflowBuilder.SlotAttribute<MySlotHelper>] partial class MySlotViewModel`（`Templates/WorkflowBuilder.cs:40`）。
2. Helper 继承 `SlotHelper<MySlotViewModel>`（`Templates/Helpers/SlotHelper.cs:10`），一般只要重写 `Install`/`Uninstall` 做订阅。
3. 显式设默认 `Channel` —— 生成器默认 `MultipleBoth`（`Writers/WorkflowWriter.cs:1043`）。
4. 若要让节点默认带这种 slot，改节点的 `CreateWorkflowSlot<T>()` 覆写（生成器版在 `Writers/WorkflowWriter.cs:906-918`，回退到 `SlotDefaultViewModel`）。

### 3.3 加一个条件插槽选择器（端口数随数据变）

1. 写 Provider：`ISlotProvider` 返回 `SlotDefinition(value, label)`（`Interfaces/WorkflowSystem/ISlotProvider.cs`、`SelectorEx/SlotDefinition.cs:6`），或直接用一个 `enum`/`bool` 的 `Type` / 类型全名字符串。
2. 在节点上声明：
   ```csharp
   [VeloxProperty]
   [SlotSelectors(typeof(MyEnum))]
   public partial SlotEnumerator<SlotViewModel> InputSlots { get; set; }
   ```
3. **不要**手写后备字段以外的初始化：`InitializeWorkflow` 会 `field.Install(this, "InputSlots")`（`Writers/WorkflowWriter.cs:1650-1651`）。
4. `SetSelector` 三种入参形态：`Type`（enum/bool）、全名 `string`、`ISlotProvider`（`SelectorEx/SlotEnumerator.cs:287`）。
5. **撤销语义**：`SetSelector` 自提交一对 undo；选择器类型内部再改值属于 live state，不是时间线点。
6. **已知延迟**：`[SlotSelectors]` 属性的 slot-anchor 通知**故意晚一帧**，等容器生成完再发。
7. **枚举成员一律走 `SlotEnumerator.EnumMembersOf`，不要改回 `Enum.GetValues(Type)`**。后者带 `RequiresDynamicCode`：AOT 下 `SetSelector` 直接抛 `NotSupportedException: '…[]' is missing native code or metadata` —— **有 slot selector 的工作流树在 NativeAOT 下根本建不起来**（实测：还原成旧代码后同一个探针必抛）。`GetValuesAsUnderlyingType` 要 .NET 8、`Enum.GetValues<T>()` 要 .NET 5，而本程序集含 `netstandard2.0`，所以实现走 `GetNames` + `Parse` —— 两者都无标注且全档可用，顺序也与 `GetValues` 一致（都由 `SlotEnumeratorTests.SlotsComeOutInTheOrderEnumGetValuesProduces` 钉住，含用 `|` 拼出来的 flags 枚举）。
8. **`SetSelector` 的 `string` 重载仍然不安全**：它走 `Type.GetType(全名)`（`SelectorEx/SlotEnumerator.cs:325`），是 IL2057 —— 裁剪器无法保证那个类型还在。要 AOT 就用 `Type` 或 `ISlotProvider` 形态，别传字符串。

### 3.4 加一个自定义 Link 类型

- **全局替换**（所有连接都用它）：重写 `TreeHelper<T>.CreateLink(sender, receiver)`（`Templates/Helpers/TreeHelper.cs:171`）。
- **按 Tree 声明**：`[WorkflowBuilder.TreeAttribute<THelper>(virtualLinkType: typeof(MyLink), virtualSlotType: typeof(MySlot))]` —— 这两个参数同时决定 `VirtualLink` 双端插槽的类型（`Templates/WorkflowBuilder.cs:13`）。
- 新 Link 的视图首行必须过门：`if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;`（`GUI/Rendering/WorkflowLinkRenderEx.cs:27`）。

### 3.5 加一个自定义 Tree

`[WorkflowBuilder.TreeAttribute<MyTreeHelper>(...)]`，Helper 继承 `TreeHelper<MyTreeViewModel>`。**注意选哪个构造**：

```csharp
public TreeHelper()                { useVirtualization = false; }   // 不开虚拟化，也不起 10fps tick
public TreeHelper(double cellSize) { useVirtualization = true;  }   // 开，且启动 Tickable "TreeHelper"
```
依据：`Templates/Helpers/TreeHelper.cs:38-52`。自定义 Helper 若要虚拟化，**必须**自己选 `useVirtualization = true` 并调 `tree.EnableMap(CellSize, VisibleItems)`（参考 `:109`），否则 `Virtualize` 抛 `ArgumentNullException`（`GUI/Virtualization/WorkflowSpatialEx.cs:127`）。

### 3.6 加一个分支路由节点

1. 实现 `ICompileTimeRouter`（`CompilerEx/Compile/Contracts/ICompileTimeRouter.cs`）：
   - `GetRouteTable()` → `key → 下游节点列表`；
   - `ResolveRouteKey(payload)` → 运行期给的 key。
2. **静态 vs 动态不是靠 `RouterCompileMode` 字段决定的** —— 编译器只看 `ResolveRouteKey(null)` 是否返回 `null`（`CompilerEx/Compile/CompilerViewModel.cs:94-95`）。想静态就在 `payload is null` 时返回当前选中的 key；想动态就在 `payload is null` 时返回 `null`。
   `RouterCompileMode` 枚举（`CompilerEx/Compile/Contracts/RouterCompileMode.cs:15`）是给你自己的节点/Agent 面做标签用的，**Core 编译器从不读它**。
3. **同一个 key 指到多个下游 = fan-out**，会被编成 `ParallelSegment`（`CompilerViewModel.cs:132-151`）。
4. **同一个 router 的同一个 route key 若有多条路径到达同一节点，编译报错**。
5. Terminal 角色下路由表会被裁到锥内（`CompilerViewModel.cs:93,303`）。

### 3.7 加一个可重定向节点

实现 `IRedirectable`，在 `ResolveRedirectAsync` 里返回**更早的** `CompileContext.Order`。
- 引擎限制：只能往更早退（`RuntimeEngine.cs` 的 `RunExecuteAsync` 只接受 `targetOrder < order`）；`Order == -1` 是绝对停止；单次运行最多 `MaxRedirects = 50`（`:24`）。
- 被跳过的节点再次执行时，只有 `Attempt == 当前 Attempt` 或 `Order < ActiveRedirectTarget` 的输出才被采信（`CompilerEx/Runtime/Model/RuntimeContext.cs:404-406`）。**这是「前缀保留」契约**：终止节点之前的产物可以直接用，之后的一律重算。
- **`Error` 与未捕获的异常会来问你**（`Warn` 不会 —— 那只是一行日志，运行照常走）：引擎调 `ResolveRedirectAsync` 要目标（`RuntimeContext.cs` 的 `Error`；抛异常那支在 `RuntimeEngine.DriveAsync` 的 catch，重试耗尽后）。返回 `null` = 不重定向，于是整轮结束 —— 这是**正常返回**，是契约说「我也没办法」的方式，不是错误。

### 3.8 加一个网格装饰器 / 小地图

实现 `IWorkflowGridDecorator`（`ScrollOffsetX/Y`、`ContentOffsetX/Y`、只读 `RulerBand`）或 `IWorkflowMinimapOverlay`（额外 `ViewportWidth/Height`、`WorkflowTree`、`IsMinimapVisible`）。
- `WorkflowSurfaceMath` 里已备好小地图数学：`MinimapFit` / `MinimapLocal` / `MinimapViewportRect` / `MinimapToWorld` / `MinimapToScroll`（`GUI/Math/WorkflowSurfaceMath.cs:258-324`）—— 别自己推。
- `RulerBand` 要转发给 `WorkflowSpatialEx.SetVirtualizeInset`，否则有标尺的那条边上的节点看不见。

### 3.9 加一家新平台适配器

**本文只写契约与注册位置；具体到每家的做法、平台硬限制、刻意背离，写在 `adapters/<平台>.md`，以 `skills/veloxdev-create-workflow/references/new-adapter.md` 为准。**

**契约（新增一家必须遵守的共性）：**

1. **七个视图角色**：画布宿主、画布变换、视图池、节点拖拽、插槽连接、插槽布局、网格装饰器/小地图。
   逐角色职责表与既有各家的对应类，见 `skills/veloxdev-create-workflow/references/view-layer.md`（七角色表）。
2. **绑定挂在 `DataTemplate` 根上**，附着属性名与 `PART_*` 命名约定见同一份 `view-layer.md`。
3. **`Viewport` 是画布局部坐标，只有适配器写它**；写完同步触发虚拟化（`Templates/Helpers/TreeHelper.cs:106`）。
4. **连线视图首行必须过渲染就绪门**：`if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;`（`GUI/Rendering/WorkflowLinkRenderEx.cs:27`）。NaN 锚点 = 未测量，这是**跨平台统一**的机制，不需要各家自己发明时序。
5. **插槽锚点写入用 Core 提供的三个函数之一**（按你测量到的坐标系选）：`SlotAnchorFromVisualCenter` / `SlotAnchorFromNode` / `SlotAnchorFromCanvasLocal`（`GUI/Math/WorkflowSurfaceMath.cs:230,237,248`）。
6. **滚轮方向统一：上滚 = 放大**，放大即 `Scale *= 1/1.1`（`Scale` 是折叠因子）；`Scale` 夹在 `[0.1, 10]`。七家一致：`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:531-532`，其余六家同形。**写反成 `delta > 0 ? 1.1 : 1/1.1` 是七家曾经一起犯过的错**。
7. **一次缩放的提交顺序**：枢轴 → `Scale` → `EnsureNegativeCover` → 重布局 → `PivotCenterScroll` → `ClampScrollOffset`（`GUI/Math/WorkflowSurfaceMath.cs:438` 的时序约束）。
8. **既有默认实现可以直接复用**：`TreeDefaultViewModel` / `NodeDefaultViewModel` / `SlotDefaultViewModel` / `LinkDefaultViewModel`（`Templates/ViewModels/`）。一家适配器**不需要**定义新的 ViewModel 类型。
9. **适配器不引用其它适配器**，也没有共享适配器基类 —— 七家各写各的（`Src/Adapters/VeloxDev.*/Attached/Workflow/`）。能从自家平台 API 拿到的东西不要去 Core 里加开关。
10. **视口位置必须成对：滚动时写回，挂树时恢复。** 写回用 `WorkflowSurfaceMath.ViewportOffsetFromScroll`（世界坐标），恢复用 `ViewportRestoreScroll` + `ClampValue`，各自的逆函数就是对方。**缺一半都算没实现**：只写不恢复 = 存了白存（宿主加载回来停在原点）；只恢复不写 = 存下来的是空。判据用 `HasViewportRestore`（默认 (0,0) 与亚像素不算）。
    **时序是硬要求**：适配器自己第一次 `Refresh`/`UpdateVisibleRegion` 会用控件当前（还没滚过去的）位置**覆盖** `ViewportOffset`，所以捕获必须在它之前。参考实现见七家各自的 `CaptureViewportRestore` + 一个延后队列（`Dispatcher`/`DispatcherQueue`/`MainThread`/`BeginInvoke`/`OnAfterRenderAsync` 各按平台）；每次换树只恢复一次，用树引用去重（`LastRestoreTree`）。
    **适配器不做「没存档就居中」** —— 那是宿主的 UX 决定。

**注册在哪：**

| 要注册的东西 | 位置 |
|---|---|
| 适配器项目本身 | `VeloxDev.slnx`，以及 `Src/Adapters/VeloxDev.<平台>/` |
| 七家对齐的参考实现 | `Src/Adapters/VeloxDev.{WPF,Avalonia,WinUI,MAUI,WinForms,Razor,Jalium}/Attached/Workflow/` |
| `dotnet new` 模板包 | `Src/Templates/VeloxDev.<平台>.Templates/working/content/` 下 7 个模板：`workflow-tree-view`、`workflow-node-view`、`workflow-slot-view`、`workflow-link-view`、`workflow-grid-decorator`、`workflow-minimap-overlay`、`workflow-template-selector`（各含 `.template.config/template.json`） |
| 可运行 demo | `Examples/Workflow/<平台>/` 与 `Examples/Workflow/<平台> Trimmed/`（共 14 个），共享 `Examples/Workflow/Common/Lib/` |
| Skill 里的平台页 | `skills/veloxdev-create-workflow/references/gui/<平台>.md`（文件名必须与 `adapters/<平台>.md` 对齐） |

> **注意**：本表只列**本仓库自己的**注册位置。`Docs/` 那套文档站点**不在本仓库里**（`.gitignore:461` 排除，`git ls-files Docs/` 为空），它与本仓库的适配器各自独立、不随适配器一起记账。

**模板包的既有约定**（新一家要照抄）：`identity` = `VeloxDev.<平台>.<模板名>`，`shortName` = `<平台小写>-v-<后缀>`（如 `wpf-v-decorator`），`sourceName` = `TemplateClass`，`primaryOutputs` 指向被替换的源文件。样例：`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-grid-decorator/.template.config/template.json`。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一种**节点/插槽/连线类型**（用户扩展路径）

生成器会自己发现 —— 只要类上挂了四个 `[WorkflowBuilder.*]` 之一，它就在生成范围内。**你不需要注册任何东西**。但是：

- [ ] 类的 `THelper` 必须满足 `where T : IXxxHelper, new()` —— 有公开无参构造。
- [ ] 类必须 `partial`，且构造函数里调 `InitializeWorkflow()`（用生成器时由生成器给，手写时必须自己写）。
- [ ] `[VeloxProperty]` 的 slot 成员命名必须让生成器能推出后备字段（§二 #11）。
- [ ] 要进 AI 工具面：加 `[AgentContext]` / `[AgentCommandParameter]`（`Src/Core/VeloxDev.Core/AI/`）。
- [ ] 要进 demo：`Examples/Workflow/Common/Lib/` 加 ViewModel，各平台 demo 加对应视图 + `dotnet new` 模板。

### 4.2 加**一种新的核心组件种类**（几乎不该做；要做就是大改）

这类改动**必须在下面每一处同步**，否则静默少能力：

- [ ] 新接口 + Helper 接口：`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/`
- [ ] 默认实现：`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/ViewModels/` 与 `Templates/Helpers/`
- [ ] `StandardEx/` 的扩展方法文件
- [ ] 新特性：`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/WorkflowBuilder.cs`
- [ ] **生成器的触发列表**：`Src/Generators/VeloxDev.Core.Generator/Base/Analizer.cs:95-106` 的 `TriggerAttributes`（10 条）—— 加新特性要加一条，否则**整个生成器对新类型完全无感，且不报错**
- [ ] **生成器的三个 `switch`**：`Writers/WorkflowWriter.cs:347`（`GetWorkflowInterfaceName`）、`:359`（`GetWorkflowHelperInterfaceName`）、`:395-406`（`GenerateBody` 的 `case 1..4`）—— 目前的 `int WorkflowType` 编码是 1=Tree/2=Node/3=Slot/4=Link，硬编码在 `Analizer` 的特性名匹配里
- [ ] `Writers/WorkflowWriter.cs:121` 的 `IsWorkflowViewModelAttribute`（按字符串 `Contains("WorkflowBuilder.XxxAttribute")` 匹配）
- [ ] `WorkflowWriter.cs:98` 的 `CheckBaseClassForWorkflowInfrastructure` / `:1332` 的 `HasIdentifiableInfrastructure`（决定子类是否只生成 `EnsureWorkflowHelper` 覆写）
- [ ] 七家适配器的 `Attached/Workflow/`（每家的 `ViewManager` / `ViewPool` / 各 Behavior）
- [ ] `Src/Templates/*` 七套模板包
- [ ] `memory/modules/WorkflowSystem/adapters/*.md` 七份

### 4.3 加**一家新平台适配器**

- [ ] `Src/Adapters/VeloxDev.<平台>/Attached/Workflow/`（七个角色对应的类）
- [ ] `VeloxDev.slnx` 注册项目
- [ ] `Src/Templates/VeloxDev.<平台>.Templates/working/content/` 下 **7 个**模板（缺文件 ≠ 无差异）
- [ ] `Examples/Workflow/<平台>/` + `Examples/Workflow/<平台> Trimmed/`
- [ ] `skills/veloxdev-create-workflow/references/gui/<平台>.md`
- [ ] `memory/modules/WorkflowSystem/adapters/<平台>.md`（文件名与上一行对齐）
- [ ] **不要**因此改 Core —— 契约在 Core 里已经收齐（`WorkflowSurfaceMath`、`IWorkflowGridDecorator`、`IWorkflowMinimapOverlay`、`WorkflowSlotUpdateGate`）。平台差异在适配器里消化。

### 4.4 加一个**编译/运行期语义**

- [ ] `ICompileTimeRouter` / `ICompileTimeAware` / `IRedirectable` / `IRuntimeAware` 之一（`CompilerEx/` 的 Contracts 目录）
- [ ] 对应 Segment 类型：`CompilerEx/Compile/Model/` 的 6 个段模型
- [ ] **`RuntimeEngine.RunGraphAsync` 的段分派**：`CompilerEx/Runtime/RuntimeEngine.cs:131-148`（`RunExecuteAsync` / `RunBranchAsync` / `RunParallelAsync`）—— 加段类型必须加分支
- [ ] **`Context` 契约**：`CompilerEx/Runtime/Contracts/IRuntimeContext.cs` 与 `CompilerEx/Compile/Contracts/ICompileContext.cs` 加成员会影响所有实现
- [ ] `VeloxDev.Core.Test/WorkflowSystem/CompilerEx/` 的探针节点（`ProbeNodes.cs`）—— 这是唯一覆盖编译/运行路径的测试面
- [ ] AI 工具面的 `CompileNodeResult` / `BuildCompileOrders`（`WorkflowAgentToolkit.cs`）—— 它读 `ICompileTimeAware.CompileContext` 并据此判 `isStopped`

### 4.5 加一个 **AI 工具**

- [ ] `WorkflowAgentToolkit` 加 `[AgentCommand]` 方法
- [ ] `WorkflowToolCategory` 加/选分类（`.../Functions/WorkflowToolCategory.cs`）
- [ ] `AgentContextCollector` 若要暴露新的元数据
- [ ] `WorkflowAgentContextProvider` 是**唯一工具来源**（`.../WorkflowAgentContextProvider.cs`），按 `_scope.ContextKey`（= `Version` + 预算用量档）缓存渲染

---

## 五、几个「以为能改、其实不该改」的地方

1. **`WorkflowSlotUpdateGate` 的 NaN 判定不要改成本地化条件**（比如按平台加开关）。它是跨七家统一的、也是唯一让渲染时序不需要各家自己发明的机制（`GUI/Rendering/WorkflowSlotUpdateGate.cs:1-16`）。
2. **`ClampScrollOffset` 的正负边缘不对称是刻意的**，不是 bug；负边缘返回「增长后的量」是让调用方的正向滚动抵消这次平移的约定（`GUI/Math/WorkflowSurfaceMath.cs:116` 及 `:156` 的 `OvershootGrowth`）。
3. **`CanvasLayout.OnCollapsePivotChanged` 与 `OnZoomCenterChanged` 是故意空的**（`GUI/GeometryModels/CanvasLayout.cs:132-133`）：`CollapsePivot` 由适配器在同一次缩放手势里紧挨着 `Scale` 写入，重算会打断这个序列。
4. **`RouterCompileMode` 不是开关**（见 §3.6）。
