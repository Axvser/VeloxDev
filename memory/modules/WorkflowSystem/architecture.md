# WorkflowSystem 架构

> 模块位置：`Src/Core/VeloxDev.Core/WorkflowSystem/`
> 对外接口：`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/`（17 个文件，命名空间 `VeloxDev.WorkflowSystem`）
> 平台差异见同目录 `adapters/<平台>.md`；扩展做法见 `extension.md`。

---

## 一、这个模块是什么、不是什么

**是什么**：节点图编辑器的「模型树 + 编译运行 + 画布几何/虚拟化」三层，**零 UI 依赖** ——
`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:7` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0`，整个项目只引了一个 `Microsoft.Bcl.HashCode`。

**不解决**（这几条决定了你找不到代码时该往哪看）：

| 你以为在这里 | 其实在哪 |
|---|---|
| 怎么画节点/连线 | 七家适配器 `Src/Adapters/VeloxDev.*/Attached/Workflow/`，Core 只给坐标数学 |
| 鼠标命中、拖拽、滚轮 | 适配器的 `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSurfaceBehavior` |
| 持久化格式 | 没有格式定义；靠 Newtonsoft 的 `[OnSerializing]` / `[OnDeserialized]` 钩子，见 `GUI/GeometryModels/Anchor.cs:56`、`:88` |
| 撤销栈的存储 | 栈是 `TreeHelper<T>` 的私有字段；Core 只定义「一对 Redo/Undo 委托」`WorkflowActionPair.cs:6` |
| 节点「算什么」 | 用户实现 `IWorkflowNodeViewModelHelper.ReceiveAsync`（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:92`）。默认实现返回 `null`，**什么都不往下传**（`Templates/Helpers/NodeHelper.cs:57`） |

**AI 工具面不在本模块内**：`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/`（命名空间 `VeloxDev.AI.Workflow`）是本模块的**消费方**，不是组成部分；它引用 `VeloxDev.Core`（`Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj` 的 ProjectReference/PackageReference 对）。改 Core 的契约会连带打断它，但不要把它的逻辑写进 Core。

---

## 二、四组件 + Helper：状态归谁

四个组件接口都继承 `IWorkflowViewModel`（`Interfaces/WorkflowSystem/IWorkflowViewModel.cs:9`），四个 Helper 接口都继承 `IWorkflowHelper`（`Interfaces/WorkflowSystem/IWorkflowHelper.cs:3`，只有 `Closing/CloseAsync/Closed` 三个钩子）。

**分界线是固定的：ViewModel 存状态，Helper 存行为与订阅。**

| 组件 | ViewModel 拥有（可序列化的状态） | Helper 拥有（运行时状态） | 依据 |
|---|---|---|---|
| Tree | `Layout`、`Nodes`、`Links`、`LinksMap`、`VirtualLink` | 撤销/重做栈（`TreeCache`）、`VisibleItems`、`Viewport`、`CurrentSender`、`SpatialManager` | `Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:13,17,21,25,29`；`StandardEx/WorkflowTreeEx.cs:12,655` |
| Node | `Parent`、`Anchor`、`Size`、`Slots` | `_scaleTracker`、子 Helper 的并发信号量 | `Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:13,17,21,25`；`Templates/ViewModels/NodeDefaultViewModel.cs:32` |
| Slot | `Parent`、`Channel`、`State`、`Anchor`、`Targets`、`Sources` | 无独立运行时状态（`SlotHelper<T>` 只有事件） | `Interfaces/WorkflowSystem/IWorkflowSlotViewModel.cs:13,17,21,25,29,33`；`Templates/Helpers/SlotHelper.cs:19` |
| Link | `Sender`、`Receiver`、`IsVisible` | 无 | `Interfaces/WorkflowSystem/IWorkflowLinkViewModel.cs:12,16,20`；`Templates/Helpers/LinkHelper.cs:18` |

**Helper 是「按实例挂」的，不是全局单例**。`EnsureWorkflowHelper()` 里 `if (Helper is not <具体类型>) Helper = new <具体类型>()` 是生成器写的（`Src/Generators/VeloxDev.Core.Generator/Writers/WorkflowWriter.cs:474`），所以**手写 ViewModel 必须自己在构造函数里调 `InitializeWorkflow()`**，见 `Templates/ViewModels/NodeDefaultViewModel.cs:26`。

### 唯一写者（single writer）

这几处只能有一个写者，绕过它就是 bug：

