# TransitionSystem — WinUI 适配器

> 契约与官方做法在 [`../extension.md`](../extension.md)；怎么用这一家见
> `skills/veloxdev-create-animation/references/adapter.md`。本文只写这一家的**形状**：
> 被哪些平台事实逼成现在这样、哪里和另外六家不一样、改哪里会踩什么。

代码：`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/`（含 `Samplers/`）。

---

## 一、这一家实现契约的方式（只说与契约有关的取舍）

骨架与 WPF 一一对照（`DispatcherQueue` 换 `Dispatcher`，`DispatcherQueuePriority` 换 `DispatcherPriority`），
七个类都是薄壳；真正有内容的只有 `UIThreadInspector`、`Interpolator.CreateScheduler` 与十个采样器。

契约之外，这一家**多了一个公开成员**：`UIThreadInspector.CaptureUIThread()`（`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:16-21`）。
理由见 §二·L5 —— 它是 WinUI 拿不到全局 UI 队列时唯一的补救入口，别家（有 `Application.Current.Dispatcher` / `Dispatcher.UIThread`）不需要。

`CreateFramePacer` 是**必须覆写**而不是可选：不覆写就退回线程池定时器，采样写路径与等待线程不一致，
正是 `extension.md` §二·5 要避免的每帧一次 dispatch；本家用 `DispatcherQueue` 建一个 `DispatcherQueueTimer` 当 pacer
（`PlatformAdapters/TransitionInterpreter.cs:9-12`）。

采样器共十个（`PlatformAdapters/Interpolator.cs:14-23`）：Brush / Thickness / Point / CornerRadius / Transform /
Projection / Size / Rect / GridLength / Color。其中三个（Brush、Projection、Transform）的端点值是 WinRT
对象，**在纯数据进程里无法构造**，所以它们不在闭式解套件里、改由真跑起来的 AT 覆盖
（`Examples/Transition/AUTO TEST/Samplers/UnreachableSamplers.cs:40-50`、`Samplers/WinUiEntries.cs` 的 remarks）。

---

## 二、平台硬限制（决定了别家能照抄什么）

**L1 · WinUI 没有 Preview / 隧道阶段，且指针事件在到达自定义处理器前已被框架标记 handled。**
本家的滚轮缩放因此挂在 `ScrollViewer` 上、用冒泡阶段、并注册 `handledEventsToo: true`
（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:278-288`，理由自陈在 `:278-281`）；
代价是**一格 Ctrl+滚轮可能先被原生滚动吃掉一点**（同注释 `:280-281` 自认，`skills/veloxdev-create-workflow/references/gui/winui.md:19` 复述）。
同一约束在别处也留下印子：插槽连线用的是冒泡 `PointerPressed`/`PointerReleased`、**不置 `e.Handled`**，
并按下降沿 `ReleasePointerCaptures()` 来替代「抢在子元素之前」
（`Attached/Workflow/WorkflowSlotConnectionBehavior.cs:37-49`），而 WPF 用的是隧道阶段的 `PreviewMouseLeftButtonDown` 并置 `Handled`
（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:26-33,43-44`）。

**L2 · WinUI 没有公共 `OnRender`，自定义绘制的常规路子不存在。** 小地图因此是**保留式元素池化**：
`WorkflowMinimapOverlay : Canvas`（`Attached/Workflow/WorkflowMinimapOverlay.cs:21`），
`Rectangle` 子元素池 + **原地改** `Width/Height/SetLeft/SetTop`
（`RebuildShapes`，`:487-600`），刷新由 16 ms `DispatcherQueueTimer` **节流而非去抖**（`:184-191` 建、`:337-354` 调度，
去抖会「连续平移期间只画一次」的理由写在 `:341-344`），并且 `Tick` 里吞 `COMException`（`:189-191`，应用拆卸时对象已销毁）。
判据：**别家有 `OnRender`/`Render` 就别抄这套** —— WPF/Avalonia/Jalium 覆写 `OnRender`/`Render`
（`Src/Adapters/VeloxDev.WPF/.../WorkflowMinimapOverlay.cs:442`、`.../Avalonia/.../WorkflowMinimapOverlay.cs:529`、
`.../Jalium/.../WorkflowMinimapOverlay.cs:274`），MAUI 走 `GraphicsView`/`IDrawable`（`.../MAUI/.../WorkflowMinimapOverlay.cs:19`）。

