# WorkflowSystem 自动布局（扇出规范化 + 分离投影，第一版）

> 负责「节点自动布局」的管道：契约在 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Layout/`，实现在 `Src/Core/VeloxDev.Core/WorkflowSystem/Layout/`，测试在 `Src/Core/VeloxDev.Core.Test/WorkflowSystem/Layout/`。
> 这一版实现两件事：**层级对齐**（`HierarchyAlignmentStage`：扇出组、扇入合流点、单子边）与**坐标后的分离约束投影**（`TileSeparationStage`）。没有 Sugiyama 分层、没有层内排序、没有正交走线。管道与数据模型已经按可替换阶段立起来，上面三种是后面往里插的阶段。

## 一、命名空间必须是 `VeloxDev.WorkflowSystem`，不能是 `.Layout`

实现目录叫 `Layout/`，但**命名空间不能带 `.Layout` 这一段**。带上的后果是适配器编译失败，而且错在别家代码里：

```
Src/Adapters/VeloxDev.MAUI/Attached/Workflow/ViewManager.cs
Src/Adapters/VeloxDev.MAUI/Attached/Workflow/ViewPool.cs
  error CS0118: “Layout”是 命名空间，但此处被当做 类型 来使用
```

MAUI 这几处的命名空间是 `VeloxDev.WorkflowSystem.AttachedBehaviors`（Core 命名空间的子命名空间），里面裸用 `Layout`（`Microsoft.Maui.Controls.Layout`）。C# 先看外层命名空间里**声明**的成员/命名空间，找到 `VeloxDev.WorkflowSystem.Layout` 就不再去看 `using` 带来的类型。同一课在 [输入别名规范](../../specifications/input-alias-specifications.md) 里已经写过一次（裸名会静默盖住平台同名类型），这里是「命名空间段」版本的复发。

⇒ 契约与实现**都用** `namespace VeloxDev.WorkflowSystem`；`Interfaces/WorkflowSystem/Layout/` 只是目录分层，不影响命名空间（同 `Interfaces/architecture.md` §一）。以后要在 `Src/Adapters/` 的祖命名空间下新建子命名空间，先搜一遍七家适配器里有没有同名裸类型。

## 二、数据流：谁读模型、谁只碰数组

```
WorkflowLayoutProblemBuilder.Build(tree, options)      ← 唯一在调用线程读模型的地方
    · node.Anchor / node.Size 是折叠值 ⇒ world = collapsed × Layout.Scale
    · ContentBounds 只并节点矩形；tree.Links 里端点不在树上的链接丢弃，自环丢弃
        ↓ 不可变快照 WorkflowLayoutProblem { Nodes, Edges, Sources }
WorkflowLayoutPipeline.Solve(problem, options, context)
    · new WorkflowLayoutState —— 可写数组 X/Y/Rank/Order/RouteX/RouteY
    · planner.CreateStages(problem, options) 依次执行
        · 默认规划器返回 [HierarchyAlignmentStage, TileSeparationStage]
    · 最后一律把 pinned 节点的 X/Y 抄回快照值（阶段动不了钉住的节点）
    · DefaultWorkflowLayoutCost.Measure
        ↓ WorkflowLayoutSolution { Placements, Cost, Diagnostics }
