# 几何工具：移动/定位/缩放与绘图不一致的那些坑

> 锚点/缩放/槽位测量的**机制**在
> [`WorkflowSystem/architecture.md`](../WorkflowSystem/architecture.md) §关于 Anchor 与
> [`WorkflowSystem/extension.md`](../WorkflowSystem/extension.md)（第 5/7 条）。

---

## 一、两条移动路径，与「不要读回来再写回去」这条铁律

| | 人类拖拽 | Agent 工具 |
|---|---|---|
| 派发的命令 | **`MoveCommand` + `Offset` 增量**（七家适配器的 `WorkflowNodeDragBehavior`，如 WPF `WorkflowSystem/AttachedBehaviors/WorkflowNodeDragBehavior.cs` 的 `OnMouseMove`） | `MoveNode` 也派发 `MoveCommand`（世界位移），**不**用 `SetAnchorCommand` + 自算绝对锚点 |
| 增量语义 | 视图空间增量，由 `StandardMove` 按 `Scale` 换算 | 无 |

**铁律：`node.Anchor` 的 getter 返回的是被画布 `Scale` 坍缩过的值**（`NodeDefaultViewModel.Anchor` 的 getter
→ `Anchor.Collapse`），而 setter 把入参**当世界坐标直接存**。所以
`new Anchor(node.Anchor.H + dx, …)` 再交给 `SetAnchorCommand` 是一个**读-改-写陷阱**：
画布不是 1:1 时落点就错，越移越偏。**缩放越大错得越多，而这正是 agent 摆几十个节点时的状态**（要看全图必然缩小）。
`Anchor.Collapse` 只缩 H/V、**保留 `Layer`**，所以读 `Layer` 是安全的。

⇒ **相对移动一律用 `MoveCommand(new Offset(dx,dy))`**：它与拖拽是同一条代码路径，缩放换算、`Layer` 保留、
`MarkDirty()` 全部自动继承。这条已由
`Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的 `MoveNode` 落实，
回归测试是 `NodeGeometryToolTests.MoveNode_LandsWhereADragWould_AtANonUnitScale`（两个节点、`Scale = 0.5`、
一个用工具移、一个直接 `MoveCommand`，断言**世界位移相等**）。

> ⚠ 工具的 `[Description]` 若写「Mirrors GUI node-drag: dispatches `SetAnchorCommand`」就与事实不符
>（拖拽从不派发 `SetAnchorCommand`）；模型是照着描述做决策的。

## 二、`SetNodePosition` 的层：省略 ≠ 置零

`layer` 参数是 `int? layer = null`（不是 `int layer = 0`），省略即**保留当前层**。默认 0 会把
`Panel.ZIndex`（模板绑 `Anchor.Layer`）每次绝对定位都清零 —— 节点堆叠顺序被静默打乱，看起来像渲染故障。

## 三、连线画在**槽位锚点**之间，而槽位锚点由视图层测量

- 连线模板直接绑 `Sender.Anchor.Horizontal` / `Receiver.Anchor.Horizontal`（
  `Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-tree-view/TemplateClass.xaml` 的 `LinkTemplate`）——
  **link 自己不存坐标、不缓存**，端点就是两个 slot 的锚点。
- slot 锚点**不是 Core 算的**，是各适配器的 `WorkflowSlotLayoutBehavior` 量出来的（用
  `WorkflowSurfaceMath` 的三个函数之一）。所以**任何程序化的几何改动都必须让平台重算**，否则节点卡片动了、线还停在旧端点。
- 重算的触发是**反应式**的：节点 `Anchor`/`Size` 的 `PropertyChanged` + 框架布局事件（WPF
  `WorkflowSlotLayoutBehavior` 的 `OnLayoutUpdated` / `OnSizeChanged` / `OnNodePropertyChanged`，监听的属性名集合由 `SlotPropertyNames` 维护，含 `"Anchor"`/`"Size"`）。
- **`RefreshSlotAnchors(node)` 就是那个「重新发一次通知」的推手**（非变更、不产生 undo，`WorkflowAgentToolkit.RefreshSlotAnchors`）。
  调用点是**槽位形状类**工具（`PatchNodeProperties` / `PatchComponentById` / `ExecuteCommandOnNode` / `ExecuteCommandById` / `SetEnumSlotChannel`）与 `MoveNode` / `SetNodePosition` / `ResizeNode`。
  **新加几何工具时也要调它** —— 漏调就是「卡片动了、线还停在旧端点」。

### 已知的洞里还剩什么（未证实）

- **MAUI 与 Razor 的重测是从指针事件里驱动的**（MAUI `WorkflowNodeDragBehavior` 的 `OnPlatformPointerMoved` / `OnPanUpdated` 调
  `WorkflowSlotLayoutBehavior.Refresh`；Razor 由 JS 的 `veloxdev-node-drag-move` 事件驱动），
  **程序化写入结构上到不了那条路**。Razor 另有 `MutationObserver` 兜底。
- 「被虚拟化掉的节点重新实体化时会不会重测」**没有找到证据**（WPF 侧有 `Loaded`/`DataContextChanged` 触发，
  看上去覆盖，但未实测）。
- 复现时先确认使用者用的是哪一家适配器 —— 不同家结论不同。

## 四、布局工具与布局引擎

`WorkflowAgentToolkit` 在 `WorkflowToolCategory.Layout` 下注册三件：`GetLayoutInfo`（读：树上的 solver 与默认 options）、
`PreviewLayout`（读：`DryRun`，返回每个落点、cost、诊断）、`ArrangeLayout`（写：走 `Tree.Arrange`，一次 undoable action 写回全部锚点）。
宿主没注册 solver 时 `GetLayoutInfo` 报 `DefaultWorkflowLayoutSolver (fallback)`。

布局引擎在 `Src/Core/VeloxDev.Core/WorkflowSystem/Layout/`：`HierarchyAlignmentStage` + `TileSeparationStage`，
经 `IWorkflowLayoutSolver` 注入；数据流与扩展点见 `memory/modules/WorkflowSystem/layout.md`。

`Resources/Workflow/{en,zh}/Skills/SmartLayout.md` 保留手工兜底（最长路径分层、barycenter、80/40 px 间隙等），
但已要求先走 `PreviewLayout` / `ArrangeLayout`；两版必须逐条对称（`AgentEmbeddedResources` 按语言取）。