**L3 · `LayoutUpdated` 是逐元素的，且画布在节点子树布局之后才重排自己的子元素。**
所以槽位测量不能只挂节点自己的 `LayoutUpdated`，还要**额外**挂坐标宿主（`PART_Canvas`）的
（`Attached/Workflow/WorkflowSlotLayoutBehavior.cs:147-163`；理由注释在 `:21-28`：只挂节点那份会在节点**移动前**的
画布位置上量到槽位，表现为折叠缩放时「连线端点悬在端口外」）。**WPF 免疫**：它的 `LayoutUpdated` 是整棵树一次
layout pass、永远 post-arrange（同注释 `:26-28`）。WinUI 是七家里唯一挂两级 `LayoutUpdated` 的。
附带两条同源事实：`LayoutUpdated` 的 `sender` 恒为 `null`，只能用闭包捕获控件（`:108-110`）；
`SizeChanged` 与之并行挂（`:205-213`），两者都**同步**调 `Sync`，异步 hop 会晚一帧（`:250-266`）。

**L4 · WinRT 的 `DependencyObject` 需要进程里有真正的 XAML 运行时才能类激活。**
纯数据进程里 `new SolidColorBrush()` 直接 `REGDB_E_CLASSNOTREG`（`UnreachableSamplers.cs:26-31,40-50` 记着实测到的
`RPC_E_WRONG_THREAD` / `REGDB_E_CLASSNOTREG` 两层底）。后果是这三个采样器的正确性**不可能**在纯数据套件里验。
这不是懒，是硬限制；但它意味着改这三个采样器时，闭式解套件不会拦你。

**L5 · WinUI 没有「应用的 DispatcherQueue」这个全局句柄。** `DispatcherQueue.GetForCurrentThread()` 是唯一的入口，
没有 `Application.Current.DispatcherQueue` 可兜底（`UIThreadInspector.cs` 全文无 `Application.Current`）。
所以 `ThreadFor` 的策略是「目标自身知道就用它，否则用**曾经捕到的那个**」
（`QueueFor`，`:36-41`），而那个「曾经捕到的」只能由调用方在 UI 线程上调 `CaptureUIThread()` 落下，
且是**静态 CAS、一次定终身**（`:20`）—— 第一次捕错（例如在某条自己建了 `DispatcherQueue` 的后台线程上捕）
就永久错，没有第二次机会。对照 WPF：`target is DispatcherObject ? o.Dispatcher : Application.Current?.Dispatcher ?? Dispatcher.FromThread(...)`
（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs:26`）——它有一个权威全局可依。

**L6 · `DispatcherQueuePriority` 没有 `Render` 这一档。** 别家的 `TransitionEffect.Priority` 默认
`DispatcherPriority.Render`（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/TransitionEffect.cs:7`、Avalonia/Jalium 同），
WinUI 取 `High`，并用注释解释为什么（`PlatformAdapters/TransitionEffect.cs:7-8`：「在处理渲染之前先处理」——
正因为没有 Render 这个语义档位，才需要一句话解释）。
同族的第二处：`InternalPriority` 覆写成 `Normal`（`UIThreadInspector.cs:48`），而 WPF/Avalonia/Jalium 都取各自最高的
`Send`（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs:29`）。
`Normal` 恰好等于 `default(DispatcherQueuePriority)`，所以 `extension.md` §二·6 警告的「不覆写 → Inactive → 启动前停顿」
在 WinUI 不成立（Inactive 是 WPF 的档位）；但 `Prepare` 的阻塞读 `Run<T>` 在 WinUI 排的是 `Normal`，
即它可能排在别的 `Normal`/`High` 工作之后 —— 这一条**代码里没有任何理由注释**，见 §四·P1。

**L7 · 这一家的值类型会在构造/写入时抛，不是截断。**
`Microsoft.UI.Xaml.CornerRadius` 拒绝负分量（`PlatformAdapters/Samplers/CornerRadiusSampler.cs:9-10`：注释写明「越界是抛异常，不是截断」，`:10` 的 `ClampAtZero` 就是为这条存在），
`GridLength` 同（`Samplers/GridLengthSampler.cs:26-27`：「连构造函数都会 Validate —— 越界是抛异常，不是截断」）。
所以这两个采样器**必须钳**，不能照 `ThicknessSampler`/`PointSampler` 那样裸 lerp。

**L8 · `Projection` 家族里真正被支持的只有 `PlaneProjection`。** 注册的是**抽象基类** `Projection`
（`PlatformAdapters/Interpolator.cs:19`），而 `Normalize` 把任何非 `PlaneProjection` 的值换成默认 `PlaneProjection`
（`Samplers/ProjectionSampler.cs:64-82`）。即：这个采样器对**整个 `Projection` 家族**作了承诺，实际只支持一个成员，
其余子类会被静默替换成「无旋转无偏移的平面投影」—— 与 `extension.md` §二·1「注册基类型就承诺整个家族」同形的静默降级。
同一条也解释了为什么 `RotationDirection` 在这一家只对投影生效：见 §三·D4。

---

## 三、刻意背离（与另外六家不一样，以及为什么）

**D1 · 只有这一家在**混合两端不可比的画刷时**没有真正的交叉淡出**。**
`Samplers/BrushSampler.cs:98-109` 退化成「取一个代表色写进 scratch 纯色刷」，代表色取**最后一个** gradient stop
（`:133-138`）；WPF 是唯一有真交叉淡出的（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/BrushSampler.cs:47-60,91-119`
用 `RenderTargetBitmap` 逐帧重画两端并按 opacity 混）。
这里的做法和其他家不一样，因为 WinRT 的 `Brush` 没有可用的离屏合成入口。
「取首/取尾」这一档也是分裂的：WinUI 与 Jalium 取**最后**一个 stop，Avalonia 与 MAUI 取**第一个**
（`skills/veloxdev-create-animation/references/adapter.md:116`）。

