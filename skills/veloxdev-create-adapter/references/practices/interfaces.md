# Interface surface

The types below are the adapter's contract with `VeloxDev.Core`. Namespace is `VeloxDev.WorkflowSystem` unless stated otherwise.

## Workflow components and their helpers

`ITreeComponent`, `INodeComponent`, `ISlotComponent`, `ILinkComponent` are the model; `GetHelper()` returns the behavior half. The helper capabilities an adapter reads are optional interfaces, not members of the helper interfaces:

| Capability | Reached through | Supplies |
| --- | --- | --- |
| Input events | `IInputEvents.Input` (`InputRelay`) | pointer entered/exited/moved/pressed/released/wheel, key down/up |
| Node lifecycle | `INodeEvents` | `Moving` / `Moved`, `Resizing` / `Resized`, `Deleting` / `Deleted` |
| Slot lifecycle | `ISlotEvents` | `ChannelChanging` / `ChannelChanged` |
| Tree lifecycle | `ITreeEvents` | `Connecting` / `Connected` |
| Link geometry | `ILinkHitTestable` | `Curve`, `Visual`, `SetCurve`, `Contains` |

`WorkflowEventRelay.Attach(node, INodeEventSink)`, `Attach(slot, ISlotEventSink)` and `Attach(tree, ITreeEventSink)` forward those capabilities to a host object once for every adapter. Call it from the attached behavior whose `DataContext` is the component, and dispose the returned subscription on rebind/unload.

Geometry the adapter reads and writes:

- `INodeComponent.Anchor` / `Size` — collapsed-through-`Scale` view values; setters notify the spatial index and views.
- `ISlotComponent.Anchor` — starts at `(NaN, NaN, 0)`; the adapter's slot layout writes it.
- `ITreeComponent.Layout` — `CanvasLayout` with `OriginSize`, `PositiveOffset`, `NegativeOffset`, `Scale`, `ActualSize`, `ActualOffset`, `ViewportOffset`, `ZoomCenter`, `CollapsePivot`.
- `ITreeHelper.VisibleItems` — the virtualized realization set the view pool and link hit-testing walk.
- `ITreeHelper.Viewport` — `Viewport(left, top, width, height)` written by the surface.

## Surface metrics, scroll and viewport

