# WorkflowSystem 架构

> 模块位置：`Src/Core/VeloxDev.Core/WorkflowSystem/`
> 对外接口：`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/`（17 个文件，命名空间 `VeloxDev.WorkflowSystem`）
> 平台差异见同目录 `adapters/<平台>.md`；扩展做法见 `extension.md`。

---

## 一、这个模块是什么、不是什么

**是什么**：节点图编辑器的「模型树 + 编译运行 + 画布几何/虚拟化」三层，**零 UI 依赖** ——
`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`，整个项目运行期只引了一个 `Microsoft.Bcl.HashCode`（另有生成器包与项目引用，见 §五）。

**不解决**（这几条决定了你找不到代码时该往哪看）：

| 你以为在这里 | 其实在哪 |
|---|---|
| 怎么画节点/连线 | 七家适配器 `Src/Adapters/VeloxDev.*/Attached/Workflow/`，Core 只给坐标数学 |
| 鼠标命中、拖拽、滚轮 | 适配器的七个角色行为（`WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSurfaceBehavior` 等；**Jalium 这几个角色现在在适配器的 `WorkflowTreeView` 等可继承基类里，见 `adapters/jalium.md`**） |
| 持久化格式 | **2026-10-03 起由生成代码定义**（`VeloxDev.Serialization`，入口 `ComponentModelEx`，见 [`VeloxDev.Core.Extension/architecture.md`](../VeloxDev.Core.Extension/architecture.md) §八）。本模块只保留**序列化钩子**，且已从 Newtonsoft 特性改为接口：`Anchor` / `Size` 的 `IVeloxJsonSerializing` / `Serialized` / `Deserialized`（`GUI/GeometryModels/Anchor.cs`、`Size.cs`），`SlotEnumerator` 的 `IVeloxJsonDeserializing` / `Deserialized`（`SelectorEx/SlotEnumerator.cs`），`BranchOption` / `BranchSegment` 的 `IVeloxJsonDeserialized`（`CompilerEx/Compile/Model/`） |
| 撤销栈的存储 | 栈是 `TreeHelper<T>` 的私有字段；Core 只定义「一对 Redo/Undo 委托」`WorkflowActionPair.cs:6` |
| 节点「算什么」 | 用户实现 `IWorkflowNodeViewModelHelper.ReceiveAsync`（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:123`）。默认实现返回 `null`，**什么都不往下传**（`Templates/Helpers/NodeHelper.cs:69`） |

**AI 工具面不在本模块内**：`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/`（命名空间 `VeloxDev.AI.Workflow`）是本模块的**消费方**，不是组成部分；它引用 `VeloxDev.Core`（`Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj` 的 ProjectReference/PackageReference 对）。改 Core 的契约会连带打断它，但不要把它的逻辑写进 Core。

---

## 二、四组件 + Helper：状态归谁

四个组件接口都继承 `IWorkflowViewModel`（`Interfaces/WorkflowSystem/IWorkflowViewModel.cs:10`），四个 Helper 接口都继承 `IWorkflowHelper`（`Interfaces/WorkflowSystem/IWorkflowHelper.cs:4`，只有 `Closing/CloseAsync/Closed` 三个钩子）。

**分界线是固定的：ViewModel 存状态，Helper 存行为与订阅。**

| 组件 | ViewModel 拥有（可序列化的状态） | Helper 拥有（运行时状态） | 依据 |
|---|---|---|---|
| Tree | `Layout`、`Nodes`、`Links`、`LinksMap`、`VirtualLink` | 撤销/重做栈（`TreeCache`）、`VisibleItems`、`Viewport`、`CurrentSender`、`SpatialManager` | `Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:15,20,25,30,35`；`StandardEx/WorkflowTreeEx.cs:11,682` |
| Node | `Parent`、`Anchor`、`Size`、`Slots` | `_scaleTracker`、子 Helper 的并发信号量 | `Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:15,20,25,30`；`Templates/ViewModels/NodeDefaultViewModel.cs:37` |
| Slot | `Parent`、`Channel`、`State`、`Anchor`、`Targets`、`Sources` | 无独立运行时状态（`SlotHelper<T>` 只有事件） | `Interfaces/WorkflowSystem/IWorkflowSlotViewModel.cs:15,20,25,30,35,40`；`Templates/Helpers/SlotHelper.cs:19` |
| Link | `Sender`、`Receiver`、`IsVisible` | 无 | `Interfaces/WorkflowSystem/IWorkflowLinkViewModel.cs:14,19,24`；`Templates/Helpers/LinkHelper.cs:18` |

**Helper 是「按实例挂」的，不是全局单例**。`EnsureWorkflowHelper()` 里 `if (Helper is not <具体类型>) Helper = new <具体类型>()` 是生成器写的（`Src/Generators/VeloxDev.Core.Generator/Writers/WorkflowWriter.cs:481`），所以**手写 ViewModel 必须自己在构造函数里调 `InitializeWorkflow()`**，见 `Templates/ViewModels/NodeDefaultViewModel.cs:31`。

### 唯一写者（single writer）

这几处只能有一个写者，绕过它就是 bug：

| 状态 | 唯一写者 | 说明 |
|---|---|---|
| `CanvasLayout.ActualOffset` / `ActualSize` | `CanvasLayout.Update()`（`GUI/GeometryModels/CanvasLayout.cs:104`） | 由 `OriginSize` / `PositiveOffset` / `NegativeOffset` / `Scale` 的 partial 变更钩子驱动（`:123-128`）。适配器只能写那四个，**不能直接写 Actual\*** |
| `TreeHelper.Viewport` | 适配器（`TreeHelper<T>.OnViewportChanged` 同步虚拟化，`Templates/Helpers/TreeHelper.cs:106`） | 坐标系是**画布局部** |
| `CanvasLayout.ViewportOffset` | 适配器，两个方向各一处：滚动时写回、挂树时读出来恢复 | 坐标系是**世界**（与 `ActualOffset` 差一个平移，见 `WorkflowSurfaceMath.ViewportOffsetFromScroll` / `ViewportRestoreScroll`）。成对才有意义，见 [extension.md](extension.md) §3.9-10 |
| `TreeHelper.VisibleItems` | `WorkflowSpatialEx.VirtualizeCore`（`GUI/Virtualization/WorkflowSpatialEx.cs:118`） | 任何地方直接改它都会让空间索引与可见集脱钩 |
| `slot.Anchor` | 适配器的 slot-layout 行为，在 render 优先级下异步测量后写入 | 见 `GUI/Rendering/WorkflowSlotUpdateGate.cs:1-16` 的长注释 |
| `VirtualLink` | `StandardSendConnection` / `StandardResetVirtualLink`（`StandardEx/WorkflowTreeEx.cs:105,196`） | `VirtualLink.Sender/Receiver` 的 Anchor 被直接赋值，不走 setter |

### 一个反直觉点：`Anchor` / `Size` 的 getter 返回的是「折叠后的临时对象」

`NodeDefaultViewModel.Anchor` 的 getter 是 `anchor.Collapse(Parent?.Layout?.Scale)`（`Templates/ViewModels/NodeDefaultViewModel.cs:46`），字段里存的是**世界坐标原值**。源码注释写得很明确：

> `Src/Core/VeloxDev.Core/WorkflowSystem/StandardEx/WorkflowNodeEx.cs`（`StandardSetAnchor` 上方）——「The Anchor/Size getters collapse toward the world origin by Layout.Scale (value / scale), so in-place mutation of the returned value would write to a discarded copy. All mutations must go through the property setters, which store the ORIGINAL world value.」

所以 `node.Anchor.Horizontal = 5;` 或者 `node.Anchor + someOffset` 里对 `node.Anchor` 的属性赋值**全部丢失**。`StandardMove` 也是按这个语义写的：`world' = (collapsed + offset) * scale`（`StandardEx/WorkflowNodeEx.cs:110`）。