**D2 · 只有这一家按预乘 alpha 混合颜色。**
`Samplers/BrushSampler.cs:140-180`：先乘 alpha、在预乘空间插值、最后除回来；
进度取自**肉眼所见的颜色**而不是预乘后的通道，因为每个预乘通道里都裹着 alpha、在那里钳不住色相
（理由注释 `:142-146`）。其余六家都在非预乘空间插值，同一份 `ColorSampler` 的独立实现也是非预乘的
（`Samplers/ColorSampler.cs:16-27`），**两个文件对同一件事用两套算法**是刻意的，不是疏漏。

**D3 · 只有这一家把圆角的四个分量各自钳制、不共享进度。**
`Samplers/CornerRadiusSampler.cs:22-27` 及注释 `:21-22`：四个圆角互不相干，逼它们同进同退会让还能缩的角无谓停住。
其余四家的 `CornerRadiusSampler` **完全不钳**（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/CornerRadiusSampler.cs:15-19`、
Avalonia/MAUI/Jalium 同）。`adapter.md:102` 明说这一家是「the one to copy」。
**顺带一条承重事实**：`CornerRadius` 的构造参数顺序这一家是 `(topLeft, topRight, bottomRight, bottomLeft)`
（树内见 `Samplers/CornerRadiusSampler.cs:25-28`），而 MAUI 是 `(topLeft, topRight, bottomLeft, bottomRight)`
（`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/Samplers/CornerRadiusSampler.cs:21`，它先分别算出四个变量再按下标传）
—— 抄过去下面两个角会**每一帧**都对调，且从第一帧就错。

**D4 · `RotationDirection` 在这一家只对 `Projection` 生效，对 `RotateTransform` 是空操作。**
`Samplers/TransformSampler.cs:172` 的 `LerpAngle` 是裸 `Lerp`，全文不读 `options`（`:16-46` 只把它透传给
`NormalizeStart/End`，而这两者是直通）；方向选择只出现在 `Samplers/ProjectionSampler.cs:15,39-62`。
WPF/Avalonia/Jalium 都在 **TransformSampler** 里读方向（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/TransformSampler.cs:216-221`、
Avalonia 同文件 `:238-254`、Jalium `:229-234`；`../extension.md` §4.6 的联动表列的也是这几处）。
从 WPF 搬一段 `Property(x => x.RenderTransform, new RotateTransform{...}, RotationDirection.ClockWise)` 过来，
**编译通过、动画照跑、方向不生效**，且没有任何地方会报错（`extension.md` §4.6 的收尾句）。

**D5 · `GridLength` 单位不同时切到**终点**值，而不是保持起点。**
`Samplers/GridLengthSampler.cs:18-22`（`t >= 1 ? g2 : g1`）；Avalonia 的同名采样器保持起点。
`adapter.md:106` 的判断是「只有前者真的到达目标」。这里和别家不一样，因为单位类型不同就没有可插值的中间态，
两害相权只能选「至少能到」。

**D6 · 只有这一家把「端点身份」这件事做了一半。**
`Samplers/TransformSampler.cs:22-23` 在 `t == 0` / `t == 1` 原样返回调用方给的实例（注释 `:18-21` 讲清理由：
嵌套路径 `((TranslateTransform)x.RenderTransform).X` 依赖声明时的运行时类型），
但**同一个目录**的 `Samplers/ProjectionSampler.cs:20-24` 无条件把 scratch 写出去，端点也不例外，且没有任何注释。
后果限于投影：动画结束后 `target.Projection` 不是调用方给的那个实例。
改这里时别把 `ProjectionSampler` 当范本抄到别处。
（`adapter.md:75` 把端点守卫写成通则，这两处是同一家内的不一致。）

