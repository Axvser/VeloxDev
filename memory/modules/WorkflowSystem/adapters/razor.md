# WorkflowSystem — Razor (Blazor) 适配器

> 代码：`Src/Adapters/VeloxDev.Razor/Attached/Workflow/`（组件）+ `Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js`（手势与几何）。
> 契约与七角色共性见 `../extension.md` §3.9；组件参数表见 `Src/Adapters/VeloxDev.Razor/README.md`（那是 API 面，本文不重复）。
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
| 附着属性名 / `PART_*` 命名约定（`../extension.md:152`） | **组件 + `[Parameter]` + `RenderFragment`**（`Src/Adapters/VeloxDev.Razor/README.md:5-15`） | Blazor 没有附着属性系统，也没有可附着的元素树。别家在标记里写 `behaviors:WorkflowXxx.IsEnabled="True"`，这里只能 `<WorkflowXxx IsEnabled="true">…</WorkflowXxx>` |
| 按 `x:Name` 找画布 / 找装饰器 | 直接传 `ScrollViewerId`/`CanvasId`，装饰器与小地图以 `RenderFragment` 传入（`Attached/Workflow/WorkflowSurfaceBehavior.razor:23-30`） | 服务端没有名字作用域可查；`@ref` 拿到的是组件实例，不是元素 |
| 视图池 `ViewPool.TemplateSelector` | `ViewPool.ItemTemplate` + 消费方自己 `@switch` 派发（`README.md:15`） | 没有 DataTemplate 选择器可挂 |
| 插槽几何写入用 `SlotAnchorFrom*`（`../extension.md:155`） | **不调这三个函数**：世界坐标在 JS 里算完才回传（`wwwroot/veloxdev.workflow.js:1019-1024`），C# 只把结果写进 `slot.Anchor`（`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:80`） | 量像素这件事整个发生在浏览器里；Core 的那三个函数要的是一个**能读控件几何的宿主**，服务端没有 |

**契约之外多出来的一个东西（读这家的代码必须知道）**：`SurfaceViewportFeed`。
它是一个这家的级联值（`WorkflowSurfaceBehavior.razor.cs:111` 的 `_feed`，经
`Attached/Workflow/WorkflowSurfaceBehavior.razor:3` 的 `CascadingValue` 下发），
让标尺装饰器能在不拖动节点/连线子树的情况下重渲染（`WorkflowGridDecorator.razor.cs:98-112`）。
别家是同一个控件树里同步重绘，不需要这条旁路。

---

## 二、平台硬限制与由此产生的做法

### 1. 服务端没有元素、也没有像素 → 几何必须往返一次浏览器

**限制。** Blazor Server 的 C# 进程里，DOM 不存在。节点/插槽的**真实屏幕位置只有浏览器知道**。
别家量一个插槽锚点就是读控件几何（WPF 是
`control.TranslatePoint(new Point(w/2, h/2), coordinateHost)`，`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:350-362`），
这里必须：**JS 量 → 跨 SignalR 回传 → C# 写模型**。

**做法（三条，都是这条限制的产物）：**

- **元素带稳定 id 往返**：`WorkflowRuntimeIds` 用 `ConditionalWeakTable<object,string>` 按**引用身份**发
  `Guid`（`Attached/Workflow/WorkflowRuntimeIds.cs:17-23`），元素上写 `data-veloxdev-node-id` /
  `data-veloxdev-slot-id`，JS 靠这些属性回指同一个对象（`WorkflowSlotConnectionBehavior.razor:3`；
  JS 侧 `wwwroot/veloxdev.workflow.js:1014-1015`）。id 必须跨越重渲染稳定，所以键是**组件实例**而不是位置。
- **批量而非逐条**：插槽布局一次测量**所有** `[data-veloxdev-slot-id]` 后代，凑成一个批次回传
  （`[JSInvokable] OnSlotLayoutBatch(string[][] batch)`，`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:53-54`；
  JS 侧打包 `wwwroot/veloxdev.workflow.js:1023-1024`、`:1032`）。逐槽一次调用 = 每槽一次 SignalR 往返。
  附带好处是**没有 `SlotNames` 清单要维护**（`README.md:126-128`）。
- **测量在 JS 侧持续进行**：`ResizeObserver` + `MutationObserver` + 拖拽期 rAF 活测（`wwwroot/veloxdev.workflow.js` 的
  `initSlotLayout`），否则「拖着节点时连线跟着走」在服务端是不可能实现的。