---

## 三、五条管线

### 3.1 模型树 / 结构变更（含撤销）

```
调用方 → 组件命令（IVeloxCommand）→ 生成的命令体 → Helper.Xxx(...) → StandardEx.StandardXxx(...)
                                                                        ↓
                                                          TreeHelper.Submit(WorkflowActionPair)   ← 唯一入栈口
```

- 命令是生成器按约定成员名生成的（`NodeDefaultViewModel.cs:88-143` 是手写版，生成版见 `Writers/WorkflowWriter.cs`）。命令体的形状是 `if (parameter is not X) return; Helper.Y(...)`。
- `IWorkflowActionPair` 只是 `(Action Redo, Action Undo)`（`WorkflowActionPair.cs:6`）。
- 栈在 `TreeCache` 里（`ConditionalWeakTable<IWorkflowTreeViewModel, TreeCache>`，`StandardEx/WorkflowTreeEx.cs:11,682`），**按 Tree 实例隔离**。

### 3.2 连接建立（两阶段协议）

```
SendConnection(senderSlot) → 校验 sender 方向 → 按 sender 的 channel 清理旧连接
                           → 设 VirtualLink 双端 Anchor → CurrentSender = slot，State = PreviewSender
ReceiveConnection(receiverSlot) → 校验 receiver 方向 → ValidateConnection(sender, receiver)   ← 用户可覆写的唯一连接谓词
                                → 拒同节点 → 清同向冲突 → 按 receiver channel 清旧 → StandardCreateNewConnection
```

依据：`StandardEx/WorkflowTreeEx.cs:105-195`。**注意校验全在 receiver 侧**：`SendConnection` 只查 sender 自己的能力和容量。

### 3.3 编译 → 运行

```
CompilerViewModel.CompileAsync<T>(node, role, ct)          CompilerEx/Compile/CompilerViewModel.cs:37
  ├ CompileRole.Root     → CompileGraphAsync               :59
  └ CompileRole.Terminal → BuildAncestorConeAsync          CompilerViewModel.Reverse.cs:34
                          → CompileConeAsync               :82
  ↓ 产出
CompiledGraph { Entries: CompileSegment[] }                CompilerEx/Compile/Model/CompiledGraph.cs:12
  段有三种：ChainSegment / BranchSegment / ParallelSegment
  ↓ 每个节点被 AttachCompileTimeContext 注入 CompileContext { Order, ChainIndex, Offset, InputNodes }
                                                           CompilerViewModel.cs:267
  ↓
RuntimeEngine.RunAsync(graph, IRuntimeContext, ct)         CompilerEx/Runtime/RuntimeEngine.cs:52
  → RunGraphAsync :131 → RunExecuteAsync :166 / RunBranchAsync :254 / RunParallelAsync :329
  → DriveAsync(node) → node.GetHelper().ReceiveAsync(context, ct)   :412/:468
```

**扇出是并发的**（2026-09-27 起）：`RunParallelAsync` 用 `Task.WhenAll` 让分支交错执行（不是线程并行，也不离开宿主的 `SynchronizationContext`），每分支一个 `BranchRuntimeContext` 门面。**并发的取舍、确定性政策、以及「为什么不能用 `AsyncLocal`」——见 [compiler-execution.md](compiler-execution.md)。**

编译期的三个身份：`Order`（全局执行序，**`-1` = 绝对停止**）、`ChainIndex`（链内下标）、`Offset`（子图入口偏移）。定义在 `CompilerEx/Compile/Contracts/ICompileContext.cs`。

**Router 的静态/动态是运行期判定的**：编译器调 `router.ResolveRouteKey(null)`（`CompilerViewModel.cs:94`），返回 `null` 就是动态（`isDynamic = currentKey is null`，`:95`）。`RouterCompileMode` 枚举（`CompilerEx/Compile/Contracts/RouterCompileMode.cs:15`）**只是给用户代码/Agent 面用的标签，编译器从不读它** —— `Src/Core/VeloxDev.Core/` 下除定义外零引用；其余引用只在测试（`VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`）与 demo（`Examples/Workflow/`）。**AI 工具面（`VeloxDev.Core.Extension`）的源码里已无引用**（只有 `bin/` 编译产物残留符号），旧的 `WorkflowAgentScope.cs:33` 那条依据已不成立。

**redirect 不是环**：编译产物一定是无环的。redirect 是运行期契约 —— `IRedirectable.ResolveRedirectAsync` 返回一个更早的 `Order`，`RuntimeEngine` 就以这个 target **重跑整张图**（`RuntimeEngine.cs:52-120`），最多 `MaxRedirects = 50`（`:55`）。每轮 `context.Attempt = redirects + 1`（`:85`），输出按 `Attempt` 戳 + `ActiveRedirectTarget` 前缀双重过滤（`CompilerEx/Runtime/Model/RuntimeContext.cs:404`）。**注意：`Src/` 里没有一个 `IRedirectable` 实现者**（2026-09-27 起 demo 的 python 节点是仓里唯一的参考实现：脚本按标题点名要回退的节点，节点再把标题解析成编译 Order），所以没人会把运行拉回去 —— `Warn` 就是一行日志（运行继续、值照传），`Error` 与未捕获的异常则直接结束整轮（见 [compiler-execution.md](compiler-execution.md) §六）。