**D7 · 只有这一家为「快速路径」设了类型门禁。**
`Samplers/TransformSampler.cs:48-53` 的 `IsKnownTransform` 把自定义 `Transform` 子类挡在克隆快路径之外 ——
因为 `CloneTransform`（`:55-63`）对未知类型**抛异常**，而那正是唯一会抛的地方。
Avalonia 有同一道门禁（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/Samplers/TransformSampler.cs:29` 的调用点、`:58` 的 `IsKnownTransform`、`:61` 的 `CloneTransform`）。抄这一家时这道门禁必须一起抄。

---

## 四、坑（改这里时最容易踩的，附依据）

**P1 · `InternalPriority = Normal` 是唯一没有理由注释的优先级决定**（`UIThreadInspector.cs:48`），
别家是 `Send`。它不是错，但它让 `Prepare` 的阻塞读排在有优先级宿主里较低的档位上；改成 `High` 之前请先想清楚
`extension.md` §二·6 那条（读阻塞的是发起动画的那个线程）。

**P2 · pacer 的定时器刻意是 `IsRepeating = false`，但每次 `Arm` 都会重新 `Start()`。**
`PlatformAdapters/TransitionInterpreter.cs:42` 与 `:18-23`。这与 `adapter.md:63` 的说法**不矛盾但极易误读**：
那条规则针对 MAUI 的 `IDispatcherTimer`（`IsRepeating=false` 之后**不再**响应 `Start`）；
WinUI 的 `DispatcherQueueTimer` 每次 `Start` 都会重新计时，所以「一次 `Arm` 恰好一次 `Fire`」成立。
**把这一家的 `false` 抄到 MAUI，或把 MAUI 的用法当通则，都会得到恒为两帧的动画。**

**P3 · `Dispose` 必须 `base.Dispose()` 之后再退订 `Tick` 并置空定时器**（`TransitionInterpreter.cs:27-37`）。
顺序反了会让挂着的续体被搁死（`extension.md` §二·5）。

**P4 · 端点身份见 §三·D6。** 改 `ProjectionSampler` 时补端点守卫要顺手确认 `PlaneProjection` 的嵌套路径是否被依赖
（`Projection` 是抽象基类，能声明出来的是 `PlaneProjection`）。

**P5 · `Projection` 注册在基类上 → 静默替换（见 §二·L8）。** 想真正支持别的 `Projection` 子类，
得先注册**它自己的确切类型**（查找先精确后基类），再在采样器里加分支，否则新子类永远走不到。

**P6 · `ColorSampler` 与 `BrushSampler` 的混色算法不同（预乘 vs 非预乘，见 §三·D2）。**
改动任一处时不要「顺手统一」—— 统一哪一边都会改变另一边的正确性（`ColorSampler.cs:16-27` 的注释与
`BrushSampler.cs:142-146` 各自写下了各自的理由）。

**P7 · `ThemeValueConverters.ThemeResourceLookup` 自己走 `Application.Current.Resources`，
包含 `ThemeDictionaries` 与 `MergedDictionaries` 两层递归**（`PlatformAdapters/ThemeValueConverters.cs:265-307`）。
`Application.Current` 为 `null` 时返回 `false`（`:269-273`）—— 纯单元测试进程里就是这条路径，
所以这里的失败是「转换器返回 null」，不是异常。

---

## 五、核不到的东西（写下来免得下一个人重找）

- 历史记录里的「WinUI **brush 没有绝对映射模式**」这句话，在 `Src/`、`Examples/`、`memory/`、`skills/` 里
  **都搜不到出处**。能确证的是它最近邻的那条事实：这一家混色时没有真正的交叉淡出、退化为单一代表色（§三·D1），
  以及它走预乘空间（§三·D2）。**照这句原话去改代码会误导。**
- 历史记录里的「**池化视图可能在离树状态下收到属性变更**」同样搜不到出处，且 WinUI 的池化视图
  **从不离开视觉树**（`Attached/Workflow/ViewManager.cs:190-201` 只 `Visibility = Collapsed` + `DataContext = null`，
  从池里取回时也不再 `Children.Add`，`:160-174`），并且 `PropertyChanged` 在入池前就退订了（`:192`）。
  离它最近的机制是 `ViewManager.cs:298` 那个延迟闭包，见 `memory/modules/WorkflowSystem/adapters/winui.md` §四·P2。
