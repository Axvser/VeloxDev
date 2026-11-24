# 适配器公共管线

Core 定义契约与泛型组合根，适配器只实现窄层 —— 与 [view-hosting.md](view-hosting.md) 的 ViewSource 同一形状。七家已迁移（Razor 与 WinForms 的表面滚动有明确例外）。

## 小地图

- Core：`Interfaces/WorkflowSystem/Surface/` 的 `IMinimapRenderer` / `IMinimapViewportHost` / `IMinimapInputHost` / `MinimapMetrics` / `SurfaceAvailableArea`；`WorkflowSystem/Minimap/MinimapPipelineCore` 负责订阅、缓存、去抖、映射、放置与拖拽状态。
- 适配器只给 renderer / viewport host / input host；组合根 `Src/Adapters/*/WorkflowSystem/ViewHosting/MinimapPipeline.cs` 六家同名，Razor 在 `WorkflowMinimapOverlay` 里直接组合。
- 适配器对外的 `WorkflowMinimapOverlay` / `IWorkflowMinimapOverlay` 与 `protected virtual DrawMinimap(context)` 保持同一套成员。
- 平台例外：Avalonia 捕获按 `IPointer` 逐实例；WinUI `ApplyScroll` 走 `TryRequestScroll`；MAUI 保留双 `GraphicsView` 与 Windows 指针仲裁；WinForms 用哨兵上限 + `1e9` 平移并走 `ViewportScrollRequested`；Razor 的 renderer 是 marker + 现有 fragment，拖拽/视口块移动留在 JS（Core 的 `ClampScrollOffset` 会写 `CanvasLayout`，与 Razor 的 JS overscroll 模型冲突）。
- 测试：`MinimapPipelineTests`、WPF/Avalonia/Jalium/WinForms minimap 无头测试、`MinimapPipelineBenchmarks`。

## 表面滚动 / 指标

- Core：`GUI/Surface/` 的 `SurfaceMetrics` / `ISurfaceMetricsSource` / `SurfaceWheelProfile` / `ISurfaceScrollHost` / `SurfaceScrollerCore` / `ISurfaceViewportRestoreHost` / `SurfaceViewportRestoreCore` / `SurfaceMetricsPush`。
- WPF / Avalonia / WinUI / Jalium / MAUI：metrics、滚动、导航与视口恢复全部经 Core；`ReadMetrics` 是平台的测量口。WinUI 用 `EffectiveScrollX/Y` 报告 committed offset；MAUI 用画布模型算逻辑上限、`ScrollToAsync` 落原生范围，pan/zoom 由适配器自己异步编排。
- **WinForms 例外**：`SurfaceMetrics`/`SurfaceMetricsPush` 已接入；`SurfaceScrollerCore` / `SurfaceViewportRestoreCore` **不用** —— 它是有符号 pan、无原生上限，Core 的 `[0,max]` 夹取与 `CanvasLayout` 扩展会改变越界手感；滚动/恢复在 `SurfaceScroller`。
- **Razor 例外**：`SurfaceMetrics`/`PushViewport` 已接入；滚动与恢复时序留在 JS（DOM 只能异步读，`ISurfaceScrollHost` 的同步上限/ExpandCanvas 契约不成立）。
- 测试：`SurfacePipelineTests`、WPF/Avalonia/WinUI/Jalium/WinForms 无头测试、`SurfaceScrollerBenchmarks`。

## 表面部件注册表

- Core：`GUI/Surface/SurfacePartRegistry.cs` 的 `WorkflowSurfacePart`（ScrollViewer / Canvas / GridDecorator / MinimapOverlay / PointerPressSource）、`SurfacePartRegistry`、`SurfacePartBinding<T>`。
- 注册表只存「当前是哪个部件」，按引用比较：同引用不通知，换件报 `(old,new)`，`Clear` 对每个角色各发一次退订且幂等。`SurfacePartBinding<T>` 把一个角色绑到 attach/detach 上，换件**先退旧再进新**，Dispose 退当前值 —— 「订阅集合 == 部件集合」就落在这里。
- 已迁六家：Avalonia / WPF / Jalium / WinUI / MAUI / WinForms 的 `WorkflowSurfaceBehavior`（WinForms 是 `EnsureParts` 里的 `Set` + `BindParts`；其余是 `ResolveNamedControls`）。绑定数量随各家的订阅面走：Avalonia 三条（ScrollViewer / MinimapOverlay / PointerPressSource）、WPF 两条（ScrollViewer / PointerPressSource）、Jalium 三条（ScrollViewer / Canvas 滚轮 / PointerPressSource）、WinUI 三条（ScrollViewer + Canvas 共同决定滚轮宿主，故两者都触发 `RehookWheel`、再加 PointerPressSource）、MAUI 三条（ScrollViewer / Canvas / PointerPressSource，平台平移与捏合手势都跟着源走）、WinForms 两条（ScrollViewer 的 `ScrollOffsetRequested` / MinimapOverlay 的 `ViewportScrollRequested`）。Canvas / GridDecorator 通常只取不订。`Detach` 一律走 `ReleaseParts`（先释放绑定、再 `Clear`）。换件回调里用到「旧那台」时，用绑定交回来的参数，不要读注册表（`Set` 已先写成新值）。
- Razor 不走注册表：它的部件是 `ElementReference` + `RenderFragment`，没有可订阅的宿主控件；滚动/布局在 JS 侧。
- 线程：调用方负责在宿主 UI 线程上调用；实现内部不加锁。
- 测试：`SurfacePartRegistryTests`（同引用不通知、换件顺序、Clear 幂等、绑定自动接管已登记部件、Dispose 退订）+ 六家无头套件。

## 槽位测量

- Core：`GUI/SlotLayout/` 的 `ISlotPartLocator` / `ISlotPart` / `ISlotMeasurementHost` / `SlotLayoutPipelineCore` / `SlotCenter` / `SlotCoordinateSpace` / `SlotLayoutConfiguration` / `SlotLayoutTrigger`。
- 七家都把测量编排交给 Core；坐标空间分 `CanvasLocal`（WinUI/MAUI/WinForms）与 `ScrollSpace`（WPF/Avalonia/Jalium），Razor 也用 `CanvasLocal`。
- 指纹只在完整的一趟记下（`found >= expected` 且无「可见但未测量」的部件）；容器未实例化的那一趟因此重试。认证规则见 [scheduling.md](scheduling.md) §二。
- 指纹里的插槽数与测量同口径（已实例化 / 可看见的部件），七家一致：WPF/Jalium/WinUI 数 `ContainerFromIndex` 非空的容器，Avalonia 同，MAUI 数 `BindingContext is ISlotComponent` 的后代，WinForms 数带 `ISlotComponent` DataContext 的视觉元素，Razor 数缓存部件。数 `ItemCount` 会得到「看着完整、实际没生成」的指纹（见 [scheduling.md](scheduling.md)）。
- Razor 的 locator 是缓存的同步壳；DOM 遍历与 rAF 调度由 `initSlotLayout` 的 JS 批处理驱动，Core 负责 0.01 容差、NaN 首写与图层保留。
- 测试：`SlotLayoutPipelineCoreTests`（含未实例化重试 / 稳态不空转）、WPF/Avalonia/Jalium/WinForms 无头测试、`SlotLayoutPipelineBenchmarks`。

## 未决

- 手势状态机（平移/缩放/捕获/焦点）没有抽 Core，按适配器各自实现。
- WinForms 的表面滚动/恢复与 Razor 的 JS 滚动/恢复留在适配器，理由见上面两节。