**汇合聚合**：`CompileContext.InputNodes.Count > 1` 时，`DriveAsync` 注入 `new GroupData(context.CollectGroupedInputs(inputs))` 作为 `Data`（`RuntimeEngine.cs:466`），它是一个只读字典 `IReadOnlyDictionary<IWorkflowNodeViewModel, object?>`（`CompilerEx/Runtime/Model/GroupData.cs:17,26`），Key = 来源 Node 的**引用身份**（比较器是 `WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>`，`GUI/Virtualization/WorkflowSpatialEx.cs:338`）。单输入时 `Data` 保持裸链式传递，`InputNodes` 为 `null`。

### 3.4 渲染（几何）

```
适配器的 slot-layout 行为测量 → 写 slot.Anchor（世界坐标）
节点视图读 node.Anchor / node.Size  ← getter 按 Layout.Scale 折叠（除以 Scale）
连线视图读两端 slot.Anchor → 首行调 link.IsRenderReady()  ← NaN 就 return
```

- **`WorkflowSurfaceMath` 是全模块唯一的坐标数学**（`GUI/Math/WorkflowSurfaceMath.cs:17`），七家适配器以前各自内联过。
- **连线的形状归 Core 算，视图不再各推一遍**：`LinkCurve.LinkCurvePoints(link, 起点, 终点, pullMinimum)` 给出要画的四个控制点，`LinkCurve.BuildLinkCubic(...)` 把同一条采样成命中用的 `LinkCurve` —— 两个函数同源，所以「画出来的」与「能点中的」不可能不一致（`GUI/Interaction/LinkCurve.cs:114,166`）。控制点沿**每个口自己那条边**的外法线拉，不是写死水平：写死的那版在口位于上/下边、或连线反向时会把控制点戳进自己节点（`LinkPortCurveTests.cs` 的 `LinkCurvePoints_NeverFoldsIntoItsOwnNode` 钉的就是这条）。**任一端没有节点就整条退回房规**（起点 +x、终点 −x）—— 拖拽预览的两端都是占位插槽、没有边可读，而两端都不拉会把橡皮筋拉成直线。
- **方向只看两个端点各自相对于父节点的实际位置**（`PortOutward(x, y, node)`，`:196`）：不读 `slot.Anchor`，也不看谁是发送端、端口画在哪条边 —— 调用方传进来的坐标就是判据。**前提是那两个坐标与 `node.Anchor` / `node.Size` 同系**：坐标是**别的**系时，调用方必须先把它们搬过去再调（WinForms 的 Trimmed 那条路就是 —— 它的 `slot.Anchor` 是客户区坐标，差一个表面投影，见 [`adapters/winforms.md`](adapters/winforms.md) §4.12）。同系的各家（从绑定读 `slot.Anchor` 的六家、以及**从模型推端口位置的 Jalium**：`PortCenter` 给的就是世界坐标）直接用，不用改公式。
- **NaN = 未测量**这个约定是渲染就绪门的全部内容。注意**只有 slot 的 anchor 默认 NaN**：`Anchor` 类本身的构造默认是 `0d`（`GUI/GeometryModels/Anchor.cs:11` 的 `Anchor(double left = 0d, double top = 0d, int layer = 0)`），是 slot 的字段给了 NaN —— 生成器版 `Writers/WorkflowWriter.cs:1045-1046`（注释：「Slot anchor defaults to NaN (no value): links don't render until both anchors are measured by the GUI」），默认实现版 `Templates/ViewModels/SlotDefaultViewModel.cs:38`。node 的 anchor 默认 `0` 或 `[DefaultAnchor]`（`Writers/WorkflowWriter.cs:779`），`VirtualLink` 双端在重置时**故意**回到 NaN 而不是原点（`StandardEx/WorkflowTreeEx.cs:196-201`）。检查在 `GUI/Rendering/WorkflowSlotUpdateGate.cs:20`，包装在 `GUI/Rendering/WorkflowLinkRenderEx.cs:27`（额外看 `IsVisible`）。**不需要任何事件订阅或时间戳** —— 测量写入真实坐标后绑定自动刷新。未挂到节点的 slot（`Parent is null`，如拖拽预览）直接算就绪（`WorkflowSlotUpdateGate.cs:28-33`）。
  > ⚠️ `WorkflowSlotUpdateGate.cs:7-8` 的 XML 注释写的是「`Anchor` 默认 horizontal/vertical 为 `double.NaN`」—— **那句话与代码不符**（`Anchor.cs:11` 的构造默认是 `0d`）。它想表达的是 **slot 的约定**，不是 `Anchor` 类的默认值。按下一条行事，别按那句注释。
- **同一套 NaN 约定还贯穿空间索引**：bounds 为空/NaN 的条目会被登记用于变更跟踪但**不进网格**（`GUI/Virtualization/SpatialGridHashMap.cs:72-78`），端点未定位时连线对返回 `Empty` bounds（`GUI/Virtualization/NodePairBoundsProvider.cs:74`），所以未测量的节点不会以 NaN 坐标进索引。
- 坐标系约定：`SlotAnchorFromVisualCenter` = `视觉中心 − ActualOffset`（即画布局部），`SlotAnchorFromNode` = 节点锚点 + 局部偏移，`SlotAnchorFromCanvasLocal` 用于已经是画布局部坐标的情形。三个都在 `GUI/Math/WorkflowSurfaceMath.cs:230,237,248`。
- 渲染变换是 `ScaleCollapse` = `(1/scaleX, 1/scaleY, -anchorX, -anchorY)`（`WorkflowSurfaceMath.cs:395`）。`CanvasLayout.Update()` 在 `Scale < 1` 时把 `ActualSize` 放大 `1/Scale`（`CanvasLayout.cs:104-122`），否则折叠后的内容会越出画布。

### 3.5 虚拟化

```
TreeHelper.Install → tree.EnableMap(CellSize, VisibleItems)       Templates/Helpers/TreeHelper.cs:125
                   → WorkflowSpatialManager 建立空间网格            GUI/Virtualization/WorkflowSpatialManager.cs:30
TreeHelper.Viewport 写入 / MarkDirty() → 10fps Tickable tick  Templates/Helpers/TreeHelper.cs:63
                   → Virtualize(Viewport) → VisibleItems 更新       GUI/Virtualization/WorkflowSpatialEx.cs:98
                   → BroadcastVisibleItemLayout()（对每个可见节点重发 Anchor/Size）
```

