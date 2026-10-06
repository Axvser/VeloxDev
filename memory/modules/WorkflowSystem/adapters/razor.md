# WorkflowSystem — Razor (Blazor) 适配器

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），命中归 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> 代码：`Src/Adapters/VeloxDev.Razor/Attached/Workflow/`（组件）+ `Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js`（手势与几何）。
> 契约与七角色共性见 `../extension.md` §3.9；这家没有独立 README —— **组件参数就是 API 面**，见 `Attached/Workflow/` 下各组件的 `[Parameter]`，本文不重复。
> **逐平台怎么写视图层**见 `skills/veloxdev-create-workflow/references/view-layer.md` 与
> `skills/veloxdev-create-workflow/references/gui/razor.md`，本文不重复。
>
> **一句话**：这是唯一一家把七个视图角色写成**组件**、并把一部分 DOM **交给 JS 独占**的适配器。
> 看不懂这家的地方，先问「这块几何归 C# 还是归 JS」，答案几乎总是「归 JS」。

---

## 一、契约成员在这里的对应物，以及为什么是这些

`../extension.md` §3.9 列的七个视图角色在这家**全部由组件承载**，但有三个关键差异，都是被平台逼出来的：

| 合同里的东西 | 这家的对应物 | 为什么不能照抄别家 |
|---|---|---|
| 附着属性名 / `PART_*` 命名约定（`../extension.md:201`） | **组件 + `[Parameter]` + `RenderFragment`**（`Attached/Workflow/WorkflowSurfaceBehavior.razor:14,18,31-38`） | Blazor 没有附着属性系统，也没有可附着的元素树。别家在标记里写 `behaviors:WorkflowXxx.IsEnabled="True"`，这里只能 `<WorkflowXxx IsEnabled="true">…</WorkflowXxx>` |
| 按 `x:Name` 找画布 / 找装饰器 | 直接传 `ScrollViewerId`/`CanvasId`，装饰器与小地图以 `RenderFragment` 传入（`Attached/Workflow/WorkflowSurfaceBehavior.razor:14,18,31-38`） | 服务端没有名字作用域可查；`@ref` 拿到的是组件实例，不是元素 |
| 视图池 `ViewPool.TemplateSelector` | `ViewPool.ItemTemplate` + 消费方自己 `@switch` 派发（`Attached/Workflow/ViewPool.razor.cs:22`） | 没有 DataTemplate 选择器可挂 |
| 插槽几何写入用 `SlotAnchorFrom*`（`../extension.md:204`） | **不调这三个函数**：世界坐标在 JS 里算完才回传（`wwwroot/veloxdev.workflow.js:1051-1060`），C# 只把结果写进 `slot.Anchor`（`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:83`） | 量像素这件事整个发生在浏览器里；Core 的那三个函数要的是一个**能读控件几何的宿主**，服务端没有 |

**契约之外多出来的一个东西（读这家的代码必须知道）**：`SurfaceViewportFeed`。
它是一个这家的级联值（`WorkflowSurfaceBehavior.razor.cs:493` 的 `_feed`，经
`Attached/Workflow/WorkflowSurfaceBehavior.razor:6` 的 `CascadingValue` 下发），
让标尺装饰器能在不拖动节点/连线子树的情况下重渲染（`WorkflowGridDecorator.razor.cs:97-111`）。
别家是同一个控件树里同步重绘，不需要这条旁路。

---

## 二、平台硬限制与由此产生的做法

### 1. 服务端没有元素、也没有像素 → 几何必须往返一次浏览器

**限制。** Blazor Server 的 C# 进程里，DOM 不存在。节点/插槽的**真实屏幕位置只有浏览器知道**。
别家量一个插槽锚点就是读控件几何（WPF 是
`control.TranslatePoint(new Point(w/2, h/2), coordinateHost)`，`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:344-356`），
这里必须：**JS 量 → 跨 SignalR 回传 → C# 写模型**。

**做法（三条，都是这条限制的产物）：**

- **元素带稳定 id 往返**：`WorkflowRuntimeIds` 用 `ConditionalWeakTable<object,string>` 按**引用身份**发
  `Guid`（`Attached/Workflow/WorkflowRuntimeIds.cs:17-23`），元素上写 `data-veloxdev-node-id` /
  `data-veloxdev-slot-id`，JS 靠这些属性回指同一个对象（`WorkflowSlotConnectionBehavior.razor:3`；
  JS 侧 `wwwroot/veloxdev.workflow.js:1042-1043`）。id 必须跨越重渲染稳定，所以键是**组件实例**而不是位置。
- **批量而非逐条**：插槽布局一次测量**所有** `[data-veloxdev-slot-id]` 后代，凑成一个批次回传
  （`[JSInvokable] OnSlotLayoutBatch(string[][] batch)`，`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:54-55`；
  JS 侧打包 `wwwroot/veloxdev.workflow.js:1051-1052`、`:1060`）。逐槽一次调用 = 每槽一次 SignalR 往返。
  附带好处是**没有 `SlotNames` 清单要维护**（`README.md:131-133`）。