| Type | Role |
| --- | --- |
| `ISurfaceMetricsSource` | `ReadMetrics()` returns the current `SurfaceMetrics`. Allocation-free and current, not a cached first frame. |
| `SurfaceMetrics` | Snapshot of scroll offsets, content offsets (`CanvasLayout.ActualOffset`), viewport size, ruler band, scrollbar thicknesses and the derived `SurfaceAvailableArea`. Create with `SurfaceMetrics.Create(...)`. |
| `SurfaceMetricsPush` | `PushGrid(metrics, decorator, tree)`, `PushMinimap(metrics, minimap, tree)`, `PushViewport(metrics, tree)`. The first two also push the offset pair; `PushViewport` converts to canvas-local, writes `ITreeHelper.Viewport` and persists `Layout.ViewportOffset`. |
| `IWorkflowGridDecorator` | `ScrollOffsetX/Y`, `ContentOffsetX/Y`, `RulerBand`. |
| `IWorkflowMinimapOverlay : IWorkflowGridDecorator` | Adds `ViewportWidth/Height`, `WorkflowTree`, `IsMinimapVisible`, `AvailableRight/Top/Bottom`, `MinimapMargin`. |
| `ISurfaceScrollHost` | `Layout`, `ViewportWidth/Height`, `Horizontal/VerticalOffset`, `Horizontal/VerticalMaximum`, `WheelProfile`, `ApplyOffset(double?, double?)`, `ExpandCanvas()`. |
| `SurfaceWheelProfile` | `FromLines(deltaPerNotch, scrollLines, lineHeight)` or `FromPixels(deltaPerNotch, pixelsPerNotch)`. |
| `SurfaceScrollerCore` | Implements `IWorkflowSurfaceScroller` + `IWorkflowSurfaceNavigator`. Owns wheel conversion, per-axis clamping, overscroll canvas growth and centering. Constants `DefaultWheelDelta` (120) and `DefaultLineHeight` (16). |
| `IWorkflowSurfaceScroller` | `ScrollBy(wheelDeltaX, wheelDeltaY)` — wheel deltas, not offsets or notch counts. |
| `IWorkflowSurfaceNavigator` | `CenterOn(localX, localY)` — canvas-local point at the viewport center. |
| `ISurfaceViewportRestoreHost` | `IsRestoreEnabled`, `Horizontal/VerticalMaximum`, `ApplyOffset(x, y)`, `DeferRestore(Action)`. |
| `SurfaceViewportRestoreCore` | `Capture(tree)`, `Schedule(host, afterRestore)`, `HasPendingRestore`. Keyed on the tree reference; one restore per tree. |
| `SurfaceViewport` | Immutable snapshot used by Razor/MAUI-style overlay feeds: tree, scroll, viewport, content offsets. |
| `SurfaceViewportFeed` | `Publish(SurfaceViewport)` + `Changed`; lets a lightweight overlay re-render without the content subtree. |
| `WorkflowSurfaceMath` | The single arithmetic copy: `ToCanvasLocal`, `ToCanvasLocalAnchor`, `ToScroll`, `HasViewportRestore`, `ViewportRestoreScroll`, `ViewportOffsetFromScroll`, `ClampScrollOffset`, `ScrollMax`, `ClampValue`, the `Grid*` helpers, the three `SlotAnchorFrom*` forms, the minimap helpers, `CenterScroll`, `PivotCenterScroll`, `ScaleCollapse`, `ScaleVisualBounds`, `EnsureNegativeCover`. `DefaultPanExtendRatio` is 0.15. |

## Slot layout

| Type | Role |
| --- | --- |
| `SlotLayoutPipelineCore` | Owns which parts to measure, declaration order, duplicate suppression, write-back tolerance and the shared re-measure schedule. Constructor `(INodeComponent node, object view, ISlotPartLocator locator, ISlotMeasurementHost host, SlotLayoutConfiguration configuration)`. |
| `SlotLayoutPipelineCore` members | `RequestMeasure(SlotLayoutTrigger)`, `Measure()`, `CancelPending()`, `Stop()`, `WatchedPropertyNames`, `Node`, `Configuration`. |
| `SlotLayoutTrigger` | `Layout` (stamp-filtered, flushed inside the layout pass), `Model`, `Configuration`, `Viewport`, `Attachment` (unconditional, deferred). |
| `SlotLayoutConfiguration` | Parses `SlotNames`, `EnumeratorNames` (comma-separated, de-duplicated, order preserved) and `CoordinateHostName` / `CoordinateHostType`. |
| `ISlotPartLocator` | `CoordinateSpace`, `FindNamedSlot(name)`, `FindEnumeratedSlots(name)`, `ResolveCoordinateHost(name, type)`, `CountSlots(slotNames, enumeratorNames)`. |
| `ISlotPart` | `Slot`, `IsMeasured`, `MeasureCenter(relativeTo)`. A live handle, valid only until the next layout pass. |
| `ISlotMeasurementHost` | `ReadNodeSize()`, `ReadAnchor(slot)`, `WriteAnchor(slot, anchor)`, `InvalidateMeasure()`, `Post(Action flush)` returning whether the platform accepted the work. |
| `SlotCoordinateSpace` | `NodeLocal`, `CanvasLocal`, `ScrollSpace` — selects the `WorkflowSurfaceMath.SlotAnchorFrom*` conversion. |
| `SlotCenter` | A measured `(X, Y)` in the declared coordinate space. |
| `WorkflowLayoutBatch` | Shared queue over one owner (normally the tree). `For(owner, post)`, `Request(key, work)`, `Request(key, stamp, work)`, `Cancel(key)`, `Schedule()`, `Flush()`. |
| `WorkflowLayoutStamp` | Fingerprint of `Anchor`, `Size`, measured size and slot count; `Capture(node, measuredWidth, measuredHeight, slotCount)`. An unchanged stamp skips the expensive half. |