| 状态 | 唯一写者 | 说明 |
|---|---|---|
| `CanvasLayout.ActualOffset` / `ActualSize` | `CanvasLayout.Update()`（`GUI/GeometryModels/CanvasLayout.cs:93`） | 由 `OriginSize` / `PositiveOffset` / `NegativeOffset` / `Scale` 的 partial 变更钩子驱动（`:117-122`）。适配器只能写那四个，**不能直接写 Actual\*** |
| `TreeHelper.Viewport` | 适配器（`TreeHelper<T>.OnViewportChanged` 同步虚拟化，`Templates/Helpers/TreeHelper.cs:97`） | 坐标系是**画布局部** |
| `TreeHelper.VisibleItems` | `WorkflowSpatialEx.VirtualizeCore`（`GUI/Virtualization/WorkflowSpatialEx.cs:119`） | 任何地方直接改它都会让空间索引与可见集脱钩 |
| `slot.Anchor` | 适配器的 slot-layout 行为，在 render 优先级下异步测量后写入 | 见 `GUI/Rendering/WorkflowSlotUpdateGate.cs:1-16` 的长注释 |
| `VirtualLink` | `StandardSendConnection` / `StandardResetVirtualLink`（`StandardEx/WorkflowTreeEx.cs:97,173`） | `VirtualLink.Sender/Receiver` 的 Anchor 被直接赋值，不走 setter |

### 一个反直觉点：`Anchor` / `Size` 的 getter 返回的是「折叠后的临时对象」

`NodeDefaultViewModel.Anchor` 的 getter 是 `anchor.Collapse(Parent?.Layout?.Scale)`（`Templates/ViewModels/NodeDefaultViewModel.cs:40`），字段里存的是**世界坐标原值**。源码注释写得很明确：

> `Src/Core/VeloxDev.Core/WorkflowSystem/StandardEx/WorkflowNodeEx.cs`（`StandardSetAnchor` 上方）——「The Anchor/Size getters collapse toward the world origin by Layout.Scale (value / scale), so in-place mutation of the returned value would write to a discarded copy. All mutations must go through the property setters, which store the ORIGINAL world value.」

所以 `node.Anchor.Horizontal = 5;` 或者 `node.Anchor + someOffset` 里对 `node.Anchor` 的属性赋值**全部丢失**。`StandardMove` 也是按这个语义写的：`world' = (collapsed + offset) * scale`（`StandardEx/WorkflowNodeEx.cs:95`）。

---

## 三、五条管线

### 3.1 模型树 / 结构变更（含撤销）

```
调用方 → 组件命令（IVeloxCommand）→ 生成的命令体 → Helper.Xxx(...) → StandardEx.StandardXxx(...)
                                                                        ↓
                                                          TreeHelper.Submit(WorkflowActionPair)   ← 唯一入栈口
```

- 命令是生成器按约定成员名生成的（`NodeDefaultViewModel.cs:81-135` 是手写版，生成版见 `Writers/WorkflowWriter.cs`）。命令体的形状是 `if (parameter is not X) return; Helper.Y(...)`。
- `IWorkflowActionPair` 只是 `(Action Redo, Action Undo)`（`WorkflowActionPair.cs:6`）。
- 栈在 `TreeCache` 里（`ConditionalWeakTable<IWorkflowTreeViewModel, TreeCache>`，`StandardEx/WorkflowTreeEx.cs:12,655`），**按 Tree 实例隔离**。

### 3.2 连接建立（两阶段协议）

```
SendConnection(senderSlot) → 校验 sender 方向 → 按 sender 的 channel 清理旧连接
                           → 设 VirtualLink 双端 Anchor → CurrentSender = slot，State = PreviewSender
ReceiveConnection(receiverSlot) → 校验 receiver 方向 → ValidateConnection(sender, receiver)   ← 用户可覆写的唯一连接谓词
                                → 拒同节点 → 清同向冲突 → 按 receiver channel 清旧 → StandardCreateNewConnection
```

依据：`StandardEx/WorkflowTreeEx.cs:97-171`。**注意校验全在 receiver 侧**：`SendConnection` 只查 sender 自己的能力和容量。

### 3.3 编译 → 运行

