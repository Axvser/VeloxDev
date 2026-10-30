# HeadlessAdapters — 架构

> 代码：`Src/Verification/HeadlessAdapters/`。共享夹具 `VeloxDev.AdapterTestKit`（`netstandard2.0`，只引用 Core），
> 加五套测试 —— WPF（`net8.0-windows` + `UseWPF`）、Avalonia（`net8.0` + `Avalonia.Headless` 11.1.0）、
> WinForms（`net8.0-windows` + `UseWindowsForms`）、Razor（`net8.0` + `bunit` 2.12.5）、
> Jalium（`net10.0-windows` + `Jalium.UI.Controls` 26.10.8）。
> 已登记在 `VeloxDev.slnx` 的 `/Verification/Headless/` 下（2026-10-23 用户要求）。代价：根 `dotnet test` 不再是后端-only，
> WPF/WinForms 套件要求 Windows。按路径跑 `run-headless-tests.ps1` 仍是最快的循环，`check-coverage.ps1` 跑行覆盖地板。

---

## 一、与 AUTO TEST 的分工

| 套件 | 需要什么 | 证明什么 |
| --- | --- | --- |
| `AUTO TEST` | 交互式桌面 + 真实窗口 | 七家 demo 的端到端行为（UI Automation / Playwright） |
| 本目录 | 普通测试进程 | 适配器在没有窗口系统时仍遵守的表面契约 |

`AUTO TEST` 的 AGENTS.md 已经写明「Nothing here needs a headless CI today」。本目录补的是那句之后留下的空白：
平台代码此前没有可在 CI 里跑的回归。

---

## 二、共享夹具：`TestGraph`

`VeloxDev.AdapterTestKit/TestGraph.cs` 建一棵确定性的图：两个近节点、一个 6000,4000 的远节点、一条左→右连线。

- **建图走 Helper，不走命令。** 命令的 `Execute` 是 `_ = ExecuteAsync(parameter)`（`Src/Core/VeloxDev.Core/MVVM/CommandPipeline{TParam,TResult}.cs` 的 `Execute(object?)`），
  返回时工作可能还没做完；断言会和它竞速。Helper 的 `CreateNode` / `CreateSlot` / `SendConnection` 是同步的。
- **必须换掉 `TreeDefaultViewModel` 的默认 helper。** `TreeDefaultViewModel` 构造时装的是 `new TreeHelper()`，
  而 `TreeHelper()` 的默认构造把 `useVirtualization` 设为 `false`。
  表面写 `Viewport` 是不带条件的（`WorkflowSurfaceBehavior.UpdateVisibleRegion`），
  虚拟化未启用时 `WorkflowSpatialEx.VirtualizeCore` 会抛 `ArgumentNullException`。
  所以夹具先 `tree.SetHelper(new TreeHelper(200d))`，再建节点。
- **200 是栅格边长。** 远节点在 6000,4000，默认视口 800×600，必然在可见集外；换格子大小要同步改测试的坐标。

---

## 三、WPF：STA + 手工泵

`WpfHeadlessSurface` 完全用代码搭可视树，按 trimmed 模板的形状放 `Border → TestGridDecorator → ScrollViewer → Canvas`。

- **测试类用 `[STATestClass]`**（MSTest 4 自带）。WPF 控件要求 STA；这个特性让整个类跑在一个 STA 线程上。
- **名字要自己注册。** `FindName` 是 namescope 查找，代码建的树没有 XAML 的 `x:Name` 注册：
  `NameScope.SetNameScope(host, scope)` + `scope.RegisterName(...)`。只设 `element.Name` 不够。
- **不调 `Show()`。** 只 `Measure` / `Arrange` / `UpdateLayout`，不创建 `Window`，也不需要 `Application.Current`。
- **按优先级排队的工作要泵。** `QueueViewportRestore` 用 `DispatcherPriority.Loaded` 投递，`ViewManager` 的批次用 `Background`。
  `WpfHeadlessSurface.Pump()` 用 `DispatcherFrame` + `PushFrame` 排到 `ApplicationIdle`。