- **只有 `TreeHelper(double cellSize)` 这个构造开虚拟化**，无参构造把 `useVirtualization = false`（`Templates/Helpers/TreeHelper.cs:38-52`）。「图每次都全渲染」十有八九是用了无参构造。
- 索引有两张：节点网格 `_nodeMap` 与节点对（连线）网格 `_nodePairMap`（`WorkflowSpatialManager.cs:11-12`）。连线在两端都还没被索引时进 `_pendingLinks` 暂存，节点插入后 `RetryPendingLinks()` 补挂（`:22,254`）。
- 查询是 `QueryAgentBounds(viewport, expansionDepth: 1)`（`:80`），扩张一层是为了把「刚好在视口外但连线要穿过视口」的端点也捞出来。
- **查询之前索引必须补齐（2026-09-27 起）。** `SpatialGridHashMap.Query` 先跑一次 `EnsureIndexed()`：① 把重入守卫延后的那次 `ResyncGrid` 补上（原先它只在下一次 bounds 变化时才跑）；② 给**边界曾经为空**的条目（视图还没测量 ⇒ 登记了但**不在任何格子里**）再读一次 `Bounds`，变成真的就补进网格。不补的后果是**永久性**的：那类条目任何查询都碰不到 ⇒ **视口怎么移都救不回来**（用户实测：Agent 对话进行中节点/连线概率消失，重入 Viewport 无效）。判别测试 `SpatialIndexFreshnessTests.AnItemMeasuredSilently_IsFoundByTheNextQuery`（把 `EnsureIndexed()` 注释掉即红）。同族的 `WorkflowSpatialManager.QueryAgentBounds` 也在查询前补一次 `RetryPendingLinks()` —— 那条暂存原本**只**由 `NodeAdded` 触发，此后没有新节点就永远挂着。
- 重入守卫、bounds 空/脏态、resync：`GUI/Virtualization/SpatialGridHashMap.cs:21-23,56,202,247`，以及 `WorkflowSpatialEx.cs:14-24` 的 `ConditionalWeakTable` 重入表。
- 视口修正 `RulerBand` → `SetVirtualizeInset`（`WorkflowSpatialEx.cs:235`）：只影响 `Virtualize` 内部的查询膨胀，**不动权威的 `Viewport`**。

### 3.6 交互（命中测试）

分两层，别把第二层当成第一层：

**① 手势那层仍然没有 Core 代码** —— 命中谁、拖动还是连线，判断在适配器的 Behavior 里，判断完调组件的 `IVeloxCommand`：

```
适配器 Behavior 的 pointer 事件 → 决定命中谁 → 调组件的 IVeloxCommand（如 node.MoveCommand / slot.SendConnectionCommand）
                                → StandardEx → 改模型状态 → 属性变更通知 → 视图重绘
```

**② 输入归 Core，是一套标准输入**（2026-10-04 起，替换掉原来的「按组件定制的事件」）。链路是：

```
连线视图画完 → link.PublishCurve(曲线, 自己)      // 视图是形状的所有者，也是事件的 Source
适配器把原生指针/按键翻译成标准输入 → WorkflowInput.For(tree).Route(args)
    → 按 args.Target 展开祖先链（link→tree / slot→node→tree / node→tree / 空白→tree）
    → 每级取 IInputEvents.Input 派发（目标先、祖先后）
    → 指针换了目标时先给留下那个发 Exited、给新那个发 Entered
    → Core 自己不做任何动作：KeyDown 只路由，删不删由定了 KeyDown 的宿主决定
```

- **API 形状抄 Avalonia**（`GUI/Events/Input/`）：`PointerEventArgs` 六个具体子类（Entered/Exited/Moved/Pressed/Released/Wheel）、`KeyEventArgs` 两个（Down/Up）、`MouseButton`（`None/Left/Right/Middle/XButton1/XButton2`）、`InputModifiers`、`InputKey`（务实子集，其余报 `Unknown` + `RawKeyCode`）。**2026-10-05 起这些名字不再带 `Workflow` 前缀**（`WorkflowKey` → `InputKey`，其余直接删前缀：`PointerPressedEventArgs` / `KeyEventArgs` / `MouseButton` …）。原文那句「前缀是硬要求，因为适配器同时 `using` 平台命名空间」只说了表层，**真正的机制是命名空间的从属关系**：适配器文件的命名空间是 `VeloxDev.WorkflowSystem.AttachedBehaviors` —— **Core 命名空间的子命名空间**；C# 先解析外层命名空间里**声明**的成员、全都找不到才轮到 `using` 引入的名字，所以 Core 的裸名会**静默盖住平台同名类型**：不报 CS0104 歧义，而在下游报 `CS1061 未包含 GetPosition/Handled/Pointer`、`CS0019 运算符 != 无法应用于 MouseButton 和 MouseButton`、`CS0115 没有找到适合的方法来重写` 这类看着毫不相干的错（去掉前缀后第一次全量构建：134 个错误、11 个文件）。

- **平台层怎么引用这 15 个名字 = 规范，不在这里**（用户 2026-10-05 定为规范）：适配器与 item template 每个文件**同时持有 `Wf` / `PlatformInput` 两个别名、处处带前缀**（哪怕该名字在本文件里并不冲突、哪怕某个别名一次没用上）；demo 只在**真冲突**处留别名 —— 本轮只有四个 demo 文件留：Avalonia Demo `Views/Workflow/SlotView.axaml.cs`、Jalium Demo `Views/Workflow/NodeEditorSurface.cs`、WPF Demo `Views/Workflow/WorkflowView.xaml.cs`、WinForms Demo `Controls/WorkflowCanvas.cs`，其余 demo 保持裸名。这 15 个之外的 Core 名字（`WorkflowInput` / `WorkflowEventHandle` / `Anchor` / `IWorkflowTreeViewModel` …、以及按组件定制的那三个事件族）也保持裸名。清单、判据（按用途不按名字）、核查命令与自查表都在 [memory/specifications/input-alias-specifications.md](../../specifications/input-alias-specifications.md)。
- **位置是 `Anchor`，`Position.Layer` 取来源视图所在图层**。但**指针本身没有图层**：`SetPointerCommand` / 虚拟连线端点仍按旧规则取起点那一端的图层。
- **能力接口 + 一个 relay**：`IInputEvents { InputRelay Input; }`，四组 Helper（`TreeHelper<T>`/`NodeHelper<T>`/`SlotHelper<T>`/`LinkHelper<T>`）都实现。宿主订阅：`((IInputEvents)link.GetHelper()).Input.PointerEntered += …`。
- **命中归适配器判**（`LinkHitTestEx.HitTestVisibleLinks` 是它调的那个共享算法）：Core 不新增 node/slot 命中。