- **测量在 JS 侧持续进行**：`ResizeObserver` + `MutationObserver` + 拖拽期 rAF 活测（`wwwroot/veloxdev.workflow.js` 的
  `initSlotLayout`），否则「拖着节点时连线跟着走」在服务端是不可能实现的。

### 2. 尺寸、网格、坐标轴、小地图视口块**归 JS 独占**（这家的分区法）

**限制。** Blazor 的重渲染是**异步**的：一次 `StateHasChanged` 渲染出来的 DOM 是**服务端记的模型状态**，
而画布尺寸早被 JS 在浏览器里改大了（边缘扩展 +800）。如果 C# 也去写画布尺寸，下一次重渲染就会
**把画布缩回去 / 把网格擦掉**。

**做法。** 明确分区，并把这个理由写进两份代码里：

- 画布宿主（`veloxdev-wf-canvas-host`）的**像素尺寸只由 JS 设置**，C# 从不写它；
  网格层与两条坐标轴是 JS 定位的独立层，**画布本身只有纯色背景**（`Attached/Workflow/WorkflowSurfaceBehavior.razor:16-19`，
  同一理由在 `WorkflowSurfaceBehavior.razor.cs:501-506` 的样式注释里再写了一遍）。
  扩展发生在 JS 内部：滚动余量到边（`onScroll` 判据 50px 内）就调 `expandCanvas`，后者把宿主宽/高各加 800
  （触发 `wwwroot/veloxdev.workflow.js:688-689`，加宽 `:632,637`）。
  JS 侧把这条分区写成了显式理由：**「async renders never write the content translate, host size, or scroll
  (those are JS-owned)」**（同文件 `:451-453`，在 `:424-427` 再写一遍作为 settle 守卫只覆盖节点几何与连线点的依据）。
- 小地图的**视口块也归 JS**（`setMinimapViewport`/`refreshMinimapViewport`），C# 只推映射关系。
- C# 侧 `OnParametersSet` 里对 `_offsetX/_offsetY` 的写入只增不减（`WorkflowSurfaceBehavior.razor.cs:525-526`），
  镜像 JS 侧的边缘扩展语义。

**这条是「别家能照抄什么」的正确答案**：别家的画布尺寸与网格由控件树/框架渲染拥有，没有这个问题；
**一家新平台如果它的渲染也是异步的（服务端或虚拟 DOM），就必须先划出「谁独占哪块 DOM」，否则会被重渲染擦掉。**

### 3. 渲染分趟且可能乱序 → 缩放闪烁，需要「手势期间所有人停手」

**限制。** 一次缩放要改的东西分布在很多条独立的投递路径上：每个节点组件自己的 `SyncPosition`、
每个卡片的重渲染、画布的 translate/scroll、连线曲线点的重同步。在浏览器看来它们是**一帧一帧陆续到达的**
——于是出现「节点先跳到新位置、画布还是旧的 translate」的中间帧，随后再跳回来。这就是**缩放闪烁**。

**做法（三层，缺一层就漏）：**

1. **`WorkflowGeometryScope`**：`OnWheelZoom` 全程持一个作用域（`WorkflowSurfaceBehavior.razor.cs:593` 的
   `using var _zoomScope = WorkflowGeometryScope.Zoom();`），所有逐节点几何写者见到
   `WorkflowGeometryScope.IsZooming` 就停手（`WorkflowNodeDragBehavior.razor.cs:94,108`）。
   它是 **`AsyncLocal<int>` 而不是 static 标志** —— 一个 Blazor Server 进程跑很多 circuit，
   `AsyncLocal` 才让它们互不可见，且计数器支持重入（`Attached/Workflow/WorkflowGeometryScope.cs:23-33`）。
2. **一次手势一个原子提交**：整个滚轮突发只算**一个枢轴**（`WorldAtViewportCenter(...)` 在循环外算一次，
   `WorkflowSurfaceBehavior.razor.cs:598`），净增量折成 `count` 次复合步（`:602`），
   且**只把最终状态推给 DOM** —— 一次 `applyZoomSurface` 同时落地 translate + scroll + 节点几何 + 连线点位
   （`:572-579` 的 XML、JS 侧 `applyZoomSurface`）。
3. **滚轮合并在 JS**：JS 把一次 SignalR 往返窗口内的多个 wheel 事件**净 delta** 累加后调一次
   `OnWheelZoom`（`wwwroot/veloxdev.workflow.js:1209` 的 `pending += e.deltaY > 0 ? -120 : 120` 与
   `:1184` 的 `invokeMethodAsync('OnWheelZoom', …)`），并带一个 **250ms 尾窗**的 settle 守卫
   （`SETTLE_TAIL_MS = 250`，`:533-555`），在最后一步之后仍逐 rAF 重断节点几何与连线点
   —— 因为每个节点的重渲染是**各自一条 SignalR 消息**，两次缩放突发在服务端重叠时，
   第一批的渲染可能晚于第二批落地（`:446-453` 的注释）。

**为什么别家不需要这个。** 别家的写入与重绘在同一帧、同一线程内同步发生，根本不会产生中间帧；
**这家是在给「异步渲染」补同步语义**，所以这套机制在别家是没有对应物的，别照抄进来。