```
CompilerViewModel.CompileAsync<T>(node, role, ct)          CompilerEx/Compile/CompilerViewModel.cs:37
  ├ CompileRole.Root     → CompileGraphAsync               :59
  └ CompileRole.Terminal → BuildAncestorConeAsync          CompilerViewModel.Reverse.cs:33
                          → CompileConeAsync               :82
  ↓ 产出
CompiledGraph { Entries: CompileSegment[] }                CompilerEx/Compile/Model/CompiledGraph.cs:12
  段有三种：ChainSegment / BranchSegment / ParallelSegment
  ↓ 每个节点被 AttachCompileTimeContext 注入 CompileContext { Order, ChainIndex, Offset, InputNodes }
                                                           CompilerViewModel.cs:246
  ↓
RuntimeEngine.RunAsync(graph, IRuntimeContext, ct)         CompilerEx/Runtime/RuntimeEngine.cs:21
  → RunGraphAsync → RunExecuteAsync / RunBranchAsync / RunParallelAsync
  → DriveAsync(node) → node.GetHelper().ReceiveAsync(context, ct)   :227
```

编译期的三个身份：`Order`（全局执行序，**`-1` = 绝对停止**）、`ChainIndex`（链内下标）、`Offset`（子图入口偏移）。定义在 `CompilerEx/Compile/Contracts/ICompileContext.cs`。

**Router 的静态/动态是运行期判定的**：编译器调 `router.ResolveRouteKey(null)`（`CompilerViewModel.cs:94`），返回 `null` 就是动态（`isDynamic = currentKey is null`，`:95`）。`RouterCompileMode` 枚举（`CompilerEx/Compile/Contracts/RouterCompileMode.cs:11`）**只是给用户代码/Agent 面用的标签，编译器从不读它** —— `Src/Core/VeloxDev.Core/` 下除定义外零引用；其余引用只在测试（`VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`）、demo（`Examples/Workflow/`）与 AI 工具面的 scope 声明（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:33`）。

**redirect 不是环**：编译产物一定是无环的。redirect 是运行期契约 —— `IRedirectable.ResolveRedirectAsync` 返回一个更早的 `Order`，`RuntimeEngine` 就以这个 target **重跑整张图**（`RuntimeEngine.cs:24-50`），最多 `MaxRedirects = 50`。每轮 `context.Attempt = redirects + 1`（`:40`），输出按 `Attempt` 戳 + `ActiveRedirectTarget` 前缀双重过滤（`CompilerEx/Runtime/Model/RuntimeContext.cs:147-149`）。

**汇合聚合**：`CompileContext.InputNodes.Count > 1` 时，`DriveAsync` 注入 `new GroupData(context.CollectGroupedInputs(inputs))` 作为 `Data`（`RuntimeEngine.cs:246`），它是一个只读字典 `IReadOnlyDictionary<IWorkflowNodeViewModel, object?>`（`CompilerEx/Runtime/Model/GroupData.cs:17,26`），Key = 来源 Node 的**引用身份**（比较器是 `WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>`，`GUI/Virtualization/WorkflowSpatialEx.cs:339`）。单输入时 `Data` 保持裸链式传递，`InputNodes` 为 `null`。

### 3.4 渲染（几何）

```
适配器的 slot-layout 行为测量 → 写 slot.Anchor（世界坐标）
节点视图读 node.Anchor / node.Size  ← getter 按 Layout.Scale 折叠（除以 Scale）
连线视图读两端 slot.Anchor → 首行调 link.IsRenderReady()  ← NaN 就 return
```

- **`WorkflowSurfaceMath` 是全模块唯一的坐标数学**（`GUI/Math/WorkflowSurfaceMath.cs:17`），七家适配器以前各自内联过。
- **NaN = 未测量**这个约定是渲染就绪门的全部内容。注意**只有 slot 的 anchor 默认 NaN**：`Anchor` 类本身的构造默认是 `0d`（`GUI/GeometryModels/Anchor.cs:9` 的 `Anchor(double left = 0d, double top = 0d, int layer = 0)`），是 slot 的字段给了 NaN —— 生成器版 `Writers/WorkflowWriter.cs:1038-1039`（注释：「Slot anchor defaults to NaN (no value): links don't render until both anchors are measured by the GUI」），默认实现版 `Templates/ViewModels/SlotDefaultViewModel.cs:33`。node 的 anchor 默认 `0` 或 `[DefaultAnchor]`（`Writers/WorkflowWriter.cs:772`），`VirtualLink` 双端在重置时**故意**回到 NaN 而不是原点（`StandardEx/WorkflowTreeEx.cs:177-179`）。检查在 `GUI/Rendering/WorkflowSlotUpdateGate.cs:20`，包装在 `GUI/Rendering/WorkflowLinkRenderEx.cs:27`（额外看 `IsVisible`）。**不需要任何事件订阅或时间戳** —— 测量写入真实坐标后绑定自动刷新。未挂到节点的 slot（`Parent is null`，如拖拽预览）直接算就绪（`WorkflowSlotUpdateGate.cs:28-33`）。
  > ⚠️ `WorkflowSlotUpdateGate.cs:7-8` 的 XML 注释写的是「`Anchor` 默认 horizontal/vertical 为 `double.NaN`」—— **那句话与代码不符**（`Anchor.cs:9` 是 `0d`）。它想表达的是 **slot 的约定**，不是 `Anchor` 类的默认值。按下一条行事，别按那句注释。
- **同一套 NaN 约定还贯穿空间索引**：bounds 为空/NaN 的条目会被登记用于变更跟踪但**不进网格**（`GUI/Virtualization/SpatialGridHashMap.cs:63`），端点未定位时连线对返回 `Empty` bounds（`GUI/Virtualization/NodePairBoundsProvider.cs:70`），所以未测量的节点不会以 NaN 坐标进索引。
- 坐标系约定：`SlotAnchorFromVisualCenter` = `视觉中心 − ActualOffset`（即画布局部），`SlotAnchorFromNode` = 节点锚点 + 局部偏移，`SlotAnchorFromCanvasLocal` 用于已经是画布局部坐标的情形。三个都在 `GUI/Math/WorkflowSurfaceMath.cs:195,202,213`。
- 渲染变换是 `ScaleCollapse` = `(1/scaleX, 1/scaleY, -anchorX, -anchorY)`（`WorkflowSurfaceMath.cs:360`）。`CanvasLayout.Update()` 在 `Scale < 1` 时把 `ActualSize` 放大 `1/Scale`（`CanvasLayout.cs:104-112`），否则折叠后的内容会越出画布。

### 3.5 虚拟化

```
TreeHelper.Install → tree.EnableMap(CellSize, VisibleItems)       Templates/Helpers/TreeHelper.cs:109
                   → WorkflowSpatialManager 建立空间网格            GUI/Virtualization/WorkflowSpatialManager.cs:29
