# HeadlessAdapters — 扩展

> 加一家可直接无头测试的平台，或给已有平台补一条契约时读这一份。
> 架构与今天的覆盖见 [`architecture.md`](architecture.md)。

---

## 一、加一家平台套件

1. 新建 `Src/Verification/HeadlessAdapters/VeloxDev.<GUI>.Headless.Tests/`：
   - 引用 `VeloxDev.AdapterTestKit` 与 `Src/Adapters/VeloxDev.<GUI>/`；
   - MSTest 4.0.2，`IsPackable=false`，`[assembly: DoNotParallelize]`；
   - 目标框架跟着适配器走（WPF 是 `net8.0-windows` + `UseWPF`；Avalonia 是 `net8.0`）。
2. 复制一份最近的 `Infrastructure/<GUI>HeadlessSurface.cs`，只改平台相关四件事：
   - **命名解析**：WPF 用 `NameScope.RegisterName`；Avalonia 用 `NameScope.Register` + `Complete`；
     WinForms 可能直接传控件引用（它没有名字解析）。
   - **滚动容器**：WPF/Avalonia 是 `ScrollViewer`；Razor 是 DOM/JS；WinForms 是 `ScrollableControl`。
   - **泵**：WPF `DispatcherFrame`；Avalonia `Dispatcher.UIThread.RunJobs()`；Razor `WaitForAssertion`。
   - **模板选择器**：WPF `DataTemplateSelector`；Avalonia `IDataTemplate`；其余按平台。
3. 测试方法与现有两套**同形**：同名、同断言、同错误信息口径。两家不一致时，失败信息直接指出是哪一家。
4. 把新项目加进 `run-headless-tests.ps1` 的 `$projects`。

## 二、给已有平台补一条契约

先判断它属于哪一层：

- **平台无关的数学/状态**（滚动夹取、视口换算、连接校验）→ 属于 Core 的测试，不该进这里；
- **适配器把平台事件/布局翻译成模型状态** → 属于这里；
- **真实窗口里的输入序列/视觉表现** → 属于 `AUTO TEST` 的 `Suites/Checks`。

写测试时：

- 断言模型侧的可观察量（`Tree.Helper.Viewport`、`Layout.ViewportOffset`、`VisibleItems`、`Canvas.Children.Count`），
  不要断言私有 `SurfaceState` 或 `state` 字段；
- 需要触发姿势时优先用平台的真实输入 API（Avalonia `MouseWheel`/`MouseDown`），
  平台改不了全局修饰键时才退回直调命令（WPF 的 Ctrl+滚轮就是这种情况）；
- 每次布局/排队之后泵一次；没有泵就断言，失败信息会指向「表面没写」，其实是「工作还没跑」。

## 二·五、平台差异速查

| 平台 | 入口 | 必须记住的一条 |
| --- | --- | --- |
| WPF | `[STATestClass]` + `DispatcherFrame` | 模板必须 `Seal()`，否则 `LoadContent` 抛 |
| Avalonia | `HeadlessUnitTestSession` | 必须加载 `FluentTheme`，否则 `ScrollViewer.Viewport` 恒 0 |
| WinForms | `[STATestClass]` + `CreateControl()` | `Refresh` 不检查 `IsEnabled`；禁用只清滚动口 |
| Razor | `BunitContext` + `Render<T>` | `IsEnabled=false` 才不 import JS；`ChildContent` 是 `RenderFragment<SurfaceCanvas>` |
| Jalium | `NameScope` + 反射 `SetLoadedState(true)` | `ScrollTo*` 要下一次 `Measure/Arrange` 才生效；`DataTemplate()` 不传 `object` |

## 三、不要做

- **不要把 GUI 类型放进 `VeloxDev.AdapterTestKit`。** 它只依赖 Core，七家共享；一旦引入 WPF/Avalonia，
  另一家就装不进来。平台差异留在各自 `Infrastructure/`。
- **不要为了「七家都绿」把 WinUI/MAUI/Jalium 硬塞进同一个进程。** 见 `architecture.md` §六。
- **不要在测试里用 `TreeDefaultViewModel` 的默认 helper。** 它的虚拟化是关的（`TreeHelper()` 默认构造把 `useVirtualization` 设为 `false`），表面一写 `Viewport` 就抛
  （`WorkflowSpatialEx.VirtualizeCore`）。用 `tree.SetHelper(new TreeHelper(200d))`。
- **不要把行覆盖地板往下调。** `check-coverage.ps1` 的地板只许随新增测试上调；字面 100% 行覆盖不是
  验收线（多 TFM `#if`、窗口路径、GPU、JS 不在一次运行的统计里）。契约 100% 由各套件的
  `SurfaceContractTests` 执行。
- **不要在测试项目里用 `#if` 假装无头。** 需要后端-only 时维护过滤器/单独的 `VeloxDev.Backend.slnx`。