### 4. 跨进程编组没有失败信号（与 TransitionSystem 同源）

circuit 一导航就销毁，但 JS 侧的回调、`_dotNetRef`、挂着的定时器都还在。
这家的对策是**每个组件在 `DisposeAsync` 里逐句 `try/catch`** 地关掉 JS 句柄与 `DotNetObjectReference`
（例如 `Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:95-127`：`dispose` 调用、
`_handle.DisposeAsync()`、`_dotNetRef.Dispose()`、`_module.DisposeAsync()` 各包一层空的 `catch`）——
**这是这家的标准形状，新写一个组件要照抄这段**，别家没有对应负担。

### 5. 滚轮事件的方向在 JS 侧就已经翻过号

C# 收到的是**已翻号**的 `wheelDelta`（正数 = 上滚），所以 `factor = wheelDelta > 0 ? 1 / 1.1 : 1.1`
（`WorkflowSurfaceBehavior.razor.cs:604`）——与 `../extension.md:205` 的统一方向一致。
翻号发生在 JS：`pending += e.deltaY > 0 ? -120 : 120`（`wwwroot/veloxdev.workflow.js:1209`，
浏览器下滚是正 `deltaY`，翻成负数 = 缩小），一格固定 ±120。
**这条容易在改 JS 时被反向**：`deltaY` 与 `wheelDelta` 的正方向相反，谁在哪一层翻号必须两边一致。

**视口往返（2026-10-03 接上）**：滚动是 JS 独占的，所以恢复是「C# 取、JS 滚」。`OnParametersSet` 里
`CaptureViewportRestore`（`Tree` 引用变了才算），首帧作为两个新增的可选尾参交给 `initSurface`（在
`ensureRulerReserve()` 之后、首次 `report()` 之前应用，免得第一份上报是原点），之后换树走 `scrollToPosition`。
坐标要加本家那段 ruler 超出预留的平移 —— 与 `OnSurfaceScroll` 里 effX/effY 同式。
**`scrollToPosition` 收的是像素滚动位置、不是世界坐标**，它此前的注释写反了（demo 也照错的用）。
见 [../extension.md](../extension.md) §3.9-10。

---

## 三、与其它六家的刻意背离

1. **这里是唯一一家七角色全是 `.razor` 组件的适配器，别家全是附着属性类。** 因为 Blazor 没有附着属性系统
   也没有可附着的元素树（`Attached/Workflow/*.razor` 的 `[Parameter]` 就是全部契约）。**新平台若同样没有附着属性面，
   照抄这里的「组件 + 参数 + RenderFragment」而不是照抄别家的附着属性**。
2. **这里是唯一一家由 JS 独占一部分 DOM 区域的适配器**（画布宿主尺寸、网格、坐标轴、小地图视口块，§二·2）。
   别家的画布尺寸/网格都是框架渲染的产物。
3. **这里是唯一一家需要 `WorkflowGeometryScope` 这种「手势期间让所有几何写者停手」的横切机制的适配器**（§二·3）。
   别家的同步渲染让这个问题不存在。
4. **这里是唯一一家在契约之外引出 `SurfaceViewportFeed` 级联值的适配器**，专门为了让标尺重渲染不拖动节点子树。
5. **这里是唯一一家几何写入要跨一次网络往返的适配器**，因此它的 id 机制、批量回传、JS 活测都不是「实现细节」
   而是**性能主线**（§二·1）。
6. **这里是唯一一家不调用 Core 的 `SlotAnchorFrom*` 三件套的适配器**：世界坐标在 JS 里算完才回传
   （`wwwroot/veloxdev.workflow.js:1051-1060`），C# 只把结果写进 `slot.Anchor`
   （`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:83`）。`../extension.md:204` 的「必须用三件套之一」
   这条契约在这家**没有对应物**（三件套要的是能读控件几何的宿主）。**新平台若也把测量放进 JS，
   不要为了「守契约」硬套一个 Core 函数 —— 那会把 JS 已经算好的世界坐标再换算一次。**
   代价是这家的 slot 锚点写入绕开了 Core 的坐标系收敛点，属于「已知的、有理由的例外」。

---

## 四、改这里时最容易踩的坑（带依据）

### 1. 区域设置陷阱：把 `double` 写进 CSS/SVG —— **2026-10-03 已清零，规则仍在**

这是这家最贵的一条，也是全仓库唯一有这一类 bug 的平台（别家动画产物是 CLR 值，不用格式化进字符串）。
**为什么会有这类 bug**：C# 把 `double` 格式化成 CSS 属性值，在逗号小数点的区域下写出 `translateX(28,5px)`，
浏览器直接**丢弃**这条声明，于是标尺刻度不再跟随滚动。JS 侧反过来永远是对的（ECMAScript 的 `toFixed`
恒定用 `.`），于是**解析侧也有一半**。

