# 几何工具：移动/定位/缩放与绘图不一致的那些坑

> 起因（2026-09-27）：使用者报「Agent 自己调布局很拉跨，而且截图里连线会错位，人类手动操作没这问题」。
> 本文记的是那次追查得到的、下次还会踩的东西。锚点/缩放/槽位测量的**机制**在
> [`WorkflowSystem/architecture.md`](../WorkflowSystem/architecture.md) §关于 Anchor 与
> [`WorkflowSystem/extension.md`](../WorkflowSystem/extension.md)（第 5/7 条）—— 本文只写 agent 工具侧的后果。

---

## 一、两条移动路径，与「不要读回来再写回去」这条铁律

| | 人类拖拽 | Agent 工具 |
|---|---|---|
| 派发的命令 | **`MoveCommand` + `Offset` 增量**（七家适配器的 `WorkflowNodeDragBehavior`，如 WPF `Attached/Workflow/WorkflowNodeDragBehavior.cs:121`） | `MoveNode` 曾用 `SetAnchorCommand` + 自己算的**绝对**锚点 |
| 增量语义 | 视图空间增量，由 `StandardMove`（`WorkflowNodeEx.cs:110-112`）按 `Scale` 换算 | 无 |

**铁律：`node.Anchor` 的 getter 返回的是被画布 `Scale` 坍缩过的值**（`NodeDefaultViewModel.cs:44`
→ `Anchor.Collapse`，`Anchor.cs:58`），而 setter 把入参**当世界坐标直接存**。所以
`new Anchor(node.Anchor.H + dx, …)` 再交给 `SetAnchorCommand` 是一个**读-改-写陷阱**：
画布不是 1:1 时落点就错，越移越偏。**缩放越大错得越多，而这正是 agent 摆几十个节点时的状态**（要看全图必然缩小）。
`Anchor.Collapse` 只缩 H/V、**保留 `Layer`**，所以读 `Layer` 是安全的。

⇒ **相对移动一律用 `MoveCommand(new Offset(dx,dy))`**：它与拖拽是同一条代码路径，缩放换算、`Layer` 保留、
`MarkDirty()`（`NodeHelper.cs:98-102`）全部自动继承。这条已由
`Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的 `MoveNode` 落实，
回归测试是 `NodeGeometryToolTests.MoveNode_LandsWhereADragWould_AtANonUnitScale`（两个节点、`Scale = 0.5`、
一个用工具移、一个直接 `MoveCommand`，断言**世界位移相等**）。

> 工具的 `[Description]` 曾写着「Mirrors GUI node-drag: dispatches `SetAnchorCommand`」——**描述与事实不符**，
> 拖拽从不派发 `SetAnchorCommand`。改工具语义时顺手核一遍描述，模型是照着它做决策的。

## 二、`SetNodePosition` 的层：省略 ≠ 置零

`layer` 参数是 `int? layer = null`（不是 `int layer = 0`），省略即**保留当前层**。默认 0 会把
`Panel.ZIndex`（模板绑 `Anchor.Layer`）每次绝对定位都清零 —— 节点堆叠顺序被静默打乱，看起来像渲染故障。

## 三、连线画在**槽位锚点**之间，而槽位锚点由视图层测量

- 连线模板直接绑 `Sender.Anchor.Horizontal` / `Receiver.Anchor.Horizontal`（
  `Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-tree-view/TemplateClass.xaml:25-28`）——
  **link 自己不存坐标、不缓存**，端点就是两个 slot 的锚点。
- slot 锚点**不是 Core 算的**，是各适配器的 `WorkflowSlotLayoutBehavior` 量出来的（用
  `WorkflowSurfaceMath` 的三个函数之一）。所以**任何程序化的几何改动都必须让平台重算**，否则节点卡片动了、线还停在旧端点。
- 重算的触发是**反应式**的：节点 `Anchor`/`Size` 的 `PropertyChanged` + 框架布局事件（WPF
  `WorkflowSlotLayoutBehavior.cs:162-196`，监听的属性名集合含 `"Anchor"`/`"Size"`）。
- **`RefreshSlotAnchors(node)` 就是那个「重新发一次通知」的推手**（非变更、不产生 undo，`WorkflowAgentToolkit.cs:3149-3153`）。
  调用点是**槽位形状类**工具（`:935`/`:948`/`:1046`/`:1063`/`:1822`）与 `MoveNode` / `SetNodePosition` / `ResizeNode`。
  **新加几何工具时也要调它** —— 漏调就是「卡片动了、线还停在旧端点」。

### 已知的洞里还剩什么（未证实）

- **MAUI 与 Razor 的重测是从指针事件里驱动的**（MAUI `WorkflowNodeDragBehavior.cs:229`/`:317` 调
  `WorkflowSlotLayoutBehavior.Refresh`；Razor 由 JS 的 `veloxdev-node-drag-move` 事件驱动），
  **程序化写入结构上到不了那条路**。Razor 另有 `MutationObserver` 兜底。
- 「被虚拟化掉的节点重新实体化时会不会重测」**没有找到证据**（WPF 侧有 `Loaded`/`DataContextChanged` 触发，
  看上去覆盖，但未实测）。
- 那次报障**没有确认使用者用的是哪一家适配器** —— 不同家结论不同，这是下次复现时要先问的第一个问题。

## 四、布局：没有布局工具是**设计如此**，知识才是补强点

`WorkflowAgentToolkit.cs:182-184` 明写不做 bundled layout 工具（理由是每一步都落到单个组件命令，undo 栈不被绕过）。
`WorkflowToolCategory.Layout` 是保留位、无工具注册。Core 里也**没有布局引擎**（`CanvasLayout` 是视口偏移/缩放，不是节点排布）。

于是「排得好看」全靠提示词语料：`Resources/Workflow/{en,zh}/Skills/SmartLayout.md`（最长路径分层、barycenter 交叉最小化、
按 `Size` 算坐标、80/40 px 间隙、重叠判定公式、象限换算）。**两版必须逐条对称** —— `AgentEmbeddedResources` 按语言取文档。

2026-09-27 补的一节「宽层必须折行」（一个扇出源 → N 个处理器 → 一个汇聚，原规则会排成一列 30 个、
层高线性增长、连线横跨整段，实测见过 3197×2631 的画布（历史实测，不可复核））：一层超 8 个节点或超 ~1600 px 折成子列；共享同层的
N 个节点摆成 `ceil(sqrt(N))` 子列网格并在 Y 上对齐那个扇出源；包围盒尽量接近 3:2。
**使用者要的是「Agent 自己理解坐标系并设计低碰撞、美观的布局」，所以补的是判据与量化门槛，不是塞一个固定算法。**

**仍缺的两件**（未做）：① 缺一个**度量**工具让模型自检（包围盒/重叠对/最小间隙，现在它只能自己扒 `ListNodes` 的 JSON 算）；
② 缺**批量定位**原语（30 个节点要 30 次 `SetNodePosition`，模型得跨多轮记住 30 组坐标，设计自由度被工具粒度卡死）。