- **`FrameworkElementFactory` 的模板必须 `Seal()`。** 未密封时 `LoadContent` 抛
  「FrameworkElementFactory 必须位于此操作的密封模板中」；`AnyTemplateSelector.BuildTemplate` 已封。
- **Ctrl+滚轮不在 WPF 套件里测。** `OnZoomPreviewMouseWheel` 读的是进程级 `Keyboard.Modifiers`
  （`WorkflowSurfaceBehavior.cs` 的 `OnZoomPreviewMouseWheel`），合成的 `MouseWheelEventArgs` 改不了它。缩放方向与夹取放在 Avalonia 无头输入里测。

---

## 四、Avalonia：`HeadlessUnitTestSession`

`AvaloniaHeadlessSurface` 在一个无头 `Window` 里搭同一棵树。

- **入口是 `HeadlessUnitTestSession.StartNew(typeof(TestApp))`**，每个测试用 `Dispatch(..., CancellationToken.None)` 进 UI 线程。
  `Dispatch` 每次会新建一个 `Application` 实例（`EnsureApplication`），静态状态因此按测试重置。
- **必须给主题。** 空 `Application` 没有控件模板，`ScrollViewer` 的 `Viewport` 会一直是 `0×0`，
  表面拿不到视口。`TestApp.Initialize` 里 `Styles.Add(new FluentTheme())`；项目因此直接引用了 `Avalonia.Themes.Fluent` 11.1.0。
- **名字同样要自己注册。** `NameScope.SetNameScope(host, scope)` + `scope.Register(...)` + `scope.Complete()`；
  适配器的 `FindNamed` 在找不到 namescope 时返回 `null`（`WorkflowSurfaceBehavior.cs` 的 `FindNamed`），不会抛。
- **输入是真的。** `Window.MouseWheel(point, delta, RawInputModifiers.Control)` 走真实路由，
  所以 `CtrlWheel_ZoomsIn_AndTheScaleStaysClamped` 能验方向（Scale 是折叠因子，上滚变小）与 `[0.1, 10]` 夹取。
- **每次改窗口/滚动后 `Dispatcher.UIThread.RunJobs()`。** `ScrollViewer` 的 `ScrollChanged`、视口恢复的 `Loaded` 队列都靠它。

---

## 五、今天覆盖的契约

五套套件对同一组不变量说话（失败信息因此直接指出是哪一家）。每个套件另有一份
`SurfaceContractTests`：按名字钉住七个角色与每个公共附着点/参数/静态入口 —— 这就是「契约 100%」的执行方式。

1. `IsEnabled=false` 时 `Refresh` 是 no-op；
2. attach 后命名部件被解析，`WorkflowCanvasTransformBehavior` 与网格装饰器拿到相同的内容偏移；
3. 滚动同时写 `TreeHelper.Viewport` 与 `Layout.ViewportOffset`；
4. `Layout.ViewportOffset` 在 attach 后恢复到滚动条；
5. 虚拟化的 `VisibleItems` 随视口进出节点；
6. `ViewPool` 为每个可见项建一个视图；
7. detach 后表面不再写模型；
8. （仅 Avalonia）Ctrl+滚轮的缩放方向与夹取。

行覆盖基线（2026-10-23，`check-coverage.ps1`，**已合并既有采样器无头套件**）：WPF 44.9%、
Avalonia 55.2%、WinForms 35.3%、Razor 41.9%、Jalium 35.6%；地板 44/55/35/41/35。
地板只许上调；字面 100% 行覆盖不是验收线（多 TFM `#if`、窗口路径、GPU、JS）。

---

## 六、其余五家

不是遗漏，是没有可用的无头宿主：