**做对的范本（改这类代码照这几处抄）：**
- `Attached/Workflow/WorkflowNodeDragBehavior.razor.cs:64` —— 节点位置串走 `InvariantCulture`。
- `Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:680-683` —— 推给 JS 的节点几何数组走 `InvariantCulture`。
- `Attached/Workflow/WorkflowGridDecorator.razor.cs:169-183` —— 标尺**文字**走 `InvariantCulture`
  （这个文件曾是「一半对一半错」的样本：文字对了、同一文件里算位置的几处没对；现在全对）。
- demo 侧：`Examples/Workflow/Blazor/Demo/Demo/Components/Workflow/TemplateSlotView.razor.cs:255` 的注释
  把这条规则写成了显式约定（「Razor 用当前区域写裸 double，逗号小数点会写出浏览器读不了的 SVG 属性」），
  `N()` 与各 `*Css => N(...)` 包装都走不变文化（`N()` 在同文件 `:256`）。
  2026-10-03：`TemplateLinkView.razor.cs` 的 `ToCss` 的 rgba 那行（原来漏了）也补成 `FormattableString.Invariant`
  （该文件 `:138-139`；它自己那条同义注释与 `N()` 在 `:160` 与 `:167`）。
- **连线 path 那条链（2026-10-03 全修）**：`workflow-link-view/TemplateClass.razor.cs` 与其镜像
  `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/LinkView.razor.cs` —— `ToCss` 的 rgba、
  `CanvasWidthCss`/`CanvasHeightCss`/`ThicknessCss`、以及 `BuildCurve()` 拼的 `M…C…` 全走不变文化。
  这条链尤其不能漏 `BuildCurve`：**适配器的缩放 JS 会用自己的 `Math.max`/`toFixed` 重写同一个 `d` 属性**
  （`wwwroot/veloxdev.workflow.js:311-315`），JS 恒用 `.`，两边格式一旦分叉，settle 守卫每一帧都会把值改回去，
  表现为缩放期间曲线反复闪。

**2026-10-03 清掉的 7 处（原来「仍然活着」的两张清单，现已为空）：**

- 写侧：`WorkflowGridDecorator.razor.cs` 的标尺厚度/两条 transform/刻度长度（收进一个新的 `Css(double)` 助手，
  `.razor:11,26` 那两处内联 `style` 也改走它）、`WorkflowMinimapOverlay.razor.cs` 的宽高/节点半径/视口描边宽与
  映射后的 `XCss/YCss/WCss/HCss`、`WorkflowSurfaceBehavior.razor.cs` 的 `--veloxdev-gs` 网格间距 CSS 变量、
  **`WorkflowCanvasTransformBehavior.ToCss`**（公开静态 API，消费方会直接拿来拼 `style`）。
- 解析侧：`WorkflowSlotLayoutBehavior.razor.cs:72`。原来 `double.TryParse(entry[1], out var x)` 不传 provider，
  而值是 JS 的 `toFixed(2)` 生成的、**恒定用 `.`**（`wwwroot/veloxdev.workflow.js:1051-1052`）；逗号小数点区域的
  默认风格含 `AllowThousands`，`.` 被当成**千位分隔符** ⇒ **静默量错**（不抛异常、也不是零），插槽锚点整体错位。
  现在传 `NumberStyles.Float, CultureInfo.InvariantCulture` —— 顺带把 `AllowThousands` 也去掉了。
- 模板与镜像：五个条目各自的 `ToCss` rgba 副本（`grid-decorator`/`minimap-overlay`/`node-view`/`slot-view`
  ＋`link-view`）与 `slot-view` 的 `SlotSizeCss`（进 SVG `width`/`height` 属性）。（第六个 `tree-view` 的
  `ToCss`/调色板 2026-10-04 已移入适配器 `WorkflowPresentation`，见 §六。）

**刻意保留的 1 处**：`Examples/Workflow/*/{InfoOverlay}` 的 `scale.ToString("0.00")` 是 HUD 的**显示文字**，
不进 CSS 属性、不被任何代码解析，七家各一份。它按当前区域显示「1,25」是显示层的事 —— 要统一属于另一件事。

**判据（给下一个改这里的人）**：在 Razor 适配器里搜索 `ToString("0` 与 `double.TryParse`，
每一处都要能回答「这个值会不会进 DOM / 来自 DOM」。会，就必须显式带 `InvariantCulture`。

### 2. `OnWheelZoom` 里那两步顺序不能换，`EnsureNegativeCover` 不能省

`layout.Scale` 写入 → `WorkflowSurfaceMath.EnsureNegativeCover(Tree)` → 才读 `ActualOffset`/`ActualSize`
（`Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:606-619`，理由写在 `:612-613`）。
漏掉 `EnsureNegativeCover` 的后果与别家同源：深缩放（`Scale ≲ 0.4`）下负侧内容越出固定负偏移、**连线永久截断**
（`../extension.md:93` 的 #13）。这里额外要注意的是：**JS 侧读到的 `reachW/H` 是浏览器里的可达内容宽度，
不是模型值**，夹取时要用两者的大者（`WorkflowSurfaceBehavior.razor.cs:618-619`），否则一次边缘平移过的宿主会被缩回去。

### 3. `WorkflowRuntimeIds.TryFind` 是一次**线性扫描**