### 2. 尺寸、网格、坐标轴、小地图视口块**归 JS 独占**（这家的分区法）

**限制。** Blazor 的重渲染是**异步**的：一次 `StateHasChanged` 渲染出来的 DOM 是**服务端记的模型状态**，
而画布尺寸早被 JS 在浏览器里改大了（边缘扩展 +800）。如果 C# 也去写画布尺寸，下一次重渲染就会
**把画布缩回去 / 把网格擦掉**。

**做法。** 明确分区，并把这个理由写进两份代码里：

- 画布宿主（`veloxdev-wf-canvas-host`）的**像素尺寸只由 JS 设置**，C# 从不写它；
  网格层与两条坐标轴是 JS 定位的独立层，**画布本身只有纯色背景**（`Attached/Workflow/WorkflowSurfaceBehavior.razor:6-13`，
  同一理由在 `WorkflowSurfaceBehavior.razor.cs:116-121` 的样式注释里再写了一遍）。
  扩展发生在 JS 内部：滚动余量小于 50px 时把宿主宽/高各加 800
  （`wwwroot/veloxdev.workflow.js:680,682` → `:611,616`）。
  JS 侧把这条分区写成了显式理由：**「async renders never write the content translate, host size, or scroll
  (those are JS-owned)」**（同文件 `:404-405`，在 `:430-432` 再写一遍作为 settle 守卫只覆盖节点几何与连线点的依据）。
- 小地图的**视口块也归 JS**（`setMinimapViewport`/`refreshMinimapViewport`），C# 只推映射关系。
- C# 侧 `OnParametersSet` 只做**单调增长**的标尺保留区 `_offsetX/_offsetY`（`WorkflowSurfaceBehavior.razor.cs:134-151`），
  镜像 JS 侧的边缘扩展语义。

**这条是「别家能照抄什么」的正确答案**：别家的画布尺寸与网格由控件树/框架渲染拥有，没有这个问题；
**一家新平台如果它的渲染也是异步的（服务端或虚拟 DOM），就必须先划出「谁独占哪块 DOM」，否则会被重渲染擦掉。**

### 3. 渲染分趟且可能乱序 → 缩放闪烁，需要「手势期间所有人停手」

**限制。** 一次缩放要改的东西分布在很多条独立的投递路径上：每个节点组件自己的 `SyncPosition`、
每个卡片的重渲染、画布的 translate/scroll、连线折线点的重同步。在浏览器看来它们是**一帧一帧陆续到达的**
——于是出现「节点先跳到新位置、画布还是旧的 translate」的中间帧，随后再跳回来。这就是**缩放闪烁**。

**做法（三层，缺一层就漏）：**

1. **`WorkflowGeometryScope`**：`OnWheelZoom` 全程持一个作用域（`WorkflowSurfaceBehavior.razor.cs:199` 的
   `using var _zoomScope = WorkflowGeometryScope.Zoom();`），所有逐节点几何写者见到
   `WorkflowGeometryScope.IsZooming` 就停手（`WorkflowNodeDragBehavior.razor.cs:98,115`）。
   它是 **`AsyncLocal<int>` 而不是 static 标志** —— 一个 Blazor Server 进程跑很多 circuit，
   `AsyncLocal` 才让它们互不可见，且计数器支持重入（`Attached/Workflow/WorkflowGeometryScope.cs:6-33`）。
2. **一次手势一个原子提交**：整个滚轮突发只算**一个枢轴**（`WorldAtViewportCenter(...)` 在循环外算一次，
   `WorkflowSurfaceBehavior.razor.cs:209`），净增量折成 `count` 次复合步（`:212-213`），
   且**只把最终状态推给 DOM** —— 一次 `applyZoomSurface` 同时落地 translate + scroll + 节点几何 + 连线点位
   （`:180-181` 的 XML、JS 侧 `applyZoomSurface`）。
3. **滚轮合并在 JS**：JS 把一次 SignalR 往返窗口内的多个 wheel 事件**净 delta** 累加后调一次
   `OnWheelZoom`（`wwwroot/veloxdev.workflow.js:1181` 的 `pending += e.deltaY > 0 ? -120 : 120` 与
   `:1156` 的 `invokeMethodAsync('OnWheelZoom', …)`），并带一个 **250ms 尾窗**的 settle 守卫
   （`SETTLE_TAIL_MS = 250`，`:512-526`），在最后一步之后仍逐 rAF 重断节点几何与连线点
   —— 因为每个节点的重渲染是**各自一条 SignalR 消息**，两次缩放突发在服务端重叠时，
   第一批的渲染可能晚于第二批落地（`:425-433` 的注释）。