**两相没了，只剩「订阅者先跑」**：原来是 `Preview*/Outcome` 两相共用一个句柄；现在**一次动作只发一次**，而且 Core 本身没有默认动作可说「之前」，`WorkflowEventHandle` 仍在、语义不变：
`PreventDefault` = 框架这一手不执行（Delete 就是「这条不许删」）、`StopPropagation` = 到此为止、祖先一个都收不到。两个标志都不设时行为与它们出现之前逐字相同。

- 句柄**一次路由一个**：整条链共用，所以祖先能读到目标那级做了什么决定。
- `IsSuspended` 仍在 Core（`WorkflowInput`）：菜单开着时指针跟踪不动 —— **`Exited` 也要认**（2026-10-03 由 Jalium 实测逼出来的那条不变）。

**右键菜单**（2026-10-04 起）：Core 不再有 `ContextMenuRequested` 这一族。**适配器从自己的 `PointerPressed(Right, link)` 里弹**，宿主想否决就在链上更靠前的一级（连线自己）订同一个事件并置 `PreventDefault` —— 顺序由「目标先于祖先」保证，与订阅先后无关。开合由适配器自己记账（置 `WorkflowInput.IsSuspended`）。**「菜单不能比它指着的那条线活得久」改由 `tree.GetHelper().LinkRemoved` 实现**（树既有的事件，Delete/Undo/Agent 改树都会发）：七家各订一次，WinForms/Jalium 落在基类、标记五家落在 `WorkflowSurfaceBehavior`。

**光标下的连线视图不再由 Core 点亮**（2026-10-04）：悬停外观是**宿主/demo** 的事 —— 订那条线自己的 `Input.PointerEntered` / `PointerExited` 即可，互斥不需要记账（路由已经保证「离开的先收 Exited、进入的后收 Entered」）。Core 里没有 `ILinkHighlight`、也没有 `AutoHighlight`。

**模型层（节点/插槽/树的动作为准）的事件挂在各自的 Helper 上**，经**能力接口**暴露（2026-10-03 起）：

```
IWorkflowNodeEvents : Moving/Moved · Resizing/Resized · Deleting/Deleted
IWorkflowSlotEvents : ChannelChanging/Changed
IWorkflowTreeEvents : Connecting/Connected
```

- 与连线那套**同一形状**：`Xxx…ing` 在框架动手**之前**（`WorkflowEventHandle.PreventDefault` = 这一次不发生）、`Xxx…ed` 在之后，两相共用一个句柄。
- 由 `StandardEx` 在改动模型之前/之后问一下 Helper（`RaiseMoving` / `RaiseMoved` …）—— 与 `ValidateConnection` 同一条路子：**框架问 Helper，Helper 问宿主**。
- 能力接口而**不是** `IWorkflowNodeViewModelHelper`：往那个接口加成员会打断每一个实现者（`ILinkHitTestable.cs` 的 remarks 是这条规矩的出处）。宿主订阅要转型：`((IWorkflowNodeEvents)node.GetHelper()).Moving += …`。
- `Connecting` 与 `ValidateConnection` **复合**：事件否决 或 校验器为假 ⇒ 不连。

**凡是交给宿主的组件落位（`Anchor`），都是完整落位 —— 图层跟着走。** 最典型是 `NodeMoveEventArgs.From/To`：宿主若拿 `To` 自己落位、或存 `From` 撤销，不能把图层抹成 0。指针位置没有图层（例外）；指针成为虚拟连线终点时，图层由 `StandardSetPointer` 统一取起点那一端。

**右键菜单是这条链上唯一一个「默认动作由订阅方做」的动作，所以它也有 Preview 相**（2026-10-03）：
`ContextMenuRequesting`（可否决）→ `ContextMenuRequested`（谁弹菜单谁订这一相）。两相**共用同一个 args 与句柄**。
为什么非要有：别的动作的「默认」是框架自己干的，框架当然在事件**之后**才动手；而菜单的默认是**订阅方**去弹，
没有 Preview 相的话，「否决」与「弹出」就靠**订阅顺序**决胜负 —— 谁先订谁说了算，后订的否决白否决。
补上这一相之后顺序由构造保证，**七家一行都不用改**（它们订的都是「弹」那一相）。

⚠ **`IsSuspended` 要到 `Exited` 也认**（2026-10-03 由 Jalium 那家实测逼出来）：原来只有 `Moved` 分支判挂起，
于是菜单一开、指针飞到菜单上，`Exited` 照样把 hover 清掉 —— 菜单正要作用的那条线瞬间不再高亮。
现在两处同一条判据。各家自己额外拦过 `Exited` 的那一段，**2026-10-03 已从七家全删**：Razor
`WorkflowSurfaceBehavior.razor.cs`、MAUI `WorkflowLinkOverlay.OnHoverExited`、Jalium `OnMouseLeave`
（这三家是当初点名的），以及 WinUI / WPF / Avalonia / WinForms 四家的适配器（同一处冗余，一起清）。
留着的坏处不是多一次判断，而是让人误以为「平台不清 hover 是平台的功劳」。

**右键菜单三事件也在同一个 hub 上**：`ContextMenuRequesting`（Preview，可否决 —— 这就是「这里不给菜单」的写法）
→ `ContextMenuRequested`（谁弹菜单谁订这一相）+ `ContextMenuOpened` / `ContextMenuClosed`。菜单**本身仍是宿主的**
（要选位置、要平台自己的弹出物），宿主用 `Publish(ContextMenuEvent)` 报回开合，hub 据此自动收放 `IsSuspended`。
七家原先各自手工 `IsSuspended = true/false` 那一段，**2026-10-03 已一处不剩**；六个完整 demo 也一并从
`LinkPressed` 改成订 `ContextMenuRequested`，因此 **`LinkPressed` 今天没有订阅者**（事件仍在，留给兼容与自定义）。
⚠ **否决只在 Preview 相有效**：`Requested` 那一相读 `e.Handle.PreventDefault` 永远是 false。