`Attached/Workflow/WorkflowRuntimeIds.cs:37-44` 用 `foreach (var pair in Ids)` 在整张表里找 id。
`Ids` 是 `ConditionalWeakTable<object,string>`，**每个节点、每个插槽都在这张表里**，
而 `OnSlotLayoutBatch` 会对批次里**每一条**调一次 `TryFind`（`WorkflowSlotLayoutBehavior.razor.cs:78`）——
即 O(槽数 × 组件总数)，且发生在服务端、每次拖动都在跑。
目前没看到性能报告，**改这里之前先量**（这条是「已知形状，未量过代价」，不要当成已确认的瓶颈）。

### 4. 两处「注释与代码不符」——按代码为准

- `Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:11` 与 `:32` 的 XML 说插槽元素暴露 **`data-slot-id`**，
  实际属性是 **`data-veloxdev-slot-id`**（`Attached/Workflow/WorkflowSlotConnectionBehavior.razor:3`；
  JS 侧全部按 `data-veloxdev-slot-id` 查询，`wwwroot/veloxdev.workflow.js:293-294,994,1005,1042-1043,1110`）。
  **照注释写会拿不到任何测量结果、且不报错。**
- `Attached/Workflow/ViewPool.razor.cs:11-12` 说「Blazor 的渲染器自己做 diff/pooling，所以这只是个薄包装」，
  但同文件 `:58-65` 又做了**只在集合引用变化时才重快照**的优化 —— 这两句不矛盾，但**只看注释会以为这里可以随便
  每帧 `StateHasChanged`**：真那样做，父组件每渲染一次就 `ToArray()` 一次 O(N) 分配（注释本身承认了这条历史）。
  结论：`ItemsSource` 传引用稳定的集合并让它自己发 `CollectionChanged`。

### 5. 适配器里没有任何组件带 `_disposed` 守卫

`Attached/Workflow/` 下搜不到 `_disposed` 字段（全量 grep：只有 `WorkflowGeometryScope` 的句柄有同名私有字段，
语义无关）。于是有两条「circuit 导航走了、回调还在路上」的窗口：
- `WorkflowMinimapOverlay.razor.cs:287-327` 的 `MarkDirty` 是「取消上一个 16ms `Task.Delay` → 醒来 → `InvokeAsync(StateHasChanged)`」，
  醒来时组件可能已经 `DisposeAsync`（`:396`）走完。
- `WorkflowGridDecorator.razor.cs:104-111` 的 `OnFeedChanged` 直接 `InvokeAsync(StateHasChanged)`，
  没有「我还在树上吗」的判断。

**我没有构造过复现**（这条是形状、不是已确认故障）：若下一个 agent 在这两个地方见到
`ObjectDisposedException` 或「渲染已被释放的组件」，根因就在这，而**这是别家没有的一类坑**——
别家的窗口活到进程结束，没有「组件比进程先死」这件事。修法是在这两个异步路径上加 `_disposed` 标记。

### 6. `WorkflowCanvasTransformBehavior` 是静态助手，不是组件

`Attached/Workflow/WorkflowCanvasTransformBehavior.cs:6-9` 明说：因为 Blazor 没有元素属性系统，
它把 translate 偏移**以数据和 CSS 串两种形态**暴露出去。**它不写任何东西、也不申请任何所有权**；
真正做左右上扩展的是 surface 自己的内容层（`README.md:179-182`），所以消费方**通常不该**再自己套一次
`transform` —— 叠两层会让坐标算两次。

### 7. `_Imports.razor` 一删，本工程**所有** `.razor` 的 `@on…` 静默失效

`Src/Adapters/VeloxDev.Razor/_Imports.razor` 只做一件事：`@using Microsoft.AspNetCore.Components.Web`。
**删掉它不报错、不警告，但工程里每一个 `.razor` 的 `@on…` 都会被编成字面属性** ——
`__builder.AddAttribute(10, "@onkeydown", "OnSurfaceKeyDown")`，属性名里带着 `@`，浏览器不认识，
于是表面根一个事件都收不到：Delete / Escape / 指针路由 / 右键菜单**一起失效**，而 `@ref`、组件标签照常工作。
`@on…:preventDefault` / `:stopPropagation` 两个指令属性同样落在这里 —— 少了它，页面上的原生右键菜单也压不掉。
**这是工程级配置，与文件内容无关**：把 demo 里那个能正常绑定的 `.razor` 搬进本工程，一样变字面量。

判据是生成代码那一句：名字**不带 `@`**、值是 `EventCallback` 才对
（`AddAttribute(10, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, …))`），
`:preventDefault` 则要编成 `AddEventPreventDefaultAttribute`。看生成代码：
`dotnet build … -t:Rebuild -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=<dir>`。

---

## 五、两条历史说法的核实结论

