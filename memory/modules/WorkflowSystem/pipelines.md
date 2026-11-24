# 适配器公共管线

Core 定义契约与泛型组合根，适配器只实现窄层 —— 与 [view-hosting.md](view-hosting.md) 的 ViewSource 同一形状。七家已迁移（Razor 与 WinForms 的表面滚动有明确例外）；公共 API 随 `12.2.0` 发布。

## 小地图

- Core：`Interfaces/WorkflowSystem/Surface/` 的 `IMinimapRenderer` / `IMinimapViewportHost` / `IMinimapInputHost` / `MinimapMetrics` / `SurfaceAvailableArea`；`WorkflowSystem/Minimap/MinimapPipelineCore` 负责订阅、缓存、去抖、映射、放置与拖拽状态。
- 适配器只给 renderer / viewport host / input host；组合根 `ViewHosting/MinimapPipeline.cs` 七家同名。
- 公开的 `WorkflowMinimapOverlay`、`IWorkflowMinimapOverlay` 与 `protected virtual DrawMinimap(context)` 不变。
- 平台例外：Avalonia 捕获按 `IPointer` 逐实例；WinUI `ApplyScroll` 走 `TryRequestScroll`；MAUI 保留双 `GraphicsView` 与 Windows 指针仲裁；WinForms 用哨兵上限 + `1e9` 平移并走 `ViewportScrollRequested`；Razor 的 renderer 是 marker + 现有 fragment，拖拽/视口块移动仍留在 JS（Core 的 `ClampScrollOffset` 会写 `CanvasLayout`，与 Razor 的 JS overscroll 模型冲突）。
- 测试：`MinimapPipelineTests`、WPF/Avalonia/Jalium/WinForms minimap 无头测试、`MinimapPipelineBenchmarks`。

## 表面滚动 / 指标

- Core：`GUI/Surface/` 的 `SurfaceMetrics` / `SurfaceAvailableArea` / `ISurfaceMetricsSource` / `SurfaceWheelProfile` / `ISurfaceScrollHost` / `SurfaceScrollerCore` / `ISurfaceViewportRestoreHost` / `SurfaceViewportRestoreCore` / `SurfaceMetricsPush`。
- WPF / Avalonia / WinUI / Jalium / MAUI：metrics、滚动、导航与视口恢复全部经 Core；`ReadMetrics` 是唯一平台测量点。WinUI 用 `EffectiveScrollX/Y` 报告 committed offset；MAUI 用画布模型算逻辑上限、`ScrollToAsync` 落原生范围，pan/zoom 仍是适配器自己的异步编排。
- **WinForms 例外**：`SurfaceMetrics`/`SurfaceMetricsPush` 已接入；`SurfaceScrollerCore` / `SurfaceViewportRestoreCore` **不用** —— 它是有符号 pan、无原生上限，Core 的 `[0,max]` 夹取与 `CanvasLayout` 扩展会改变越界手感；滚动/恢复保留在 `SurfaceScroller`。
- **Razor 例外**：`SurfaceMetrics`/`PushViewport` 已接入；滚动与恢复时序留在 JS（DOM 只能异步读，`ISurfaceScrollHost` 的同步上限/ExpandCanvas 契约不成立）。
- 测试：`SurfacePipelineTests`、WPF/Avalonia/WinUI/Jalium/WinForms 无头测试、`SurfaceScrollerBenchmarks`。

## 槽位测量

- Core：`GUI/SlotLayout/` 的 `ISlotPartLocator` / `ISlotPart` / `ISlotMeasurementHost` / `SlotLayoutPipelineCore` / `SlotCenter` / `SlotCoordinateSpace` / `SlotLayoutConfiguration` / `SlotLayoutTrigger`。
- 七家都把测量编排交给 Core；坐标空间分 `CanvasLocal`（WinUI/MAUI/WinForms）与 `ScrollSpace`（WPF/Avalonia/Jalium），Razor 也用 `CanvasLocal`。
- Razor 的 locator 是缓存的同步壳，真正的 DOM 遍历/rAF 调度仍由 `initSlotLayout` 的 JS 批处理驱动；Core 负责 0.01 容差、NaN 首写与图层保留。
- 测试：`SlotLayoutPipelineCoreTests`、WPF/Avalonia/Jalium/WinForms 无头测试、`SlotLayoutPipelineBenchmarks`。

## 未决

- 手势状态机（平移/缩放/捕获/焦点）没有抽 Core，仍按适配器各自实现；这是刻意后置的一块。
- WinForms 的表面滚动/恢复与 Razor 的 JS 滚动/恢复不迁 Core；它们不是漏项，硬套会改变行为或同步契约不成立。