## Link geometry and hit testing

| Type | Role |
| --- | --- |
| `ILinkHitTestable` | Implemented on the link's helper. `Visual` is the control that drew the curve (or `null` on immediate-mode surfaces); `Curve` is the last published geometry; `SetCurve(curve, visual)`; `Contains(x, y, radius)` in the published space. |
| `LinkCurve` | Immutable flattened polyline. `FromPoints`, `BuildCubic`, `FromCubic`, `LinkCurvePoints(link, …)`, `BuildLinkCubic(link, …)`, `PortOutward`, `Contains`, `PointAtLength`, `LengthAt`, `Bounds`, `Length`. `DefaultSampleCount` is 128. |
| `LinkHitTestEx` | `DefaultHitRadius` (6), `HitTarget()`, `PublishCurve(curve, visual)`, `HitTest(x, y, radius)`, `HitTestVisibleLinks(tree, x, y, radius)`. The last walks `VisibleItems` back-to-front, skips `VirtualLink`, and ignores a link covered by a node card. |
| `SlotHitTestEx` | `DefaultHitRadius` (12), `HitTest(tree, x, y, radius)`. Finds the nearest measured slot without the visual tree. |
| `WorkflowSlotUpdateGate` / `WorkflowLinkRenderEx` | `IsLinkRenderReady(link)` and `link.IsRenderReady()`: visible plus both endpoint anchors non-`NaN` (an endpoint with no parent is ready). |

## Input

| Type | Role |
| --- | --- |
| `WorkflowInput` | One instance per tree via `For(tree)`. `Route(PointerEventArgs)`, `Route(KeyEventArgs)`, `Tree`, `PointerTarget`, `HoveredLink`, `ConnectionSender`, `PreviewReceiver`, `PreviewState`, `PreviewCheck`, `PreviewChanged`, `PreviewValidating`, `HitRadius`, `IsSuspended`, `Scroller`, `Navigator`. |
| `IInputEvents` | `InputRelay Input { get; }`. |
| `InputRelay` | `PointerEntered` / `PointerExited` / `PointerMoved` / `PointerPressed` / `PointerReleased` / `PointerWheelChanged`, `KeyDown` / `KeyUp`. |
| `PointerEventArgs` | `Position` (`Anchor`, `Layer` from the source view), `Modifiers`, `Source`, `Target`, `Handle`. |
| Pointer args | `PointerEntered/Exited/MovedEventArgs` add nothing; `PointerPressed/ReleasedEventArgs` add `Button` (`MouseButton`) and `ClickCount`; `PointerWheelEventArgs` adds `DeltaX` / `DeltaY`. |
| `KeyEventArgs` | `Key` (`InputKey`), `RawKeyCode`, `IsRepeat`, `Source`, `Target`, `Handle`; `KeyDownEventArgs` / `KeyUpEventArgs`. |
| `WorkflowEventHandle` | `PreventDefault` and `StopPropagation`. One handle per logical action, shared along the route. |

The route's chain is target → ancestors → tree; the tree passed to `For` is always last. On a pointer target change the route raises `Exited` on the old component and `Entered` on the new one before the original event. Its only default action is updating `PointerTarget` unless `IsSuspended`.

## Minimap