**「菜单不能比它指着的那条线活得久」也归 hub**（2026-10-03 用户定）：hub 记下 `Opened` 报来的那条线，
盯 `tree.Links.CollectionChanged`；它一离开（Delete 键、Undo、Agent 改树都算）就发
`ContextMenuDismissRequested`（`GUI/Events/Menu/ContextMenuDismissRequestedEventArgs.cs`，带 `Link`）。
hub 收不了宿主的弹窗，所以这是**请**不是做：宿主关掉自己的菜单、照常报 `Closed`，挂起随之放开 ——
`IsSuspended` 的责任人仍然只有 hub 一个。**判定只此一处**，所以七家天然一致；此前只有 Jalium 完整 demo
自己带过一份（WinForms 一侧没有），那种按平台各写一遍的正是漂移的成因。⚠ 已知边界：订阅是构造时
一次性订在当前那个 `Links` 实例上，若有人整体替换 `tree.Links`（只有生成的 AIContext 反序列化会），
这条会静默失效 —— `TreeHelper.Install` 有同一个边界，真要修得连它一起修。

要点：

- **输入只有一个位置**：`WorkflowInput.For(tree)`（`GUI/Events/WorkflowInput.cs`），一棵树一个实例、`ConditionalWeakTable` 缓存。适配器只**转发**，宿主与组件视图都从组件的 Helper 上订 —— 没有「每个表面各持一个」这种说法。它同时是 `HitRadius` / `IsSuspended` / `PointerTarget` / `HoveredLink` 的持有者。
- **命中判据是「已发布的曲线」**，不是「锚点测没测到」。视图画不出来时用 `PublishCurve(null)` 撤回，所以「没有曲线」就等于「那里没有东西」。**不要**改回按 `IsRenderReady()` 判 —— Jalium 按设计从不写 `slot.Anchor`，那样会让它整家连线静默失效。
- **命中面只是画出来的那道描边**，不是整块画布：曲线就是视图画的那条，半径 `LinkHitTestEx.DefaultHitRadius`（6）。
- **曲线是运行期几何，永远不序列化**（别把它挂上任何归档序列化路径：不给它 `[Archivable]`，也不让它成为某个被收录成员的声明类型）。

⇒ 「加一个新的连线交互动作」（比如双击重命名）现在就是订标准输入：适配器把那次指针事件路由进来，宿主在组件上订它 —— **不用改 Core**；但**手势**（拖动、连线）仍是适配器的事。

节点命令共 8 个（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:36-78`），Tree 8 个（`IWorkflowTreeViewModel.cs:41-83`），Slot 4 个（`IWorkflowSlotViewModel.cs:46-64`），Link 1 个（`IWorkflowLinkViewModel.cs:30`），另有全部组件共有的 `CloseCommand`（`IWorkflowViewModel.cs:31`）。

**这意味着「加一个新的交互手势」通常完全不改 Core** —— 在适配器里发一条已有的命令即可；只有命令语义不够时才回 Core 加 `StandardXxx`。

---

## 四、不变量

违反下面任何一条，通常**不报错**，只是静默行为错。

1. **一次变更只能入栈一次。** 每个改模型的动作必须恰好经过一个 `Submit`。`SetSelector` 内部自己提交了一个 undo pair（`SelectorEx/SlotEnumerator.cs:287`），再包一层 `Submit` 就是两层栈项、Ctrl+Z 语义崩坏。AI 工具面的 `SetEnumSlotCollection` 注释直接写明了这一点（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs`）。
2. **`Move` / `SetAnchor` / `SetSize` 按设计不可撤销**（`StandardEx/WorkflowNodeEx.cs:74,93,110`）。同理 `WorkflowAgentToolkit` 的 `MoveNode`/`SetNodePosition` 文档里点明下层 `SetAnchorCommand` 没有 undo 项。
3. **`Anchor` / `Size` 的 getter 是折叠值**（见 §二）。一切修改走 setter，setter 存世界原值。
   ⚠ **对外的读数也必须是世界值**：AI 工具面的 `ListNodes` / `GetNodeDetail` 原来直接读 getter，于是同一个位置写进去、读出来不是同一个数，比例还随缩放变（2026-10-05 修，见 [`AI/architecture.md`](../AI/architecture.md) §七·七）。凡是把节点几何报给宿主/模型的地方，都要乘回 `Layout.Scale` —— 渲染需要折叠值，读数不需要。
4. **`WorkflowGuard.Fail` 在 Release 里是彻底的空操作**。它是 `[Conditional("DEBUG")]`（`WorkflowGuard.cs:19`），连同消息参数一起被编译掉。所以 `StandardSetChannel` 在 slot 没挂到树上时 `return`（`StandardEx/WorkflowSlotEx.cs:19-23`）—— Debug 抛异常，Release 静默什么都不做。**不要把 `WorkflowGuard.Fail` 当成运行时防御**。
5. **`Anchor` 默认 NaN 表示未测量**，连线在任一端点 NaN 时不得渲染（`WorkflowSlotUpdateGate.cs:20`）。
6. **`EnableMap` 必须先于 `Virtualize`。** `VirtualizeCore` 找不到空间映射抛 `ArgumentNullException`（`WorkflowSpatialEx.cs:127`）；`EnableMap` 的返回码是 `-1`（cellSize ≤ 0）/ `0`（已启用）/ `1`（成功）（`WorkflowSpatialEx.cs:47-67`），`Uninstall` 里用 `ClearMap()` 的返回值 `== 5` 做 `Debug.Fail` 兜底（`Templates/Helpers/TreeHelper.cs:143`）。
7. **`EnsureNegativeCover` 的调用时机是硬约束**：必须在写完新 `Scale` 之后、读 `ActualOffset`/`ActualSize` 之前（`GUI/Math/WorkflowSurfaceMath.cs:438` 及其上方注释）。增长单调。
8. **`ClampScrollOffset` 在正/负边缘行为不对称**（`WorkflowSurfaceMath.cs:116`）：正边缘直接返回 `max`、只把 extent 拉长；负边缘在 `extendRatio > 0` 时抬高 `NegativeOffset` 并返回增长后的量，让调用方的前向滚动抵消这次平移。
9. **`SlotEnumerator` 的 slot 属性必须写成 `[VeloxProperty] public partial T X { get; set; }`。** 生成器按 `_camelCase` 合成后备字段并在 `InitializeWorkflowCore` 里引用它，手写一个别的名字的字段会让生成的初始化代码失效（`Src/Generators/VeloxDev.Core.Generator/Writers/WorkflowWriter.cs:1390` 一带；生成规则见 `skills/veloxdev-create-workflow/references/model.md`）。
10. **`InitializeWorkflowCore` 注册 slot 时刻意绕过 `CreateSlotCommand`**，因为那时 `node.Parent == null`。手写这条路会踩到 `WorkflowNodeEx.StandardCreateSlot` 里的幂等守卫（`StandardEx/WorkflowNodeEx.cs:28`：同引用的 slot 二次派发直接 `return`）—— 守卫的存在正是为了不让「迟到的延迟派发」推进一个幽灵 undo 项，其 Undo 会把 slot 撕出来。
11. **`Channel` 的自动清理只发生在 `One*` 通道上**。`ShouldCleanupConnections` 只看 `OneTarget`/`OneSource`/`OneBoth` 位（`StandardEx/WorkflowTreeEx.cs:557-586`），`Multiple*` 从不删任何东西；「顺带清理反方向」那一条门控在 `HasFlag(OneBoth)`（两个 one 位同时存在），所以 `MultipleBoth` 不触发。
12. **两种 Slot 实现的默认 Channel 不一致**：生成器造的 slot 体默认 `MultipleBoth`（`Writers/WorkflowWriter.cs:1043`），而 `SlotDefaultViewModel` 默认 `OneBoth`（`Templates/ViewModels/SlotDefaultViewModel.cs:36`）。行为对不上 demo 时先查这里。