1. **「`LateUpdate` 重渲染」仍然成立，但在 demo，不在适配器。**
   原文与订阅都在 `Examples/Workflow/Blazor/Demo/Demo/Components/Workflow/TemplateLinkView.razor.cs:239`（理由：
   「它在当帧的写入落地后才触发，渲染的是刚写下的那帧（重放时每周期都触发）」）与 `:240`
   （`effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);`）——注意**必须 `InvokeAsync`**，
   因为循环在第一个 `pacer` 缺失的宿主上跑在线程池线程（见 `TransitionSystem/adapters/razor.md` §二·2）。
   `Src/Adapters/VeloxDev.Razor/` 下**没有任何** `LateUpdate` 使用；这条是 demo 侧的用法，不是适配器的机制。
   顺带：这条 demo 里**动画对象就是那个 `.razor` 组件自身**（`Transition<TemplateLinkView>`，
   `:229` 的 `BuildFlow()`），所以它的属性写入与它的重渲染在同一对象上 —— 这在别家是常态，在这家是特例。
2. **区域设置陷阱已清零（2026-10-03）**：见 §4·1 —— 写侧 6 组位置（含 `WorkflowCanvasTransformBehavior.ToCss`
   这个**公开静态 API**）与解析侧那 1 处全部改成不变文化，模板与镜像的 `ToCss`/`SlotSizeCss` 同批修完。
   **规则**：写这类进 CSS/SVG/JS 的数字必须带 `InvariantCulture`，
   判据与漏掉的表现见 §4·1。新增/改动任何进 CSS/SVG/JS 的数字时照着那张判据自查。

---

## 六、连线交互（悬停命中 / Delete / 右键菜单）

**自 2026-10-03 起这是库与模板的能力，不再是非 Trimmed demo 专属**：连线视图只负责画出曲线并发布出去，**命中**由 Core 的共享判定承担，**高亮 / 删除**归宿主/适配器，`Blazor Trimmed` 与 Razor item template 因此同样开箱可用（判据与理由见 `memory/specifications/item-template-specifications.md` §五）。
同日的第二轮重构把**右键菜单的接线整体移进适配器组件 `WorkflowSurfaceBehavior`** —— Razor 没有附着属性，所以用「参数 + RenderFragment」对应 Avalonia 的 `LinkMenuKey` + ContextMenu 资源：**条目归宿主**（`<LinkMenu Context="link">`），**右键入口、坐标记录、定位、弹出、开合上报、Escape 全在组件里**。宿主的代码后置因此不再有任何菜单（或输入面）接线；两个 demo 与模板的 `<LinkMenu>` 条目形状一致。

**重渲染订阅与展示助手也归适配器（2026-10-04）**：Blazor 没有数据绑定自动刷新，此前 `workflow-tree-view` 的 code-behind 自己订树的节点/连线集合、树自身、虚拟连线与每个节点的 `Anchor`/`Size`，变化时 `StateHasChanged` 重渲染整棵子树。这套订阅整体搬进 `Attached/Workflow/WorkflowSurfaceBehavior.razor.cs`（`SyncTreeSubscriptions`/`SubscribeTree`/`UnsubscribeTree` + 逐节点订阅；换树按**模型实例**比对重接，`DisposeAsync` 里先摘订阅）。**为什么搬得动**：模板标记长在组件的 `ChildContent` 里（`Attached/Workflow/WorkflowSurfaceBehavior.razor:23-26` 渲染它），表面 `StateHasChanged` 会重新执行 `ChildContent`；该 RenderFragment 由宿主在渲染期创建、闭包读的是宿主的实例成员（`Tree`、`GridSpacing` 等），所以标记照常拿到新值 —— **不是**「重渲染子组件才会更新」。**证据**：Trimmed demo 的 `TreeView` code-behind 现在只剩 `[Parameter]`，拖一条线进 `tree.Links` 后连线照样出现（连线数 0→1，2026-10-04 无头 Chrome + CDP 实测）；节点拖拽同样工作。**边界**：表面重渲染不会重渲染宿主页面本身，所以「表面之外」的读（非 Trimmed demo 侧栏的节点/连接计数）仍由页面自己订 `Nodes`/`Links` 维持，那一处不是残留机制。
同时搬走的还有模板标记用到的展示助手：调色板（`MinorGridColor`/`RulerBackground`/`RulerTickColor`/`RulerDividerColor`/`NodeForegroundCss`）、`ToCss`/`HexByte`、以及默认节点标记用的 `InputSlotsOf`/`OutputSlotsOf`/`SlotNamesOf`，落在新类型 `Attached/Workflow/WorkflowPresentation.cs`（公开静态、命名空间同适配器，标记里 `@WorkflowPresentation.X` 直接调）。模板的 `TemplateClass.razor.cs`（35 行）与 Trimmed 的 `TreeView.razor.cs`（34 行）因此只剩 `[Parameter]`；`Interaction` 属性（全仓无消费者）删除。模板符号 `surfaceBackground`（`template.json` 把 `TemplateSurfaceBackground` 替成 `#1E1E1E`）保留替换目标：模板把符号直接传进表面的 `Background` 参数（`Src/Templates/VeloxDev.Razor.Templates/working/content/workflow-tree-view/TemplateClass.razor:17`）—— 与 `surfaceBorderBrush/Thickness/CornerRadius` 三个无目标参数不同。

