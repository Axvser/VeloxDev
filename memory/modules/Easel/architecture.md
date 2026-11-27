# Easel — 示例工作台

> 代码：`Easel/Examples/`。运行时在 `Easel/Src/Easel/`，生成器在 `Easel/Src/Generators/`，面向消费者的手册在 `Easel/skills/easel-build-winforms-xaml-ui/`。
> 两个示例共用同一套运行时：`XamlGallery` 是目录 / 门面，`IndustrialWorkbench` 是工业控件工作台。

## XamlGallery

- 组合根：`App` → `MainForm`（Window 根）→ `GallerySession` → `GalleryVisualShell`。窗口的 DataContext 是 session；shell 在 `Attach` 后把自己的 DataContext 换成 `GalleryShellViewModel`。
- 目录是三层：**Scenarios / Easel native / WinForms native** → 组 → 卡片。分区与组的顺序只在 `Easel/Examples/XamlGallery/Demo/Catalog/GalleryTaxonomy.cs`；卡片用组名派生分区。`CardCatalog.Create` 的工厂顺序决定 `--all` 与组内卡片顺序。
- 工业页在 `Easel/Examples/XamlGallery/Demo/Views/Industrial/`（ProductionLine / ProcessFlow / AlarmCenter / MachinePanel / TearOff），ViewModel 在 `Easel/Examples/XamlGallery/Demo/ViewModels/IndustrialViewModels.cs`，用 WinForms `Timer` 推进。仪表弧与趋势点先由 `GaugeGeometry` / `PointF[]` 算好，再绑定到 `Path` / `Polyline` / `Polygon`。
- Tear-off 页把同一棵子树在页面与独立 `Window` 之间搬：拖动超过阈值立即建窗，`Window.Content = 面板`；用 `VisualHost.CancelPointerGesture` 取消页面捕获，再 `window.Surface.AdoptPointerGesture(screenPoint, MouseButtons.Left)` 把按住状态交给新宿主，窗口位置先按 `headerLocal` 对齐指针。回坞时先从窗口包装摘除、`Window.Content = null`、再 `DockSlot.Child = 面板`。`Window.DataContext` 在 `Content` 之前设成页面 VM，窗口 `Owner` 指向 Gallery 窗口。
- 跨宿主手势的原生入口在 `Easel/Src/Easel/Visuals/VisualHost.cs`：`CancelPointerGesture` 只清捕获、不补 `PointerReleased`；`AdoptPointerGesture` 命中新宿主并在当前位置补一次 `PointerPressed`，之后移动与抬起都走新宿主。
- 虚拟化引擎：`VirtualizingPanel` 是 abstract；子类只实现 `MeasureLayout` / `BuildLayout`（返回 `VirtualizationLayoutContext`），引擎负责 keep/recycle/reuse/create、池和 host 顺序。应用层用 `VirtualizingLayoutPanel` + `IVirtualizingLayout` / `DelegateVirtualizingLayout`，不必继承；concrete 布局有 stack/listbox/wrap/uniform grid。1D 用区间算术，2D/任意布局可用 `VirtualizationSpatialIndex`；列表统一叫 `VirtualizingListBox`。
- `Easel/Examples/XamlGallery/Demo/Views/GalleryTheme.cs` 的 `Fill` 除主题键外写一组固定的工业语义色：`Signal` / `Positive` / `Warning` / `Danger` / `HmiGrid` / `HmiTrack` / `SignalGradient`；状态色不随主题变。
- Data 组的 `data.virtualizing-panel` 是自定义 `VirtualizingPanel` 子类（`Easel/Examples/XamlGallery/Demo/Views/Cards/VirtualizationLogPanel.cs`）窗口化 10 万/100 万行日志，显示 first/realized/created/reused/recycled；测试在 `Easel/Src/Test/Unit/Easel.Test/VirtualizingPanelScenarioTests.cs`。
- WinForms native 组（`Easel/Examples/XamlGallery/Demo/Catalog/Cards/NativeCards.cs`）是 5 个真实托管场景，没有 `IsSnapshot` 快照卡：`native.workspace`（DataGridView/DataTable + TreeView + 原生编辑器 + ToolStrip/StatusStrip）、`native.embed`（原生 GroupBox 内放 Easel 视觉子树，与原生 ListBox 互驱）、`native.property`（PropertyGrid + CLR 设置对象）、`native.binding`、`native.host`。拆卸期用 `_detached` + `Native.IsDisposed` 守卫原生事件。
- 启动开关在 `GalleryCapture`：`--group` 同时匹配组名与分区名，`--scroll` 只对单个 target 生效，`--preview-only` 隐藏两侧栏。

## IndustrialWorkbench

- 组合根：`App` → `MainForm` → `WorkbenchShell`。首页 `OverviewView` 用固定设计尺寸（1056 DIP 宽）包在 `Viewbox` 里，任何窗口下全部控件同时可见；目录选中后切换到 `ControlDetailView`。
- `Easel/Examples/IndustrialWorkbench/Demo/Controls/` 是自绘 `VisualElement`（`OnRender` + `DependencyProperty`），`StatusTile` 是 XAML `UserControl` + code-behind；`Easel/Examples/IndustrialWorkbench/Demo/Views/Controls/` 是每个控件的大图实例与用法片段。
- `WorkbenchViewModel` 用 16 ms（约 60 fps）Timer 推进可见动画；仿真值按真实时间缩放，趋势/报警仍按 150 ms accumulator 采样。`Setpoint` / `PumpOn` / `ValveOpen` 通过 `Mode=TwoWay` 写回模型，阀门关闭会连锁到流量。
- 启动开关在 `WorkbenchCapture`：`--smoke`、`--capture [dir] [--entry id]`、`--selftest <dir>`；`--selftest` 注入真实指针验证旋钮拖动、拨杆/阀门切换与报警确认。