---

## 五、分层与依赖方向

```
Interfaces/WorkflowSystem/     纯契约，零实现
        ↑
WorkflowSystem/                实现（Templates 默认实现、StandardEx 扩展方法、CompilerEx、GUI）
        ↑
VeloxDev.Core.Extension/Agent/ 消费方（AI 工具面），命名空间 VeloxDev.AI.Workflow
        ↑
Src/Adapters/VeloxDev.*/       七家 GUI 适配器
```

- **Core 不引用任何 UI 程序集**（见 §一 csproj）。方向是单向的：适配器 → Core。
- **适配器之间不互相引用**，也没有共享适配器基类 —— 七家各写各的 `Attached/Workflow/`（文件清单见 `Src/Adapters/VeloxDev.*/Attached/Workflow/`）。
- **`VeloxDev.Core.Extension` 在 Release 走 NuGet 包、Debug 走 ProjectReference**（`VeloxDev.Core.Extension.csproj` 的 ItemGroup），`VeloxDev.Core` 自己同理。生成器（`Src/Generators/VeloxDev.Core.Generator/`）是 analyzer，**不随 ProjectReference 传递**，所以每个用生成器特性的项目都要各自重复那一对引用。
- **`StandardEx/` 是扩展方法而非接口成员**：`StandardXxx(this IWorkflowXxxViewModel, ...)`。这意味着「默认行为」和「契约」是分开的 —— 接口干净，行为可以整体换掉。Helper 的虚方法（如 `NodeHelper<T>.ReceiveAsync`）再转调 `Component.StandardXxx`。改行为时先分清你要改的是**接口**（影响所有实现）、**StandardEx**（影响所有用 Standard 的实现）还是**Helper 虚方法**（影响继承该 Helper 的实现）。

---

## 六、入口表：我要改 X，先打开哪个文件