**单主守卫（2026-10-03）由树的 `LinkRemoved` 提供，不归宿主**：菜单所指的连线一旦离开 `tree.Links`（Delete / Agent / Undo / 任何删除路径），树报 `LinkRemoved`（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:106`、`Templates/Helpers/TreeHelper.cs:121`）——**输入路由不自行解 `IsSuspended`**。Razor 这家的处理在组件内：`Attached/Workflow/WorkflowSurfaceBehavior.razor.cs` 的 `WireLinkMenu` 在订 `Input.PointerPressed` 的同一处订 `LinkRemoved`（`:368-375`），只做「是我这份菜单（`ReferenceEquals(MenuLink, link)`）就 `CloseLinkMenu()`」；`CloseLinkMenu` 照常复位 `IsSuspended`，挂起随之放开。**树只报告、宿主（这里就是组件）收自己的弹窗**。

| 事 | 落点 |
|---|---|
| 命中 | `Components/Workflow/TemplateLinkView.razor:39-42` —— 只给画线的 `<g>` 一层 `pointer-events="@HitTargetCss"`（`"stroke"`，虚拟连线 `"none"`，`TemplateLinkView.razor.cs:542`）；`<svg>` 的 `pointer-events:none` **一行未动**（`TemplateLinkView.razor:32`） |
| 悬停转发 | `Components/Workflow/TemplateLinkView.razor.cs:547-565` —— `OnPointerEnter/OnPointerExit` 把 `PointerPhase.Entered/Exited` 交给表面的 `ForwardPointerAsync`；**没有 IsSuspended 判断**（挂起由 `WorkflowInput.ApplyDefault` 在 `IsSuspended` 时直接返回挡掉：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs:203`） |
| 右键入口 | `Attached/Workflow/WorkflowSurfaceBehavior.razor:12` —— 表面根一层 `@oncontextmenu="OnSurfaceContextMenu"` + `@oncontextmenu:preventDefault="true"`；`OnSurfaceContextMenu`（同目录 `.razor.cs`）先记客户端坐标（取整成整数字符串避开区域设置），再 `ForwardPointerAsync(PointerPhase.Pressed, PointerButtonKind.Right)` 喂进输入路由。**宿主没有右键入口**（demo 的 `div.wf-canvas-wrapper` 上没有这条） |
| 弹菜单 | 组件 `WireLinkMenu` 订输入面 `Input.PointerPressed` → `ShowLinkMenu`：`e.Target` 不是连线直接返回（空白处也路由一次），否则 `MenuLink = link` 并置 `input.IsSuspended = true`（`WorkflowSurfaceBehavior.razor.cs:392-404`）。输入路由按树取用（`WorkflowInput.For`，Core 缓存），换树按实例比对重接（`:351-376`）。**宿主不再订旧的按组件事件** |
| 收菜单 | 组件 `CloseLinkMenu` 清掉 `MenuLink` 后复位 `input.IsSuspended` —— 点面板（`OnMenuPanelClick`）、点透明 backdrop、按 Escape（`OnSurfaceKeyDown`）三处都走它（`WorkflowSurfaceBehavior.razor.cs:407-414`）。**树的单主守卫也落进这里**：`WireLinkMenu` 内的 `LinkRemoved` 处理只判 `ReferenceEquals(MenuLink, link)` 后调它（`:368-375`），不另开第二条关闭路径 |
| 菜单 chrome | `Attached/Workflow/WorkflowSurfaceBehavior.razor:41-58`：透明 backdrop + 面板，类名仍是 `.wf-link-menu-backdrop` / `.wf-link-menu` / `.wf-link-menu-item`；样式随组件迁进 `Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.css`（宿主经 `App.razor` 已加载），demo `wwwroot/app.css:912-945` 保留同名基础样式与它那条红色 `.wf-link-menu-item:hover` 覆盖 |
| 条目 | 宿主传给组件的 `LinkMenu`（`RenderFragment<IWorkflowLinkViewModel>`，`WorkflowSurfaceBehavior.razor.cs` 的 `[Parameter]`）。条目拿到 `link` 直接绑命令 —— demo `Components/Pages/Workflow.razor` 的 `<button class="wf-link-menu-item" @onclick="() => link.DeleteCommand.Execute(null)">Delete</button>`；模板 `Src/Templates/VeloxDev.Razor.Templates/working/content/workflow-tree-view/TemplateClass.razor` 与 Trimmed `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/TreeView.razor` 同形。与 Avalonia `Command="{ReflectionBinding DeleteCommand}"` 同义，宿主代码后置零接线 |
| 选中视觉 | `Components/Workflow/TemplateLinkView.razor.cs:510-538`（`SelectedColor = "#FFFFFFFF"` `:512`、线宽 +1.5 `:513`、线体 alpha 0.55→0.85 `:538`；`IsLit` = `_hover` 或 `IsSelected` `:533`，管壁与彗星底色一起跟）。`IsSelected` 的来源改成组件公开的 `MenuLink`：`IsSelected="@(_surface?.MenuLink == link)"`（demo 页，表面 `@ref`）—— 指针移到菜单上后连线的本地 `_hover` 会灭，选中态得由菜单状态维持。**这是 demo 独有**；模板/Trimmed 的连线视图没有选中态 |
| 键盘焦点 | 悬停收焦点由组件做：`ForwardPointerAsync` 命中后 `_surfaceRoot.FocusAsync(preventScroll: true)`（`Attached/Workflow/WorkflowSurfaceBehavior.razor.cs`）。**`Delete` 与 `Escape` 都由表面根的 `@onkeydown`**（`.razor:10` → `OnSurfaceKeyDown`）收口；demo 连线层不再挂 `tabindex` / `@ref` / `@onkeydown`（那条路由已并入组件） |