WorkflowLayoutApplier.Apply(problem, solution)          ← 调用线程一次性写模型
WorkflowLayoutEx.Arrange(tree, options)                 ← 组合以上三步；经 tree.GetHelper() 的 IWorkflowLayoutHost
WorkflowLayoutEx.Arrange(node, options)                 ← 从 Parent 找 tree → helper → 注册的 solver；起点路径
```

- `WorkflowLayoutProblem.Sources` 只给 applier；**阶段与 cost 都不许碰它**。快照里也不再有链接引用（`WorkflowLayoutEdge` 只有两个下标），所以求解过程完全没有回读模型的路径。
- `WorkflowLayoutProblemBuilder.Build` 会跳过世界几何非有限的节点（`NaN`/`Infinity` 的 anchor 或 size，典型是视图还没测量的节点）以及落在它们身上的边；原始下标记在 `WorkflowLayoutProblem.SkippedIndices`，`Sources` 与过滤后的 `Nodes` 仍逐位对齐，applier 的下标因此不变。
- `WorkflowLayoutState` 的 `Rank` / `Order` / `RouteX` / `RouteY` 是给后续阶段预留的数组，当前阶段不写。`Rank`/`Order` 初值 `-1`，route 初值 `NaN`。
- 阶段只看 `Options` 与快照；并行时只读自己的 band、写自己的节点。

## 三、默认两段：先对齐层级，再解冲突

默认规划器 `DefaultWorkflowLayoutPlanner.CreateStages` 返回 `[HierarchyAlignmentStage, TileSeparationStage]`。

### 3.1 `HierarchyAlignmentStage`：写规范位置

自顶向下按拓扑序处理，**父节点先定稿，子节点再对齐**。三条规则，优先级从高到低：

1. **扇出组**（父节点有 2 个以上子节点）：子列左边缘 = 父右边缘 + `ColumnGap`；整组以父的垂直中心为中心，按子高 + `RowGap` 堆叠。子序取**快照出边顺序**，不取当前 Y。
2. **扇入合流点**（节点有 2 个以上父节点）：垂直中心 = 父组**整体包围盒中心**（`min top` 与 `max bottom` 的中点），不是各父中心值的平均 —— 高度不一时前者才是视觉中心线；左边缘 = 最右父节点的右边缘 + `ColumnGap`。
3. **单子边**（节点恰好 1 个父节点）：垂直中心 = 父节点的垂直中心；左边缘 = 父右边缘 + `ColumnGap`。

规则细节：

- **扇出优先**：属于「父节点有 2 个以上子节点」的节点不再按自己的父节点重新居中；否则扇出组会被逐个拉回父中心、堆成一坨。
- **容差**：`PositionTolerance = 0` 时总是写目标；大于 `0` 时，扇出组全员在容差内且保持最小间距就整组不动，单子/扇入节点的中心偏差不超过容差就不动。
- **pinned**：扇出组里任一子节点 pinned，整组跳过（固定成员凑不出规范中心），交给分离段；单子/扇入节点自身 pinned 则不写。
- **X 也走规范列**：扇入与单子规则同时写左边缘，目标分别是「最右父右边缘 + `ColumnGap`」与「父右边缘 + `ColumnGap`」。X/Y 的容差**按轴独立**判断：一轴在容差内只保留那一轴，另一轴仍可对齐。
- 对齐在分离段之前跑；对齐产生的重叠由分离段兜底。

### 3.2 `TileSeparationStage`：只解冲突

1. **切块**：以快照 `ContentBounds` 左上角为原点、`Options.TileSize`（默认 `256`）为边长建 `LayoutTileIndex`（复用 `CellKey`）。节点按**中心**归唯一格；邻域查询用 `QueryHalo(rect, halo: 1)`。
2. **band 是并行单位**：列带 = 中心格 `CellKey.X` 相同的一组节点；行带 = `CellKey.Y` 相同。`ParallelThreshold`（默认 `64`）按**带数**计；串行与并行写的是不相交节点，结果逐位相同。
3. **垂向**：每个列带按 Y 从上到下，**从节点自己的当前 Y 起步**（没有额外的垂向对齐目标）：
   - 只和**水平区间相交**的已放置邻居拉开 `RowGap`；
   - 下方的钉住节点当**天花板**：能塞就上移避开，塞不下就保留冲突（钉住的不许动，见 §八）；
   - 当前是否硬违规由 band 内邻居判定，硬违规**无视策略**必须移动。
4. **水平**：每个行带按 X 从左到右，**从节点自己的当前 X 起步**（没有按父右边缘对齐的目标），只和**垂直区间相交**的已放置邻居拉开 `ColumnGap`；钉住/硬违规判断同垂向。
5. **重复**：`MaxSweeps`（默认 `8`，小于 1 当 1）轮「垂向 + 水平」；每轮结束比较数组最大位移，`≤ 1e-6` 就停。两轮之间重建 tile 索引与 band（位置变了）。
6. **不夹取**：两个方向都允许越出原内容包围盒；`DefaultWorkflowLayoutCost` 的 `Bounds` 是解后矩形的并集，可能比 `problem.ContentBounds` 更大或更负。
7. **全局硬重叠补救**：两次扫描都是 band 局部的 —— 中心既不在同一列带、也不在同一行带的两个重叠矩形会被两边一起漏掉。sweeps 结束后 `ResolveGlobalOverlaps` 用 tile 索引收集正面积重叠对，选确定性 mover（一方 pinned 就动另一方；两枚都 pinned 则保留并报诊断），沿右/左/下/上四个候选里**绝对位移最小**的轴推到满足 `Row/ColumnGap`。硬违规不看 `PositionTolerance`，pinned 永不为 mover；迭代上限 `max(4, MaxSweeps)`，修复对数与轮数写进 diagnostics（"Global overlap repair …"）。

数字都在 `WorkflowLayoutOptions` 的默认值里（`TileSize=256`、`ColumnGap=64`、`RowGap=32`、`MaxSweeps=8`、`ParallelThreshold=64`、`PositionTolerance=8`），改数字前先看测试里的显式值。

**从指定节点开始（2026-10-30）**：`Options.StartNode` 非空时，快照只把从它沿 `Traversal`（Downstream/Upstream/Both）可达、`MaxDepth` 内（0 = 无限）的节点当作**可动集**；其余有限节点仍进 problem，但一律 `IsPinned = true`，只当障碍、绝不写。起点默认 `PinStartNode = true`，所以布局是从这个点向外长，而不是把整簇平移。只有两端都在可动集内的边才进 problem；指向集外的边不进，所以对齐段也看不到它们。`StartNode` 必须属于树且几何有限，否则 builder 抛 `ArgumentException`。Demo 的节点右键菜单「自动布局 → 从该节点整理（下游/上游/双向）」就是这个入口。

## 四、尊重原位置 = 策略对象，不是阶段里的 if

- `IWorkflowLayoutPositionPolicy` 由 `TileSeparationStage` 构造函数注入；默认 `TolerancePositionPolicy`。**`HierarchyAlignmentStage` 不经过策略**：它的容差判断在阶段内部，直接写节点的 X/Y。
- 默认策略：
  - `IsPinned` ⇒ 永不移动；
  - `HasHardViolation` ⇒ 一定移动（阶段也会无视策略强制移动，策略不能把硬违规留下来）；
  - `Tolerance <= 0` ⇒ 分离段只修硬违规，不因分离链的推动移动合规节点；
  - 否则 `AxisDistance > Tolerance` 才移动。
- 语义：`PositionTolerance = 0` 是**严格规范位置** —— 扇出组总写到计算位置（扇出段自己判定），分离段只修硬违规；值越大越尊重用户原位置（扇出组全员在容差内就不动，分离链的推动超过容差才执行）。**不要**把 `0` 理解成「哪都不动」：扇出段仍会写。
- 分离段里的软位移只来自**分离链的推动**；没有远距离软目标，因此不存在「被几千单位外的建议拖走」的问题。硬违规永远不过策略。

## 五、应用与撤销

`WorkflowLayoutApplier`：
- 跳过 `IsPinned` 与 `Placement.Moved == false` 的节点；
- 用 `StandardSetAnchor` 写**世界坐标**（`new Anchor(placement.X, placement.Y, node.Layer)`，图层保留）；
- 全部移动包成**一个** `WorkflowActionPair` 走 `tree.GetHelper().Submit(...)`，Undo 一次恢复全部；`Redo` 同理重放全部；
- 结束后只 `MarkDirty()` **一次**；
- 没有任何移动时不 `Submit`（不制造空 undo 项）。
- **快照之后模型被改过就跳过**：`WorkflowLayoutApplier.Apply` 对每个要移动的节点核对「当前世界 anchor（getter × 当前 scale）、layer、父树」是否仍等于快照；不等就跳过，计入 `WorkflowLayoutResult.SkippedCount`，绝不拿旧 `From` 覆盖。因此 Solve 与 Apply 之间可以有并发编辑；仍建议同一线程紧接执行。
- `Options.DryRun` 只由 applier 认：返回 `Applied=false`，一个锚点都不写。

## 六、代价：只读 solved 几何，网格粗筛

- `DefaultWorkflowLayoutCost` **不读任何已发布曲线**。每条边用解后的节点矩形中心连成一条线段（`CentreLine`），交叉数就是这些 solved 线段之间的真交叉；节点被布局移动后计数随之变化。`CrossingsApproximate` 因此为 `true`（它描述的是「中心线近似」，不是「视图曲线」）——这是本版的明确口径，不是 bug。
- 重叠与交叉都用临时 `LayoutTileIndex` 粗筛：重叠查节点矩形所在的格（正面积重叠必共享至少一格）；交叉把每条 solved 线段的 AABB 入格、查询同格 + 1 格邻格，只对候选对做精确判交。**没有 O(n²) 全对，也没有曲线段两两比较。**
- `WorkflowLayoutOptions.MeasureCost`（默认 `true`）可整体关掉重叠/交叉计数；位移与内容包围盒仍线性计算并始终返回，`WorkflowLayoutCostBreakdown.CostMeasured` 区分「没有量」与「量了是 0」。
- 共享端点的边对不算交叉；只认**真交叉**（端点相接、共线重叠都不算）。
- 交叉数**不参与任何位置决策**；这一版的两个阶段都不读 cost。

## 七、扩展路径

| 要加的东西 | 官方做法 | 别走的路 |
|---|---|---|
| 新阶段（断环/分层/排序/走线） | 实现 `IWorkflowLayoutStage`，让 `IWorkflowLayoutPlanner.CreateStages` 按问题返回它（默认规划器返回 `[HierarchyAlignmentStage, TileSeparationStage]`）；阶段写自己的数组（`Rank`/`Order`/`RouteX`/`RouteY` 已备） | 把分层/排序塞进 `TileSeparationStage`；在阶段里读模型或 `Sources` |
| 改对齐规则（列距/换行/子序/合流中心） | 换 `HierarchyAlignmentStage`：在规划器里用自定义实现替换它，或给它加选项；子序固定取快照出边顺序，扇入取父组包围盒中心 | 在 `TileSeparationStage` 里重新引入「父右边缘」或「父中心」对齐；那会把规范位置与冲突修复混成一个阶段 |
| **给单个编辑器换整套方案** | 让 helper 实现/使用 `IWorkflowLayoutHost`，把定制实现赋给 `tree.GetHelper()` 的 `LayoutSolver`；默认是 `DefaultWorkflowLayoutSolver`，也可只换它的 planner/cost | 在 `node.Arrange` 或 `WorkflowLayoutEx` 里按平台/全局分支；那会把规则从树级变成全局 |
| 新位置策略 | 实现 `IWorkflowLayoutPositionPolicy`，传给 `TileSeparationStage` / `DefaultWorkflowLayoutPlanner` | 改 `TileSeparationStage` 里的判断；把 tolerance 写死在阶段里 |
| 新代价函数 | 实现 `IWorkflowLayoutCost`，传给 `WorkflowLayoutPipeline` | 让 cost 反向改 `state` 的位置 |
| 换整套规划 | 换 `IWorkflowLayoutPlanner` | 在 `WorkflowLayoutPipeline` 里加 `if` 分支 |
| Demo 入口 | 七家全量 Demo 的**节点右键菜单**：`自动布局 → 从该节点整理（下游/上游/双向）`，另有 `布局容差`（0/8/32）。标签/预设集中在 Lib 的 `WorkflowLayoutDemo`；WinUI 用 `RightTapped`，WPF 用右键抬起，Avalonia / MAUI / WinForms / Jalium 用各自 context menu，Blazor 用 `@oncontextmenu` | 把容差/触发时机写进 Core 阶段 |
| Agent 工具 | Core.Extension 的 `WorkflowAgentToolkit` 在 `WorkflowToolCategory.Layout` 下注册 `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout`；都走 `Tree.Arrange`，使用树 helper 上注册的 `IWorkflowLayoutSolver`；`PreviewLayout` 用 `DryRun`，`ArrangeLayout` 一次 undoable 写入 | 在 toolkit 里另写布局算法；那会让 Agent 与 GUI 看到不同的解 |

`WorkflowLayoutPipeline` 是唯一把阶段串起来的地方；它不做任何布局决策，只负责 `pinned` 收尾与 cost 汇总。

## 八、坑

- **并行确定性靠「本轮内不读别的 band」**：分离段每次扫描只读本带已放置的节点，不再有跨带的软目标；把某个跨 band 的值读进循环（例如父节点的当前位置）会让串行/并行分叉。`HierarchyAlignmentStage` 整场单线程、按拓扑序，和 band 并行无关。
- **对齐段不经过策略**：`HierarchyAlignmentStage` 自己判断容差并直接写位置；`TolerancePositionPolicy` 只约束分离段。期望「策略拦住对齐」是错的 —— 自定义策略无法阻止规范位置。
- **组里有 pinned 子就整组跳过**：不要只排除那个 pinned 子、再按规范中心排其余子节点；那样组心会偏移，且下次运行结果不一致。
- **单子/扇入也写 X**：目标分别是最右父右边缘 + `ColumnGap`、父右边缘 + `ColumnGap`。不要把它改回「只读父节点当前 X」或「只改 Y」—— 那会让合流点叠在父列里，随后被分离段往下推开，视觉上又回到不居中。
- **扇出优先于扇入/单子**：一个节点若同时是扇出组的孩子和多个父节点的合流点，按扇出组的位置走，不再按父组包围盒重新居中。改这个优先级会同时改掉多个已有用例。
- **硬违规判定是 band 局部的**：垂向只把同列带、水平区间相交的邻居算作违规；跨列的冲突由水平扫的一轮处理。把邻域放宽到所有 halo 邻居会让节点为了「本轴修不好」的冲突而移动，违反「无硬违规不许动」。
- **钉住节点在下方的处理**：`SweepVertical` 把下方的 pinned 节点当 ceiling，能上移就上移；如果 `floor > ceiling`（两枚钉住节点之间塞不下），保留重叠是**有意**的 —— 钉住节点永不移动，不要用「修 bug」的方式去动它。`SweepHorizontal` 同理。
- **`WorkflowBounds.IsEmpty` 是「宽或高 ≤ 0」**：`WorkflowLayoutProblem.ContentBounds` 用 min/max 计算，零面积内容也会得到 `IsEmpty=true`；`TileSeparationStage` 的 tile 原点因此回退到 `(0,0)`。不要拿 `IsEmpty` 当「没有节点」的判断，那用 `problem.IsEmpty`。
- **tile 索引每次扫都重建**：位置在扫内会变，索引不能跨轮复用；`LayoutTileIndex` 只服务一次 build-then-query。
- **band 局部硬判定有盲区，靠全局修复补**：垂向只认同列带、水平只认同行带；中心同时跨列、跨行的两个矩形两边都看不到。`ResolveGlobalOverlaps` 是这条不变量的唯一兜底，不要因为「两次扫描都做了硬违规」就删掉它。
- **全局修复必须单线程且确定性**：mover 固定「有 pin 动另一边，否则下标大的」；轴向固定 右→左→下→上 的平局顺序。把它并行化会让同一次 Solve 在不同调度下得到不同位置。
- **两枚 pinned 的重叠是无解的**：全局修复保留它并在诊断里写出 "two pinned nodes"，不要为了消重叠去动 pin。密集重叠可能要多轮；迭代上限是 `max(4, MaxSweeps)`，别把 1 轮当够用。
- **applier 的快照核对是保护，不是兜底**：`WorkflowLayoutResult.SkippedCount` 非 0 意味着 Solve 与 Apply 之间有人改了模型；宿主应把这当成「这次结果没完全落地」，而不是静默当成成功。
- **非有限几何必须在 builder 里过滤，不能在阶段里修**：`NaN` 坐标进了 tile 索引或 band 会把整组结果污染成 `NaN`，`WorkflowBounds` 的比较也随之失真。过滤在 `WorkflowLayoutProblemBuilder.Build`；被跳过节点的原始下标在 `SkippedIndices`，`Sources` 是过滤后的列表 —— applier 只能用过滤后的下标，别拿原始 `tree.Nodes` 下标去索引。边只要端点在跳过集合里就一并丢弃。
- **`MeasureCost = false` 不是「零代价」**：此时 `CostMeasured = false` 且 `Crossings = 0` 是刻意的，不能把 0 当成「零交叉」。默认开启的代价走网格；`LargeGraph_CostStaysLocalAndOverlapIsZero`（500 节点 / 200 边）是量级守卫。
- **不要为了「更准」把 `ILinkHitTestable.Curve` 读回 cost**：曲线是布局前视图发布的旧几何，读它等于拿旧答案冒充 solved；`WorkflowLayoutPipeline.Solve` 承诺只读快照。若以后确实需要输入基线，在 builder 的调用线程一次性快照并单独命名，不能复用 `Crossings`。
- **`LayoutTileIndex` 也被 cost 复用**：它按矩形入格，零宽/零高的线段 AABB 也能入格（`max(min + 1, ceil(...))`），所以中心线近似不需要第二套线段网格；但索引只服务一趟 build-then-query，位置变了就重建。
- **tolerance 与硬违规的次序**：阶段先算 `hard`，再问策略；即使自定义策略返回 `false`，`hard` 仍会强制移动。策略只决定「软对齐要不要做」，不能决定「硬违规要不要修」。
- **applier 的世界帧**：`StandardSetAnchor` 收世界坐标，快照 `problem.Nodes[i].X/Y` 也是世界坐标；不要在 applier 里再乘 `Layout.Scale`（那是 `StandardMove` 的语义，不是这里）。

## 九、测试锚点

- `Src/Core/VeloxDev.Core.Test/WorkflowSystem/Layout/SeparationProjectionTests.cs`：重叠修复、硬违规无视 tolerance、钉住不动、扇出组中心对齐父、负坐标/越界生长、缩放下的世界帧往返、跨列跨行的全局重叠修复、两枚 pinned 的无解用例。
- `.../PositionToleranceTests.cs`：容忍内整组不动、超出才对齐、`0` 严格写规范位置。
- `.../FanOutLayoutTests.cs`：扇出规范位置不依赖初始摆位、容差内整组不动、组内 pinned 整组跳过。
- `.../HierarchyAlignmentTests.cs`：单子边居中并放到父右侧、链式传递列与中心、扇入取父组包围盒中心（含不等高）并放到最右父右侧、扇出后再扇入仍对齐组中心与列、容差内保留、容差按轴独立、pinned 不动。
- `.../ScopedLayoutTests.cs`：从起点只动下游、上游/下游边界、未选节点作为固定障碍、起点固定、外来起点被拒绝、`node.Arrange` 走 helper 上注册的 solver、脱离树的节点抛 `ArgumentException`。
- `.../LayoutPipelineTests.cs`：串行/并行逐位一致、`DryRun` 不写模型、一次 Undo 恢复全部、交叉数随 solved 位置变化且忽略已发布曲线（该用例把所有节点 pin 住，避免对齐段改变几何）、非有限几何被跳过且 `Sources` 下标对齐、500 节点 / 200 边的大图代价走网格、Apply 跳过 Solve 后被并发改动的节点。
- 建树用 `TreeDefaultViewModel` + `NodeDefaultViewModel`/`SlotDefaultViewModel`/`LinkDefaultViewModel`（`LayoutTestKit`），不依赖任何适配器。