| 平台 | 状态 |
| --- | --- |
| WinForms | ✅ 已落地：`WorkflowTreeView` + `CreateControl()`，不 `Show()` |
| Razor/Blazor | ✅ 已落地：`bunit` 渲染 + 直接驱动 `[JSInvokable]` |
| Jalium | ✅ 已落地：控件可 `Measure/Arrange`，表面用 `NameScope` 认领部件 |
| WinUI | ❌ 需要 Windows App SDK + XAML 线程/窗口，没有真正无头模式 |
| MAUI | ❌ 没有一方无窗口宿主 |

不要把不可无头的那几家硬塞进同一个测试进程：它们会拉进 WinUI/MAUI SDK，把一个普通 runner 上的套件
变成需要交互式桌面的套件 —— 这正是 `AUTO TEST` 被分出去的原因。

---

## 七、WinForms：继承 `WorkflowTreeView`

- 表面本身就是 `WorkflowTreeView`（`UserControl` 派生），宿主只实现 `CreateNodeView` / `CreateLinkView`；
  测试里用最轻的 `Panel` 顶替卡片/连线即可。
- `[STATestClass]` + `Size = new System.Drawing.Size(800,600)` + `CreateControl()` + `PerformLayout()`；
  不 `Show()`。`BeginInvoke` 排的视口恢复用 `Application.DoEvents()` 泵。
- **`Size` 有歧义**：`System.Drawing.Size` 与 `VeloxDev.WorkflowSystem.Size` 同时可见，必须全限定。
- **滚动走 `WorkflowInput.For(tree).Scroller?.ScrollBy(...)`**，不要直接改私有平移字段；它会经
  `OnMinimapScrollRequested` 走与拖画布/小地图同一套边界语义。
- **`WorkflowSurfaceBehavior.Refresh` 不检查 `IsEnabled`**（与 WPF/Avalonia 不同）。禁用只清滚动口
  （`ClearScroller`），所以 WinForms 套件不断言「禁用后 Refresh 不写模型」。
- `ViewPool` 在 WinForms 是**同步**的（`ViewManager.AddItem` 直接建视图），不需要泵批次。

## 八、Razor：bUnit 的 C# 半边

- bUnit **2.12.5** 的新 API 是 `BunitContext` + `Render<T>`；`TestContext` / `RenderComponent<T>` 已标成
  过时（`RenderComponent` 甚至是编译错误）。旧写法会让整套编译失败。
- `IsEnabled=false` 时 `OnAfterRenderAsync` 不会 `import` JS 模块；测试直接调组件的 `[JSInvokable]` 方法：
  - `WorkflowSurfaceBehavior.OnSurfaceScroll` / `OnWheelZoom`
  - `WorkflowNodeDragBehavior.OnNodeDrag`
  - `WorkflowSlotConnectionBehavior.OnSlotConnectionStart/End`
- **`WorkflowSurfaceBehavior.ChildContent` 是 `RenderFragment<SurfaceCanvas>`**，`.Add` 的 lambda 必须带
  `canvas` 参数；无参 lambda 会得到 `RenderFragment`，编译失败。
- 槽连接的目标用 `WorkflowRuntimeIds.Get(graph.RightInput)` 取 id；`TryFind` 才能解析回实例。
- `.js` 文件不在 .NET 覆盖率里；这里的 100% 只能定义在 C# 适配层。

## 九、Jalium：无显示控件 + 手工加载状态

- Jalium 自己的测试就在无显示进程里 `Measure/Arrange`；`ScrollViewer` 需要显式 `Width/Height`，
  否则在自建 `Panel` 里拿到 0 视口。`TestGridDecorator` 必须自己 `MeasureOverride/ArrangeOverride` 子控件。
- **只 `RaiseEvent(LoadedEvent)` 不会触发表面的 `Loaded`**：Jalium 的 `FrameworkElement.SetLoadedState`
  在抬路由事件之外还显式调 CLR `Loaded`。夹具用反射调 `SetLoadedState(true)`（内部方法）——
  这就是 `Window.Show()` 内部做的那一步；方法没了会抛 `MissingMethodException`，不会静默。