**为什么别家不需要这个。** 别家的写入与重绘在同一帧、同一线程内同步发生，根本不会产生中间帧；
**这家是在给「异步渲染」补同步语义**，所以这套机制在别家是没有对应物的，别照抄进来。

### 4. 跨进程编组没有失败信号（与 TransitionSystem 同源）

circuit 一导航就销毁，但 JS 侧的回调、`_dotNetRef`、挂着的定时器都还在。
这家的对策是**每个组件在 `DisposeAsync` 里逐句 `try/catch`** 地关掉 JS 句柄与 `DotNetObjectReference`
（例如 `Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:94-126`：`dispose` 调用、
`_handle.DisposeAsync()`、`_dotNetRef.Dispose()`、`_module.DisposeAsync()` 各包一层空的 `catch`）——
**这是这家的标准形状，新写一个组件要照抄这段**，别家没有对应负担。

### 5. 滚轮事件的方向在 JS 侧就已经翻过号

C# 收到的是**已翻号**的 `wheelDelta`（正数 = 上滚），所以 `factor = wheelDelta > 0 ? 1 / 1.1 : 1.1`
（`WorkflowSurfaceBehavior.razor.cs:214-216`）——与 `../extension.md:156` 的统一方向一致。
翻号发生在 JS：`pending += e.deltaY > 0 ? -120 : 120`（`wwwroot/veloxdev.workflow.js:1181`，
浏览器下滚是正 `deltaY`，翻成负数 = 缩小），一格固定 ±120。
**这条容易在改 JS 时被反向**：`deltaY` 与 `wheelDelta` 的正方向相反，谁在哪一层翻号必须两边一致。

---

## 三、与其它六家的刻意背离

1. **这里是唯一一家七角色全是 `.razor` 组件的适配器，别家全是附着属性类。** 因为 Blazor 没有附着属性系统
   也没有可附着的元素树（`Src/Adapters/VeloxDev.Razor/README.md:5-15`）。**新平台若同样没有附着属性面，
   照抄这里的「组件 + 参数 + RenderFragment」而不是照抄别家的附着属性**。
2. **这里是唯一一家由 JS 独占一部分 DOM 区域的适配器**（画布宿主尺寸、网格、坐标轴、小地图视口块，§二·2）。
   别家的画布尺寸/网格都是框架渲染的产物。
3. **这里是唯一一家需要 `WorkflowGeometryScope` 这种「手势期间让所有几何写者停手」的横切机制的适配器**（§二·3）。
   别家的同步渲染让这个问题不存在。
4. **这里是唯一一家在契约之外引出 `SurfaceViewportFeed` 级联值的适配器**，专门为了让标尺重渲染不拖动节点子树。
5. **这里是唯一一家几何写入要跨一次网络往返的适配器**，因此它的 id 机制、批量回传、JS 活测都不是「实现细节」
   而是**性能主线**（§二·1）。
6. **这里是唯一一家不调用 Core 的 `SlotAnchorFrom*` 三件套的适配器**：世界坐标在 JS 里算完才回传
   （`wwwroot/veloxdev.workflow.js:1019-1024`），C# 只把结果写进 `slot.Anchor`
   （`Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:80`）。`../extension.md:155` 的「必须用三件套之一」
   这条契约在这家**没有对应物**（三件套要的是能读控件几何的宿主）。**新平台若也把测量放进 JS，
   不要为了「守契约」硬套一个 Core 函数 —— 那会把 JS 已经算好的世界坐标再换算一次。**
   代价是这家的 slot 锚点写入绕开了 Core 的坐标系收敛点，属于「已知的、有理由的例外」。

---

## 四、改这里时最容易踩的坑（带依据）

### 1. 区域设置陷阱：把 `double` 写进 CSS/SVG —— **仍在，且有两侧**

这是这家最贵的一条，也是全仓库唯一有这一类 bug 的平台（别家动画产物是 CLR 值，不用格式化进字符串）。
**为什么会有这类 bug**：C# 把 `double` 格式化成 CSS 属性值，在逗号小数点的区域下写出 `translateX(28,5px)`，
浏览器直接**丢弃**这条声明，于是标尺刻度不再跟随滚动。JS 侧反过来永远是对的（ECMAScript 的 `toFixed`
恒定用 `.`），于是**解析侧也有一半**。