TreeHelper.Viewport 写入 / MarkDirty() → 10fps MonoBehaviour tick  Templates/Helpers/TreeHelper.cs:56
                   → Virtualize(Viewport) → VisibleItems 更新       GUI/Virtualization/WorkflowSpatialEx.cs:99
                   → BroadcastVisibleItemLayout()（对每个可见节点重发 Anchor/Size）
```

- **只有 `TreeHelper(double cellSize)` 这个构造开虚拟化**，无参构造把 `useVirtualization = false`（`Templates/Helpers/TreeHelper.cs:34-46`）。「图每次都全渲染」十有八九是用了无参构造。
- 索引有两张：节点网格 `_nodeMap` 与节点对（连线）网格 `_nodePairMap`（`WorkflowSpatialManager.cs:11-12`）。连线在两端都还没被索引时进 `_pendingLinks` 暂存，节点插入后 `RetryPendingLinks()` 补挂（`:22,249`）。
- 查询是 `QueryAgentBounds(viewport, expansionDepth: 1)`（`:79`），扩张一层是为了把「刚好在视口外但连线要穿过视口」的端点也捞出来。
- 重入守卫、bounds 空/脏态、resync：`GUI/Virtualization/SpatialGridHashMap.cs:20-21,32,193`，以及 `WorkflowSpatialEx.cs:14-24` 的 `ConditionalWeakTable` 重入表。
- 视口修正 `RulerBand` → `SetVirtualizeInset`（`WorkflowSpatialEx.cs:236`）：只影响 `Virtualize` 内部的查询膨胀，**不动权威的 `Viewport`**。

### 3.6 交互（命中测试）

Core 这边**没有命中测试代码**。链路是：

```
适配器 Behavior 的 pointer 事件 → 决定命中谁 → 调组件的 IVeloxCommand（如 node.MoveCommand / slot.SendConnectionCommand）
                                → StandardEx → 改模型状态 → 属性变更通知 → 视图重绘
```

节点命令共 9 个（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:30-65`），Tree 9 个（`IWorkflowTreeViewModel.cs:34-69`），Slot 4 个（`IWorkflowSlotViewModel.cs:38-53`），Link 1 个（`IWorkflowLinkViewModel.cs:25`），另有全部组件共有的 `CloseCommand`（`IWorkflowViewModel.cs:26`）。

**这意味着「加一个新的交互手势」通常完全不改 Core** —— 在适配器里发一条已有的命令即可；只有命令语义不够时才回 Core 加 `StandardXxx`。