- 名字用 `NameScope.SetNameScope(host, new NameScope())` + `host.RegisterName(...)`；
  `FindName` 沿父链找。
- `WorkflowSurfaceBehavior.TemplateSelectorKey` 的资源要放在 `Host.Resources`；
  `ViewPool` 必须同时拿到 `ItemsSource` 与 `TemplateSelector` 才建 manager。
- `DataTemplate()` 不要传 `typeof(object)`：Jalium 的 `TemplateKey` 会抛
  「object is not a valid template data type」；用无参构造 + `SetVisualTree`。
- `ScrollViewer.ScrollTo*` 先把偏移记成 requested，**下一次 Measure/Arrange** 才成为
  `HorizontalOffset/Viewport`；测试在滚动/恢复后要再排一次版。
- `Dispatcher.PushFrame` 是**静态**方法；`Size`/`Rect` 在 `VeloxDev.Jalium.*` 命名空间里会被
  `VeloxDev.Jalium` 抢先解析，必须 `global::Jalium.UI.Size` 全限定。

## 十、这一轮抬覆盖率的实测点

- **采样器覆盖不重复写测试**：`check-coverage.ps1` 额外跑一次
  `Examples/Transition/AUTO TEST/Samplers`，按 `(class,line)` 并集合并。采样器套件已覆盖全部适配器的
  采样器；再在每套里复制一遍只是浪费。
- **转换器类名与框架撞名**：WPF 的 `Point/Thickness/CornerRadius/Color/BrushConverter` 与
  `System.Windows*` 同名；WinForms 的 `Point/Size/SizeF/Rectangle/Padding/ColorConverter` 与
  `System.Drawing*` 同名。测试文件必须用别名，否则 CS0104。
- **WinForms 槽布局的坐标宿默认是 `Panel`**，走 `SlotAnchorFromCanvasLocal`：锚点 = 客户区中心，
  **不是** node.Anchor + 中心；真实宿主里节点 Location 已含锚点。期望值别照抄 WPF 的 56/184。
- **WinForms 自绘**：`DrawToBitmap` 会触发 `OnPaintBackground` + `OnPaint`，不需要显示窗体；
  小地图/网格装饰器因此可以直接覆盖绘制分支。
- **WinForms 节点拖拽不可合成输入**：它读 `Control.MousePosition`（真实光标），不是 `MouseEventArgs.Location`；
  不要试图用假 `MouseEventArgs` 驱动。要覆盖它得移动系统光标，代价大于收益。
- **Avalonia 无头输入要先 `MouseMove` 再 `MouseDown`**：否则命中/over 状态没建立，拖拽处理器不触发。
- **WPF/Avalonia/Jalium 小地图**：`Measure/Arrange` + `RenderTargetBitmap.Render` 可触发 `OnRender`；
  WPF 用 `System.Windows.Media.Imaging.RenderTargetBitmap`，Avalonia 用
  `Avalonia.Media.Imaging.RenderTargetBitmap`。Jalium 没有对应的托管位图 API（渲染走 GPU）。
- **`Transition<T>` 的重载不需要执行动画**：建一个带所有属性类型的 POCO，逐个调 `.Property(...)` 即可覆盖
  适配器的重载层；执行/计时归 Core 的测试。
- **Razor 的纯函数**：`WorkflowPresentation.ToCss` / `InputSlotsOf` / `OutputSlotsOf` 不需要 bUnit；
  `WorkflowGridDecorator` 用 `SurfaceViewport` 直接渲染即可。

## 十一、既有无头套件的修复

`Examples/Transition/AUTO TEST/Samplers/CoreSamplerEntries.cs` 的 `StructEntry` 曾因
`StructAssembler.Create` 的签名改成 `(ISampleable, object? start, object? end)` 而失败
（反射参数个数不匹配）。条目现在传 `[start, start, end]`；`property` 只在 `InsertFrame` 里用来写回。
这是本次新增套件之外唯一动过的既有测试。
