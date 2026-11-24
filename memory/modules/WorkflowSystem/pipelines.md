# 适配器公共管线（pilot）

Core 定义契约与泛型组合根，适配器只实现窄层 —— 与 [view-hosting.md](view-hosting.md) 的 ViewSource 同一形状。目前三块 pilot 只有 **WPF 已迁移**，其余六家仍走旧实现；铺开前先读本文的未决项。

## 小地图

- Core：`Interfaces/WorkflowSystem/Surface/` 的 `IMinimapRenderer` / `IMinimapViewportHost` / `IMinimapInputHost` / `MinimapMetrics`；`WorkflowSystem/Minimap/MinimapPipelineCore` 负责订阅、缓存、去抖、映射、放置与拖拽状态。
- 适配器：renderer（画框/节点/视口）、viewport host（滚动读写/原生上限）、input host（指针/捕获）；WPF 组合根在 `ViewHosting/MinimapPipeline.cs`。
- 公开的 `WorkflowMinimapOverlay`、`IWorkflowMinimapOverlay` 与 `protected virtual DrawMinimap(context)` 不变。
- 六家落点：Avalonia 捕获按 `IPointer` 逐实例；WinUI 的 `ApplyScroll` 必须走 `TryRequestScroll`；MAUI 保留双 `GraphicsView` 与 Windows 指针仲裁；Jalium 用 `HitTestCore`；WinForms 用哨兵上限并走 `ViewportScrollRequested`；Razor 没有同步 part/renderer，走 fragment + JS，并保留 JS 直接移动视口块。
- 测试：`MinimapPipelineTests`、WPF `MinimapHeadlessTests`、`MinimapPipelineBenchmarks`。

## 表面滚动 / 指标

- Core：`GUI/Surface/` 的 `SurfaceMetrics` / `ISurfaceMetricsSource` / `SurfaceWheelProfile` / `ISurfaceScrollHost` / `SurfaceScrollerCore` / `ISurfaceViewportRestoreHost` / `SurfaceViewportRestoreCore` / `SurfaceMetricsPush`。
- WPF：`WorkflowSurfaceBehavior` 内的 host 层；`ReadMetrics` 是唯一的平台测量点，grid/minimap/viewport 推送共用一份 `SurfaceMetrics`。
- 六家风险：WinUI `ChangeView` 异步，host 必须继续报告逻辑 committed offset；MAUI 原生范围异步重测，同步 host 契约还不诚实；WinForms 是有符号 pan、无上限；Razor 滚动是 JS/DOM，不能同步读。
- 测试：`SurfacePipelineTests`、WPF `WorkflowSurfaceHeadlessTests`、`SurfaceScrollerBenchmarks`。

## 槽位测量

- Core：`GUI/SlotLayout/` 的 `ISlotPartLocator` / `ISlotPart` / `ISlotMeasurementHost` / `SlotLayoutPipelineCore` / `SlotCenter` / `SlotCoordinateSpace` / `SlotLayoutConfiguration` / `SlotLayoutTrigger`。
- WPF：`WorkflowSlotLayoutBehavior` 只保留 locator/host；`Refresh` 映射为 `Viewport` 触发。
- 六家：坐标空间分 `CanvasLocal`（WinUI/MAUI/WinForms）与 `ScrollSpace`（WPF/Avalonia/Jalium）；Razor 无法用同步 locator，需 Core 暴露 `ApplyMeasured` 批量写回。
- 测试：`SlotLayoutPipelineCoreTests`、WPF `SlotLayoutHeadlessTests`、`SlotLayoutPipelineBenchmarks`。

## 未决

- `MinimapMetrics` 与 `SurfaceMetrics` 各带一份 `Available*`；铺开前合并成一套。
- 只有 WPF 迁移，其余六家仍是旧实现。
- Core 新增了公共 API；下一次发版需要 `12.2.0`（Core/Generator/适配器 x2）。