---

## 四、不变量

违反下面任何一条，通常**不报错**，只是静默行为错。

1. **一次变更只能入栈一次。** 每个改模型的动作必须恰好经过一个 `Submit`。`SetSelector` 内部自己提交了一个 undo pair（`SelectorEx/SlotEnumerator.cs:260`），再包一层 `Submit` 就是两层栈项、Ctrl+Z 语义崩坏。AI 工具面的 `SetEnumSlotCollection` 注释直接写明了这一点（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs`）。
2. **`Move` / `SetAnchor` / `SetSize` 按设计不可撤销**（`StandardEx/WorkflowNodeEx.cs:70,87,95`）。同理 `WorkflowAgentToolkit` 的 `MoveNode`/`SetNodePosition` 文档里点明下层 `SetAnchorCommand` 没有 undo 项。
3. **`Anchor` / `Size` 的 getter 是折叠值**（见 §二）。一切修改走 setter，setter 存世界原值。
4. **`WorkflowGuard.Fail` 在 Release 里是彻底的空操作**。它是 `[Conditional("DEBUG")]`（`WorkflowGuard.cs:19`），连同消息参数一起被编译掉。所以 `StandardSetChannel` 在 slot 没挂到树上时 `return`（`StandardEx/WorkflowSlotEx.cs:16-20`）—— Debug 抛异常，Release 静默什么都不做。**不要把 `WorkflowGuard.Fail` 当成运行时防御**。
5. **`Anchor` 默认 NaN 表示未测量**，连线在任一端点 NaN 时不得渲染（`WorkflowSlotUpdateGate.cs:20`）。
6. **`EnableMap` 必须先于 `Virtualize`。** `VirtualizeCore` 找不到空间映射抛 `ArgumentNullException`（`WorkflowSpatialEx.cs:128`）；`EnableMap` 的返回码是 `-1`（cellSize ≤ 0）/ `0`（已启用）/ `1`（成功）（`WorkflowSpatialEx.cs:48-67`），`Uninstall` 里用 `ClearMap()` 的返回值 `== 5` 做 `Debug.Fail` 兜底（`Templates/Helpers/TreeHelper.cs:126`）。
7. **`EnsureNegativeCover` 的调用时机是硬约束**：必须在写完新 `Scale` 之后、读 `ActualOffset`/`ActualSize` 之前（`GUI/Math/WorkflowSurfaceMath.cs:403` 及其上方注释）。增长单调。
8. **`ClampScrollOffset` 在正/负边缘行为不对称**（`WorkflowSurfaceMath.cs:81`）：正边缘直接返回 `max`、只把 extent 拉长；负边缘在 `extendRatio > 0` 时抬高 `NegativeOffset` 并返回增长后的量，让调用方的前向滚动抵消这次平移。
9. **`SlotEnumerator` 的 slot 属性必须写成 `[VeloxProperty] public partial T X { get; set; }`。** 生成器按 `_camelCase` 合成后备字段并在 `InitializeWorkflowCore` 里引用它，手写一个别的名字的字段会让生成的初始化代码失效（`Src/Generators/VeloxDev.Core.Generator/Writers/WorkflowWriter.cs:1383` 一带；生成规则见 `skills/veloxdev-create-workflow/references/model.md`）。
10. **`InitializeWorkflowCore` 注册 slot 时刻意绕过 `CreateSlotCommand`**，因为那时 `node.Parent == null`。手写这条路会踩到 `WorkflowNodeEx.StandardCreateSlot` 里的幂等守卫（`StandardEx/WorkflowNodeEx.cs:25`：同引用的 slot 二次派发直接 `return`）—— 守卫的存在正是为了不让「迟到的延迟派发」推进一个幽灵 undo 项，其 Undo 会把 slot 撕出来。
11. **`Channel` 的自动清理只发生在 `One*` 通道上**。`ShouldCleanupConnections` 只看 `OneTarget`/`OneSource`/`OneBoth` 位（`StandardEx/WorkflowTreeEx.cs:530-556`），`Multiple*` 从不删任何东西；「顺带清理反方向」那一条门控在 `HasFlag(OneBoth)`（两个 one 位同时存在），所以 `MultipleBoth` 不触发。
12. **两种 Slot 实现的默认 Channel 不一致**：生成器造的 slot 体默认 `MultipleBoth`（`Writers/WorkflowWriter.cs:1036`），而 `SlotDefaultViewModel` 默认 `OneBoth`（`Templates/ViewModels/SlotDefaultViewModel.cs:31`）。行为对不上 demo 时先查这里。

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
| 节点收到数据时算什么 | `Templates/Helpers/NodeHelper.cs` 的 `ReceiveAsync`（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:92` 是契约）。默认返回 `null` = 静默不往下传 |
| 一条边能不能连 | `Templates/Helpers/TreeHelper.cs` 的 `ValidateConnection`（`:235`，默认 `true`）。协议见 §3.2 |
| 用户拖出的连线用什么类型 | `TreeHelper<T>.CreateLink`（`Templates/Helpers/TreeHelper.cs:150`，默认 `LinkDefaultViewModel`） |
| 连接的容量/自动清理规则 | `Enums/Slot.cs:8` 的 `SlotChannel` + `StandardEx/WorkflowTreeEx.cs:530` 的 `ShouldCleanupConnections` |
| slot 的状态位怎么算 | `StandardEx/WorkflowSlotEx.cs:82` 的 `StandardUpdateState`（由 `Targets.Count`/`Sources.Count` 推导） |
| 改通道时旧连接怎么清 | `StandardEx/WorkflowSlotEx.cs:16` 的 `StandardSetChannel`（按**旧**通道的位决定删哪些） |
| 图怎么被切成执行段 | `CompilerEx/Compile/CompilerViewModel.cs`（正向）与 `CompilerViewModel.Reverse.cs`（逆向锥） |
| 运行期怎么走 | `CompilerEx/Runtime/RuntimeEngine.cs`（`RunGraphAsync` → `DriveAsync`） |
| 重定向 / 重试语义 | `CompilerEx/Runtime/RuntimeEngine.cs:24-50`；契约 `CompilerEx/Runtime/Contracts/IRedirectable.cs` |
| 汇合点的输入怎么聚合 | `CompilerEx/Runtime/Model/RuntimeContext.cs:127` 的 `CollectGroupedInputs`；结构 `GroupData.cs:26` |
| 画布坐标换算 | `GUI/Math/WorkflowSurfaceMath.cs`（全模块唯一数学，别在适配器里重写） |
| 缩放时画布怎么长 | `GUI/GeometryModels/CanvasLayout.cs:93` 的 `Update()` |
| 缩放后保存/加载丢东西 | `GUI/GeometryModels/Anchor.cs:56,88` 的序列化钩子（`Size.cs` 同构） |
| 什么进可见集 | `GUI/Virtualization/WorkflowSpatialEx.cs:119`；暂存与补挂 `WorkflowSpatialManager.cs:146,249` |
| 网格索引的数据结构 | `GUI/Virtualization/SpatialGridHashMap.cs`（注意 `:146` 的注释：重入时改 `Dictionary` 会毁内部状态） |
| 可见集变化后怎么让视图刷新 | `Templates/Helpers/TreeHelper.cs` 的 `BroadcastVisibleItemLayout()`（对每个可见节点重发 `Anchor`/`Size`） |
| 撤销/重做栈 | `StandardEx/WorkflowTreeEx.cs:193-260`（`StandardRedo`/`StandardSubmit`/`StandardUndo`/`StandardClearHistory`） |
| slot 数量可变的节点（选择器） | `SelectorEx/SlotEnumerator.cs`、`SelectorEx/ConditionalSlot.cs`、`Interfaces/WorkflowSystem/IConditionalSlotProvider.cs` |
| 网格装饰器 / 小地图的 Core 契约 | `Interfaces/WorkflowSystem/IWorkflowGridDecorator.cs`、`IWorkflowMinimapOverlay.cs` |

---

## 七、`CompilerViewModel` 的两个已知硬约束

1. **Terminal 角色的锥必须是 series-parallel。** 若独立生产者「没有在目标之前汇入同一个 join」，`CompileConeAsync` 直接抛 `InvalidOperationException`（`CompilerEx/Compile/CompilerViewModel.Reverse.cs:111`）。
2. **同一个 router 的同一个 route key 若有多条路径到达同一节点，编译报错**（AI 工具面 `CompileNodeResult` 的文档写明了这条；Core 侧对应 `CompilerViewModel.cs:286` 的 `RestrictRouteToCone` 与 `:303` 的异常）。Router 分支的存在性由 `helper.AccessAsync(CompileContext{Sender, Receiver}, ct)` 过门（`CompilerViewModel.Reverse.cs:57`）：**被拒的边不算祖先依赖**。