| 我想改 | 打开 |
|---|---|
| 节点收到数据时算什么 | `Templates/Helpers/NodeHelper.cs` 的 `ReceiveAsync`（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:123` 是契约）。默认返回 `null` = 静默不往下传 |
| 一条边能不能连 | `Templates/Helpers/TreeHelper.cs` 的 `ValidateConnection`（`:296`，默认 `true`）。协议见 §3.2 |
| 用户拖出的连线用什么类型 | `TreeHelper<T>.CreateLink`（`Templates/Helpers/TreeHelper.cs:171`，默认 `LinkDefaultViewModel`） |
| 连接的容量/自动清理规则 | `Enums/Slot.cs:9` 的 `SlotChannel` + `StandardEx/WorkflowTreeEx.cs:557` 的 `ShouldCleanupConnections` |
| slot 的状态位怎么算 | `StandardEx/WorkflowSlotEx.cs:88` 的 `StandardUpdateState`（由 `Targets.Count`/`Sources.Count` 推导） |
| 改通道时旧连接怎么清 | `StandardEx/WorkflowSlotEx.cs:19` 的 `StandardSetChannel`（按**旧**通道的位决定删哪些） |
| 图怎么被切成执行段 | `CompilerEx/Compile/CompilerViewModel.cs`（正向）与 `CompilerViewModel.Reverse.cs`（逆向锥） |
| 运行期怎么走 | `CompilerEx/Runtime/RuntimeEngine.cs`（`RunGraphAsync` → `DriveAsync`） |
| 重定向 / 重试语义 | `CompilerEx/Runtime/RuntimeEngine.cs:52-120`；契约 `CompilerEx/Runtime/Contracts/IRedirectable.cs` |
| 暂停 / 观察 / 重试 / 结构化错误 / 补偿 / 检查点 | 六个可选能力都是 `RuntimeContext` 的具体类成员，引擎在 `DriveAsync` 与 `RunAsync` 的收尾里读；见 [compiler-execution.md](compiler-execution.md) §十（前五个）与 §十一（检查点） |
| 汇合点的输入怎么聚合 | `CompilerEx/Runtime/Model/RuntimeContext.cs:384` 的 `CollectGroupedInputs`；结构 `GroupData.cs:26` |
| 画布坐标换算 | `GUI/Math/WorkflowSurfaceMath.cs`（全模块唯一数学，别在适配器里重写） |
| 缩放时画布怎么长 | `GUI/GeometryModels/CanvasLayout.cs:104` 的 `Update()` |
| 缩放后保存/加载丢东西 | `GUI/GeometryModels/Anchor.cs:64-104` 的序列化钩子（`Size.cs:64-97` 同构） |
| 什么进可见集 | `GUI/Virtualization/WorkflowSpatialEx.cs:118`；暂存与补挂 `WorkflowSpatialManager.cs:183,254` |
| 网格索引的数据结构 | `GUI/Virtualization/SpatialGridHashMap.cs`（注意 `:171` 的注释：重入时改 `Dictionary` 会毁内部状态） |
| 可见集变化后怎么让视图刷新 | `Templates/Helpers/TreeHelper.cs` 的 `BroadcastVisibleItemLayout()`（对每个可见节点重发 `Anchor`/`Size`） |
| 撤销/重做栈 | `StandardEx/WorkflowTreeEx.cs:217-275`（`StandardRedo`/`StandardSubmit`/`StandardUndo`/`StandardClearHistory`） |
| slot 数量可变的节点（选择器） | `SelectorEx/SlotEnumerator.cs`、`SelectorEx/ConditionalSlot.cs`、`Interfaces/WorkflowSystem/IConditionalSlotProvider.cs`（**两个重载：泛型 + 非泛型，见下**） |
| 网格装饰器 / 小地图的 Core 契约 | `Interfaces/WorkflowSystem/IWorkflowGridDecorator.cs`、`IWorkflowMinimapOverlay.cs` |

---

## 六点五、选择器有两个「视野」：泛型一个，非泛型一个

`SlotEnumerator<TSlot>` 与 `ConditionalSlot<TSlot>` 是泛型，所以**只拿到一个 object 的调用方打不开它们** ——
而 Agent 工具面（`WorkflowAgentToolkit`）正是那种调用方。以前它靠反射读 `Items` / `SelectorType` /
`Slot` / `TrySelect`，那在裁剪下站不住。

2026-10-03 起两对接口并存，泛型类**显式实现**非泛型那个：

| 泛型 | 非泛型 | 擦掉的是什么 |
|---|---|---|
| `IConditionalSlotProvider<TSlot>` | `IConditionalSlotProvider`（`Interfaces/WorkflowSystem/IConditionalSlotProvider.cs`） | `TSlot`：留 `Parent` / `SelectorTypeName` / `SelectorType` / `Slots`（`IReadOnlyList<IConditionalSlot>`）/ `TrySelect(object, out IWorkflowSlotViewModel?)` / `SetSelector(object?)` |
| `ConditionalSlot<TSlot>` | `IConditionalSlot`（`SelectorEx/ConditionalSlot.cs`） | `TSlot`：留 `Name` / `Value` / `Slot`（`IWorkflowSlotViewModel`） |

**三处必须显式实现，别想着改成隐式**：`Slots`（属性类型不协变，`Items` 是
`ObservableCollection<ConditionalSlot<TSlot>>`）、`IConditionalSlot.Slot`（提升出来的 `Slot` 是 `TSlot`，
接口要的是 `IWorkflowSlotViewModel`）、`IConditionalSlotProvider.TrySelect`（`out` 参数的类型不参与重载
解析，与泛型版同名共存只能是显式）。`Parent` / `SelectorTypeName` / `SelectorType` / `CurrentValue` /
`SetSelector` 类型一致，隐式实现即可。

**非泛型的 `Slots` 是投影不是副本**：`IReadOnlyList<out T>` 协变，`Items` 直接赋给它，改 `Items` 立刻可见。

**为什么值得**：`WorkflowAgentToolkit` 里原来那些 `GetProperty("Items")` / `GetProperty("Slot")` /
`GetMethod("TrySelect")` / `GetMethod("SetSelector")` 现在都是一次转型。`GetEnumSlotByValue` 也因此改成
**按标签匹配条目**（`item.Value.ToString()`，忽略大小写），而不是把名字 `Enum.Parse(Type, …)` 回枚举值 ——
后者正是要绕开的那类反射。

### `SetSelector` 的记住状态：枚举按类型名缓存，**provider 不缓存**（2026-10-05 修）

`_typeStates` 那张表按**选择器类型名**存 `SelectorState`（槽位 + 布线），切回某个类型时连布线一起复原。
这对**枚举**是对的：槽位来自类型本身。

对一个 `ISlotProvider` 选择器它是错的，而且**错得很安静**：provider 不是类型而是**值**，端口表在实例身上，
同一个类的两个实例可以给出完全不同的端口。按类型名查表会命中上一次调用的快照、`newItems` 直接丢掉，
`SetSelector` 返回后调用方看到的是「成功」。`SetSelector` 里因此有一个 `isProviderSelector` 分支，
provider 一律 `isFresh = true`。

**症状**：Agent 的 `SetEnumSlotCollection` 走的正是这条路，provider 类型永远是同一个 —— 所以**同一个节点
第二次改端口不起作用**。用户报的「Merge Report 的输入口无法扩展」有一半是这个（另一半是没有
`[Archivable]` 的 provider，见 [AI 记忆](../AI/architecture.md) §七·五）。
`PythonScriptNodeViewModel` 的构造函数注释曾把这个缓存当作「端口不能在构造函数里设」的理由 —— 那条注释
已随这次修复改写，别再照着它推理。

守卫：`Core.Test/WorkflowSystem/SlotEnumeratorTests.ReSettingAProviderSelector_RebuildsTheSlots`
（Core 这一层）与 `Extension.Test/…/SetEnumSlotCollectionTests.ReshapingThePortsASecondTime_AppliesTheNewSet`
（工具这一层）。**枚举那条路没变**：切走再切回来仍要复原布线，`SlotEnumeratorTests` 里原来那条用例就是
在钉它。

### 重建时的连线复原要管**两边**（2026-10-05 修）

`SetSelector` 重建分支时会把新分支接回旧分支原来的邻居，但原来只记 `Targets` —— 那是给**输出**枚举写的。
**输入**枚举的分支挂的是 `Sources`，它上面没有 `Targets`，于是重建一个输入端口集会把喂给它的连线连同旧槽
一起丢掉，**而调用照常报成功**：没有异常、没有日志，只有连线数变了。用户会话里重建 `Merge Report` 的输入口
（3 → 5）丢了三条，靠事后数连线才发现。

配对规则按「选择器类型变没变」分档：**类型变了按序号**（枚举换枚举，两边名字无关，原行为一字不改）；
**类型没变按名字**（同一 provider 的端口表被改，序号配对会把连线悄悄挪到另一个口上 —— 那是看起来正常的错图，
比丢掉更难发现）。名字在旧集合里找不到的分支就是新端口，不接任何线。

守卫三条：`SlotEnumeratorTests` 的 `RebuildingAProvidersSlots_KeepsTheLinksThatFeedThem` 与
`…_PairsBranchesByName_NotByPosition`，加上工具侧的
`SetEnumSlotCollectionTests.GrowingThePythonNodesPorts_KeepsTheLinksThatFeedThem`。

## 七、`CompilerViewModel` 的两个已知硬约束

1. **Terminal 角色的锥必须是 series-parallel。** 若独立生产者「没有在目标之前汇入同一个 join」，`CompileConeAsync` 直接抛 `InvalidOperationException`（`CompilerEx/Compile/CompilerViewModel.Reverse.cs:112`）。
2. **同一个 router 的同一个 route key 若有多条路径到达同一节点，编译报错**（AI 工具面 `CompileNodeResult` 的文档写明了这条；Core 侧对应 `CompilerViewModel.cs:303` 的 `RestrictRouteToCone` 与 `:319` 的异常）。Router 分支的存在性由 `helper.AccessAsync(CompileContext{Sender, Receiver}, ct)` 过门（`CompilerViewModel.Reverse.cs:58`）：**被拒的边不算祖先依赖**。