**已修（做对的范本，改这类代码照这几处抄）：**
- `Attached/Workflow/WorkflowNodeDragBehavior.razor.cs:66` —— 节点位置串走 `InvariantCulture`。
- `Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:315-318` —— 推给 JS 的节点几何数组走 `InvariantCulture`。
- `Attached/Workflow/WorkflowGridDecorator.razor.cs:175-183` —— 标尺**文字**走 `InvariantCulture`（与下面同文件的那几处并存，同一文件里一半对一半错）。
- demo 侧：`Examples/Workflow/Blazor/Demo/Demo/Components/Workflow/TemplateLinkView.razor.cs:128` 的注释
  把这条规则写成了显式约定（「Razor 用当前区域写裸 double，逗号小数点会写出浏览器读不了的 SVG 属性」），
  `N()`/`Css()` 都走不变文化（同文件 `:317` 与 `:312-315`；alpha 那处的理由写在 `:311`：「它是周期的一个端点」）。

**仍然活着（写侧，用当前区域 `"0.#"` / `"0.###"`，没有 `InvariantCulture`）：**
- `Attached/Workflow/WorkflowGridDecorator.razor.cs:79,87,88,90`（标尺厚度、两条 transform、刻度长度）
- `Attached/Workflow/WorkflowGridDecorator.razor:11,26`（刻度标签的 `style="left:@tick.Pos.ToString("0.#")px"`）
- `Attached/Workflow/WorkflowMinimapOverlay.razor.cs:144-147`（宽高、节点半径、视口描边宽）
- `Attached/Workflow/WorkflowMinimapOverlay.razor.cs:448-451`（映射后的 `XCss/YCss/WCss/HCss`）
- `Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:128`（`--veloxdev-gs` 网格间距 CSS 变量）
- `Attached/Workflow/WorkflowCanvasTransformBehavior.cs:23`（`ToCss`，这个静态助手是**公开 API**，消费方会直接用来拼 style）

**仍然活着（解析侧，JS 送来的 `.` 被当前区域解析）：**
- `Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:70`：`double.TryParse(entry[1], out var x)` **没有**传
  `CultureInfo.InvariantCulture`，而送来的值由 JS 的 `toFixed(2)` 生成、**恒定用 `.`**
  （`wwwroot/veloxdev.workflow.js:1023-1024`）。在逗号小数点的服务端区域下，`double.TryParse` 的默认风格含
  `AllowThousands`，`.` 会被当成**千位分隔符** —— 结果是**静默量错**（不是抛异常，也不是零），
  插槽锚点整体错位。**修一处不够，两侧都要修。**

**判据（给下一个改这里的人）**：在 Razor 适配器里搜索 `ToString("0` 与 `double.TryParse`，
每一处都要能回答「这个值会不会进 DOM / 来自 DOM」。会，就必须显式带 `InvariantCulture`。

### 2. `OnWheelZoom` 里那两步顺序不能换，`EnsureNegativeCover` 不能省

`layout.Scale` 写入 → `WorkflowSurfaceMath.EnsureNegativeCover(Tree)` → 才读 `ActualOffset`/`ActualSize`
（`Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:218-228`，理由写在 `:224-228`）。
漏掉 `EnsureNegativeCover` 的后果与别家同源：深缩放（`Scale ≲ 0.4`）下负侧内容越出固定负偏移、**连线永久截断**
（`../extension.md:50` 的 #13）。这里额外要注意的是：**JS 侧读到的 `reachW/H` 是浏览器里的可达内容宽度，
不是模型值**，夹取时要用两者的大者（`WorkflowSurfaceBehavior.razor.cs:234-239`），否则一次边缘平移过的宿主会被缩回去。

### 3. `WorkflowRuntimeIds.TryFind` 是一次**线性扫描**

`Attached/Workflow/WorkflowRuntimeIds.cs:37-44` 用 `foreach (var pair in Ids)` 在整张表里找 id。
`Ids` 是 `ConditionalWeakTable<object,string>`，**每个节点、每个插槽都在这张表里**，
而 `OnSlotLayoutBatch` 会对批次里**每一条**调一次 `TryFind`（`WorkflowSlotLayoutBehavior.razor.cs:75`）——
即 O(槽数 × 组件总数)，且发生在服务端、每次拖动都在跑。
目前没看到性能报告，**改这里之前先量**（这条是「已知形状，未量过代价」，不要当成已确认的瓶颈）。