| Type | Role |
| --- | --- |
| `MinimapPipelineCore<TSurface, TRenderer, TViewport, TInput>` | Platform-neutral state machine: tree subscription, dirty-flag debounce, content/viewport mapping, placement, drag math, renderer call. Derive once per adapter. |
| `MinimapHostCore<…>` | Groups the three layers: `Renderer`, `Viewport`, `Input`. |
| Pipeline members | `Tree`, `Metrics`, `IsVisible`, `Invalidate()`, `Refresh()`, `Render(surface)`, `HitTest(x, y)`, `PointerPressed()`, `PointerMoved()`, `PointerReleased()`, `PointerCaptureLost()`, `RenderRequested`, `DrawContext`, `MinimapRect`, `ContentBounds`, `ViewportBounds`, `IsDirty`, `IsDragging`, `SubscribedNodeCount`, `SubscribedLinkCount`. |
| `IMinimapRenderer<TSurface>` | `Render(surface, MinimapDrawContext)` — draw the frame, mapped nodes and mapped viewport. |
| `IMinimapViewportHost` | `ScrollOffsetX/Y`, `ContentOffsetX/Y`, `ViewportWidth/Height`, `ScrollableWidth/Height`, `ApplyScroll(x, y)`. |
| `IMinimapInputHost` | `PointerPosition`, `IsPressed`, `IsCaptured`, `ClampRect`, `Capture()`, `Release()`. |
| `MinimapDrawContext` | Canvas-local `ContentBounds` / `ViewportBounds` / `Nodes`, minimap-local `MappedNodes` / `MappedViewport`, placement and radii, `GetMinimapRect(minimumSize)`. |
| `MinimapMetrics` | `Width`, `Height`, `Available`, `MinSize`, `Padding`, `NodeCornerRadius`, `MinimapCornerRadius`, `MinimumThumbSize`; `Default` is 200 × 140. |
| `SurfaceAvailableArea` | `Right`, `Top`, `Bottom`, `Margin`, `GetRect(desiredWidth, desiredHeight, minimumSize)`. |
| `WorkflowBounds` | `Left`, `Top`, `Width`, `Height`, `Right`, `Bottom`, `IsEmpty`, `FromNode(s)`, `Union`. |

The pipeline is not thread-safe. Tree notifications can arrive from the tick thread; the adapter must marshal `Invalidate` and render calls to the host thread.

## View hosting (the template side)

| Type | Role |
| --- | --- |
| `ViewPool<THost, TView, TItem, TPriorityCore>` / `ViewPoolCore<…>` | Keeps one host element in sync with a `VisibleItems` collection, materializes each item at most once, recycles by runtime type, batches creation. |
| `ViewHostCore<…>` / `IViewHost<…>` | Groups the layers: `Factory`, `Container`, `Arranger`, `Scheduler`, `Diagnostics`, `PoolPolicy`, `LayerPolicy`, `LinkLayer`, `MeasureHost`. |
| `IViewFactory<TItem, TView>` | `WantsItem`, `CreateView`. Returning `null` means "no view here" and is remembered per runtime type. |
| `IViewContainer`, `IViewArranger`, `IViewScheduler` | Visual-tree membership/binding, geometry, and the hop to the host thread. |
| `IViewPoolPolicy` | `BatchSize`, `FrameBudgetMilliseconds`, `MaxRecycledViewsPerType` (a non-positive maximum is unlimited). |
| `IViewLayerPolicy` | `GetZOrder(item)`. |
| `IViewLinkLayer<THost, TItem>` | `IsActive`, `Handles(item)`, `Render(host, items)` — a single-pass link overlay instead of one view per link. |
| `IViewMeasureHost`, `IViewDiagnostics` | Optional post-arrange measure hook and failure sink. |

Collection notifications and the pool's public members must be used on the host thread; the pool does not marshal by itself.

## Transition system