四条结论：

1. **命中半径 = 画出来的最外圈管壁的半宽（实测 ±5.5px）**，靠浏览器原生的 `pointer-events: stroke` 拿到，**没有加任何额外的透明宽描边、也没有加元素**。静止态最外层管壁宽 = `LitThickness + 9 = 11px`（`TemplateLinkView.razor.cs:164-165`），半宽 5.5；选中态 `LitThickness = 2 + 1.5` ⇒ 12.5px，命中面随画出来的范围一起变大。实测（无头 Chrome + CDP 真手势）：沿弧长中点做法向二分，边界正好 `stroke-width: 11px` 的一半；8px / 20px 处 `elementFromPoint` 落到 `<div>`；整块 svg 盒子（2580×1252px）的左上角也落到 `<div>` ⇒ **不是包围盒**。七家一致的不是半径数值，而是「不超出画出来的范围」。
2. **`@foreach (var link in tree.Links)` 必须带 `@key="link"`**（`Components/Pages/Workflow.razor`）。没有它时删掉一条连线，DOM 会**少两个** svg —— `TemplateLinkView` 的 `CanRender` / `IsVirtual` / 端点订阅只在 `OnInitialized` 里 `Sync(Link)` 播种（`TemplateLinkView.razor.cs:570-600`），按位置复用会把这几样连同悬停状态交给**旁边那条线**；模型只少 1 条（`tree.Serialize()` 可证）。原 demo 没有任何删连线入口，所以这是个**触发不到**的潜伏 bug，被本次的删除功能踩了出来。
3. `FocusAsync` 的 **`preventScroll: true` 是必须的**：表面根和画布一样大，让它自己滚进来会把画布拽走。另外 `outline:none` 也是必须的（否则整张画布套一个巨大焦点框；选中线的白色就是焦点指示）。**焦点这一路不触发页面重渲染** —— 收焦点在组件里（`ForwardPointerAsync`），高亮是每条线自己的本地态（`TemplateLinkView.razor.cs:547-552`，订自己的 `Input.PointerEntered/Exited`），页面不再订任何旧的按组件事件。
4. **原生右键菜单被整个表面无条件压掉**：`@oncontextmenu:preventDefault="true"`（`Attached/Workflow/WorkflowSurfaceBehavior.razor:12`，在组件里）是**渲染期指令** —— 值在渲染时定死，没法按「这一次按下有没有命中连线」逐次决定，所以表面对整块画布一律 `preventDefault`，筛选只能放进事件里做（`e.Target` 不是连线才不弹，`ShowLinkMenu`）。**否决菜单在链上更靠前的一级（连线自己）**：订同一条 `Input.PointerPressed` 并置 `e.Handle.PreventDefault`；`ShowLinkMenu` 读它（`WorkflowSurfaceBehavior.razor.cs:397-398`）。

**右键入口只有表面根一条**：连线视图 `<g>` 上没有 `@oncontextmenu`，`TemplateLinkView` 也没有右键参数（`TemplateLinkView.razor:39-42` 只有 `pointer-events` / `onmouseenter` / `onmouseleave`）—— 留两条时同一次右键会被 `g` 与表面根各喂输入路由一次，输入路由对同一回按下报两回。宿主页上也没有入口（demo 的 `div.wf-canvas-wrapper`、模板/Trimmed 的外层 div），入口只此一处。

已知代价（不是缺陷，别当 bug 修）：菜单打开时那层**透明 backdrop 会吞掉画布手势**（这正是菜单该做的，`Attached/Workflow/WorkflowSurfaceBehavior.razor:41-58`），于是此时右键另一条线只是先关掉菜单。悬停 / 选中的重绘只落在**连线视图自己**（`TemplateLinkView.razor.cs:527-528,547-552`），不会整页重渲染。**demo 页为选中态保留了唯一的菜单相关代码**：一个 `_surface`（`@ref`）用来读组件的 `MenuLink`（`IsSelected="@(_surface?.MenuLink == link)"`）—— 这是「条目归宿主、接线归组件」之外的视觉读，不属于接线。

## 七、核不到的东西（写下来免得下一个人重找）

- **多 circuit（Blazor Server）下的选中/菜单状态**：`MenuLink` 现在是**组件实例字段**（`WorkflowSurfaceBehavior.razor.cs`，一个表面一份；demo 的选中态读的就是它，`IsSelected="@(_surface?.MenuLink == link)"`）。刻意不是 static，避开 Avalonia 那种进程级 static 在 Server 上串户，但只跑了单标签页。
- **触摸/笔**：CDP 只打了鼠标；`pointer-events: stroke` 本身与指针类型无关，未实测。