### 4. 两处「注释与代码不符」——按代码为准

- `Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:10` 与 `:31` 的 XML 说插槽元素暴露 **`data-slot-id`**，
  实际属性是 **`data-veloxdev-slot-id`**（`Attached/Workflow/WorkflowSlotConnectionBehavior.razor:3`；
  JS 侧全部按 `data-veloxdev-slot-id` 查询，`wwwroot/veloxdev.workflow.js:271-272,966,977,1014-1015,1082`）。
  **照注释写会拿不到任何测量结果、且不报错。**
- `Attached/Workflow/ViewPool.razor.cs:11-12` 说「Blazor 的渲染器自己做 diff/pooling，所以这只是个薄包装」，
  但同文件 `:58-65` 又做了**只在集合引用变化时才重快照**的优化 —— 这两句不矛盾，但**只看注释会以为这里可以随便
  每帧 `StateHasChanged`**：真那样做，父组件每渲染一次就 `ToArray()` 一次 O(N) 分配（注释本身承认了这条历史）。
  结论：`ItemsSource` 传引用稳定的集合并让它自己发 `CollectionChanged`。

### 5. 适配器里没有任何组件带 `_disposed` 守卫

`Attached/Workflow/` 下搜不到 `_disposed` 字段（全量 grep：只有 `WorkflowGeometryScope` 的句柄有同名私有字段，
语义无关）。于是有两条「circuit 导航走了、回调还在路上」的窗口：
- `WorkflowMinimapOverlay.razor.cs:293-333` 的 `MarkDirty` 是「取消上一个 16ms `Task.Delay` → 醒来 → `InvokeAsync(StateHasChanged)`」，
  醒来时组件可能已经 `DisposeAsync`（`:403`）走完。
- `WorkflowGridDecorator.razor.cs:105-112` 的 `OnFeedChanged` 直接 `InvokeAsync(StateHasChanged)`，
  没有「我还在树上吗」的判断。

**我没有构造过复现**（这条是形状、不是已确认故障）：若下一个 agent 在这两个地方见到
`ObjectDisposedException` 或「渲染已被释放的组件」，根因就在这，而**这是别家没有的一类坑**——
别家的窗口活到进程结束，没有「组件比进程先死」这件事。修法是在这两个异步路径上加 `_disposed` 标记。

### 6. `WorkflowCanvasTransformBehavior` 是静态助手，不是组件

`Attached/Workflow/WorkflowCanvasTransformBehavior.cs:6-9` 明说：因为 Blazor 没有元素属性系统，
它把 translate 偏移**以数据和 CSS 串两种形态**暴露出去。**它不写任何东西、也不申请任何所有权**；
真正做左右上扩展的是 surface 自己的内容层（`README.md:172-177`），所以消费方**通常不该**再自己套一次
`transform` —— 叠两层会让坐标算两次。

---

## 五、两条历史说法的核实结论

1. **「`LateUpdate` 重渲染」仍然成立，但在 demo，不在适配器。**
   原文与订阅都在 `Examples/Workflow/Blazor/Demo/Demo/Components/Workflow/TemplateLinkView.razor.cs:181`（理由：
   「它在当帧的写入落地后才触发，渲染的是刚写下的那帧（重放时每周期都触发）」）与 `:190`
   （`effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);`）——注意**必须 `InvokeAsync`**，
   因为循环在第一个 `pacer` 缺失的宿主上跑在线程池线程（见 `TransitionSystem/adapters/razor.md` §二·2）。
   `Src/Adapters/VeloxDev.Razor/` 下**没有任何** `LateUpdate` 使用；这条是 demo 侧的用法，不是适配器的机制。
   顺带：这条 demo 里**动画对象就是那个 `.razor` 组件自身**（`Transition<TemplateLinkView>`，
   `:158` 的 `BuildFlow()`），所以它的属性写入与它的重渲染在同一对象上 —— 这在别家是常态，在这家是特例。
2. **区域设置陷阱仍然成立，且比历史说法记的范围更大**：见 §4·1 —— 写侧 6 组位置（含 `WorkflowCanvasTransformBehavior.ToCss`
   这个**公开静态 API**）、解析侧 1 处，**全在适配器里**；demo 侧那 2 处已按不变文化修好。
   「只修 demo」是不够的，适配器才是消费方会直接踩到的那一层。