Namespace `VeloxDev.TransitionSystem` (users' types) and `VeloxDev.TransitionSystem.Abstractions` (bases).

| Type | Role |
| --- | --- |
| `InterpolatorCore` | Process-wide sampler registry and the platform seams. `RegisterInterpolator` / `UnregisterInterpolator` / `TryGetInterpolator`, `Prepare<TPriorityCore>(target, state, effect, host)`, `CreateScheduler(target, effect)`. |
| `ISampler` | `NormalizeStart`, `NormalizeEnd` (once per segment), `InsertFrame(target, property, ref working, start, end, options, t)` (once per frame). Stateless singleton. |
| `ITransitionHost<TPriorityCore>` | `IThreadDispatcher<TPriorityCore>` + `IApplicationState`; `TransitionHostBase<TPriorityCore>` is the base an adapter derives. |
| `ITransitionInterpreter<TPriorityCore>` / `TransitionInterpreterCore` | Runs one transition, one frame at a time. `Execute(target, samplerSet, effect, cts)`, `Args`, `Exit`. The base exposes `CreateFramePacer` to override. |
| `FramePacerCore` | `Schedule(continuation, interval, token)`; subclass `Arm` / `Disarm` and call `Fire()` from the host's frame/tick. |
| `TransitionSchedulerCore<THost, TInterpreter, TPriorityCore>` | `Execute(…, externCts)`, `Exit()`, static `FindOrCreate(target, canMutualTask)`. Only `FindOrCreate` files the scheduler under the target, which is what later `Transition.Pause` / `Seek` / `Exit(target)` read. |
| `ITransitionEffect<TPriorityCore>` / `ITransitionEffectCore` | `Priority`, `FPS`, `Duration`, `IsAutoReverse`, `LoopTime`, `Ease`, the lifecycle events (`Awaked`, `Start`, `Update`, `LateUpdate`, `Canceled`, `Completed`, `Finally`, `Warn`, `Error`) and their `Invoke*` methods. |
| `IFrameState` | Values, samplers and options keyed by `ITransitionProperty`; `SetValue` / `SetInterpolator` / `SetOptions` in expression, property and `PropertyInfo` forms. |
| `SamplerSet<TPriorityCore>` | Normalized endpoints plus `Apply(target, t, priority)`; marshals writes through the host. |
| `VeloxDev.Threading` | `NonPriority`, `ThreadRef`, `IThreadDispatcher<TPriorityCore>`. |

## Theme

Namespace `VeloxDev.DynamicTheme`.

| Type | Role |
| --- | --- |
| `ITheme` | Marker for a theme definition (e.g. `Light`, `Dark`). |
| `IThemeObject` | A theme-aware object: `InitializeTheme()`, `ExecuteThemeChanging` / `ExecuteThemeChanged`, `SetThemeValue<T>` / `RestoreThemeValue<T>`, `GetStaticThemeCache` / `GetActiveThemeCache`. |
| `IThemeValueConverter` | `Convert(Type targetType, string propertyName, object?[] parameters)`. Instantiated with `Activator.CreateInstance`, so it needs a public parameterless constructor. |
| `ThemeConfigAttribute<TConverter, TTheme…>` | Declares a property's theme contexts; the generator emits the cache and `InitializeTheme`. |
| `ThemeManager` | `SetPlatformInterpolator<T>(T)`, `SetCurrent<T>()`, `Current`, `StartModel`, `Jump<T>()` / `Jump(Type)`, `Transition<T>(ITransitionEffectCore)` / `Transition(Type, effect)`, `Register` / `Unregister`. |

`SetPlatformInterpolator` runs the adapter's sampler registrations and supplies the `CreateScheduler` answer Core cannot name. A switch anchors every target on one shared `ITimeSourceControl`; `Pause` / `Resume` / `SetRate` move the whole switch, while `Seek` / `Exit` act on one target.

## Tick

`VeloxDev.TimeLine` provides `[Tickable]`, `InitializeTickable()`, the `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` hooks, channels and `TickManager`. It is the general per-frame loop for a view concern, **not** the transition frame clock: animations are paced by the interpreter's `FramePacerCore`, and a custom adapter should not drive both from one timer by accident.
