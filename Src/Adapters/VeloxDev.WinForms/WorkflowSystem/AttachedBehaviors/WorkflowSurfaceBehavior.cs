using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.StandardEx;
using PlatformInput = System.Windows.Forms;
using Wf = VeloxDev.WorkflowSystem;
using XamlScrollViewer = VeloxDev.WinForms.Xaml.Controls.ScrollViewer;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The whole lifecycle of a WinForms workflow surface, attached to a plain root control.
/// </summary>
/// <remarks>
/// <para>
/// The surface is a normal <see cref="System.Windows.Forms.Control"/> (a XAML <c>UserControl</c> or <c>Panel</c>)
/// whose named parts are declared in its own markup. Set <see cref="SetIsEnabled"/> on that root and name the
/// viewport, canvas, grid decorator, minimap and pointer-press source with the corresponding attached properties;
/// the behavior resolves them by <see cref="System.Windows.Forms.Control.Name"/> and wires pan, zoom, input routing,
/// view pooling, layout scheduling, the floating ruler and teardown. No base class or constructor hand-off is needed.
/// </para>
/// <para>
/// Node views stay ordinary controls and attach <see cref="WorkflowNodeAttachment"/>; slot views expose a
/// <c>ViewModel</c> property (or use <see cref="WorkflowSlotAttachment"/> for a legacy code-first view). Links are not pooled — the behavior
/// enables <see cref="WorkflowLinkLayer"/> on the canvas, which paints every visible link in one pass. This behavior
/// only supplies the shared surface machinery.
/// </para>
/// </remarks>
public sealed class WorkflowSurfaceBehavior
{
    private static readonly ConditionalWeakTable<Control, SurfaceState> States = new();

    /// <summary>Whether the surface lifecycle is active on the control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Whether Ctrl + mouse-wheel zoom is enabled for the surface.</summary>
    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false));

    /// <summary>Name of the control that is the surface viewport.</summary>
    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Name of the control that hosts the pooled node and link views.</summary>
    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Name of the control that implements <see cref="IWorkflowGridDecorator"/>.</summary>
    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Name of the minimap control that implements <see cref="IWorkflowMinimapOverlay"/>.</summary>
    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Name of the control that receives the blank-surface press that starts a pan.</summary>
    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Simple or full type name of the node view created by the built-in pool selector.</summary>
    public static readonly DependencyProperty NodeViewTypeProperty = DependencyProperty.RegisterAttached(
        "NodeViewType", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Simple or full type name of the link view created by the built-in pool selector.</summary>
    public static readonly DependencyProperty LinkViewTypeProperty = DependencyProperty.RegisterAttached(
        "LinkViewType", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>The template selector handed to the view pool; takes precedence over the two view-type names.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector", typeof(TemplateSelector), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>The tree bound to the surface when no ancestor data context carries one.</summary>
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.RegisterAttached(
        "WorkflowTree", typeof(ITreeComponent), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnPartConfigurationChanged));

    /// <summary>Fills the link context menu the surface opens on a right press.</summary>
    public static readonly DependencyProperty LinkMenuBuilderProperty = DependencyProperty.RegisterAttached(
        "LinkMenuBuilder", typeof(Action<ContextMenuStrip, ILinkComponent>), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    /// <summary>Gets whether the surface lifecycle is active on <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(Control element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the surface lifecycle on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(Control element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets whether Ctrl + mouse-wheel zoom is enabled.</summary>
    public static bool GetZoomEnabled(Control element) => (bool)GetRequired(element, ZoomEnabledProperty)!;

    /// <summary>Sets whether Ctrl + mouse-wheel zoom is enabled.</summary>
    public static void SetZoomEnabled(Control element, bool value) => SetRequired(element, ZoomEnabledProperty, value);

    /// <summary>Gets the viewport part name.</summary>
    public static string? GetScrollViewerName(Control element) => (string?)GetRequired(element, ScrollViewerNameProperty);

    /// <summary>Names the viewport part.</summary>
    public static void SetScrollViewerName(Control element, string? value) => SetRequired(element, ScrollViewerNameProperty, value);

    /// <summary>Gets the canvas part name.</summary>
    public static string? GetCanvasName(Control element) => (string?)GetRequired(element, CanvasNameProperty);

    /// <summary>Names the canvas part.</summary>
    public static void SetCanvasName(Control element, string? value) => SetRequired(element, CanvasNameProperty, value);

    /// <summary>Gets the grid-decorator part name.</summary>
    public static string? GetGridDecoratorName(Control element) => (string?)GetRequired(element, GridDecoratorNameProperty);

    /// <summary>Names the grid-decorator part.</summary>
    public static void SetGridDecoratorName(Control element, string? value) => SetRequired(element, GridDecoratorNameProperty, value);

    /// <summary>Gets the minimap part name.</summary>
    public static string? GetMinimapOverlayName(Control element) => (string?)GetRequired(element, MinimapOverlayNameProperty);

    /// <summary>Names the minimap part.</summary>
    public static void SetMinimapOverlayName(Control element, string? value) => SetRequired(element, MinimapOverlayNameProperty, value);

    /// <summary>Gets the pointer-press-source part name.</summary>
    public static string? GetPointerPressSourceName(Control element) => (string?)GetRequired(element, PointerPressSourceNameProperty);

    /// <summary>Names the pointer-press-source part.</summary>
    public static void SetPointerPressSourceName(Control element, string? value) => SetRequired(element, PointerPressSourceNameProperty, value);

    /// <summary>Gets the node view type name used by the built-in pool selector.</summary>
    public static string? GetNodeViewType(Control element) => (string?)GetRequired(element, NodeViewTypeProperty);

    /// <summary>Sets the node view type name used by the built-in pool selector.</summary>
    public static void SetNodeViewType(Control element, string? value) => SetRequired(element, NodeViewTypeProperty, value);

    /// <summary>Gets the link view type name used by the built-in pool selector.</summary>
    public static string? GetLinkViewType(Control element) => (string?)GetRequired(element, LinkViewTypeProperty);

    /// <summary>Sets the link view type name used by the built-in pool selector.</summary>
    public static void SetLinkViewType(Control element, string? value) => SetRequired(element, LinkViewTypeProperty, value);

    /// <summary>Gets the template selector handed to the view pool.</summary>
    public static TemplateSelector? GetTemplateSelector(Control element)
        => (TemplateSelector?)GetRequired(element, TemplateSelectorProperty);

    /// <summary>Sets the template selector handed to the view pool.</summary>
    public static void SetTemplateSelector(Control element, TemplateSelector? value)
        => SetRequired(element, TemplateSelectorProperty, value);

    /// <summary>Gets the explicitly bound workflow tree.</summary>
    public static ITreeComponent? GetWorkflowTree(Control element)
        => (ITreeComponent?)GetRequired(element, WorkflowTreeProperty);

    /// <summary>Binds a workflow tree explicitly; otherwise the surface resolves one from an ancestor data context.</summary>
    public static void SetWorkflowTree(Control element, ITreeComponent? value)
        => SetRequired(element, WorkflowTreeProperty, value);

    /// <summary>Gets the link-menu builder.</summary>
    public static Action<ContextMenuStrip, ILinkComponent>? GetLinkMenuBuilder(Control element)
        => (Action<ContextMenuStrip, ILinkComponent>?)GetRequired(element, LinkMenuBuilderProperty);

    /// <summary>Sets the builder that fills the link context menu.</summary>
    public static void SetLinkMenuBuilder(Control element, Action<ContextMenuStrip, ILinkComponent>? value)
        => SetRequired(element, LinkMenuBuilderProperty, value);

    /// <summary>
    /// Re-reads the named parts, pushes the current layout into the viewport, grid decorator, ruler and minimap,
    /// re-places the pooled views and writes the visible viewport back to the model.
    /// </summary>
    /// <param name="host">The root control the surface is attached to.</param>
    public static void Refresh(Control host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        var state = GetState(host);
        EnsureParts(host, state);

        if (state.Attached)
        {
            EnsureTree(host, state);
        }
        else if (!ReferenceEquals(state.Tree, ResolveTree(host)))
        {
            state.Tree = ResolveTree(host);
        }

        CaptureViewportRestore(state);
        ApplyPan(host, state);
        PersistViewport(state);
        QueueViewportRestore(host, state);
    }

    // ── attached-property plumbing ──────────────────────────────────────────

    private static object? GetRequired(Control element, DependencyProperty property)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return property.GetValue(element);
    }

    private static void SetRequired(Control element, DependencyProperty property, object? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        property.SetValue(element, value);
    }

    private static void OnIsEnabledChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        if (Equals(e.NewValue, true))
        {
            Attach(target);
        }
        else
        {
            Detach(target);
        }
    }

    private static void OnPartConfigurationChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        if (!States.TryGetValue(target, out var state) || !state.Attached)
        {
            return;
        }

        EnsureParts(target, state);
        EnsureTree(target, state);
        ScheduleLayout(target, state);
    }

    // ── lifecycle ────────────────────────────────────────────────────────────

    private static void Attach(Control host)
    {
        var state = GetState(host);
        if (state.Attached)
        {
            return;
        }

        state.Attached = true;
        state.EnsureMessageFilter();

        // 画布与卡片是两套子窗口：父窗口重绘前剪掉子窗口区域，顶层窗体再交给 DWM 合成，闪烁才不会漏出来。
        NativeWindowStyleHelper.EnsureClipChildren(host);
        NativeWindowStyleHelper.EnsureComposited(host);

        host.HandleCreated += OnHostHandleCreated;
        host.Disposed += OnHostDisposed;
        host.Layout += OnHostLayout;
        host.SizeChanged += OnHostSizeChanged;
        host.ParentChanged += OnHostParentChanged;
        host.ControlAdded += OnHostControlAdded;
        host.ControlRemoved += OnHostControlRemoved;

        SubscribeDataContext(host, state);

        EnsureParts(host, state);
        EnsureTree(host, state);
        Refresh(host);
    }

    private static void Detach(Control host)
    {
        if (!States.TryGetValue(host, out var state) || !state.Attached)
        {
            return;
        }

        state.Attached = false;
        state.RemoveMessageFilter();

        host.HandleCreated -= OnHostHandleCreated;
        host.Disposed -= OnHostDisposed;
        host.Layout -= OnHostLayout;
        host.SizeChanged -= OnHostSizeChanged;
        host.ParentChanged -= OnHostParentChanged;
        host.ControlAdded -= OnHostControlAdded;
        host.ControlRemoved -= OnHostControlRemoved;

        UnsubscribeDataContext(host, state);

        DetachTree(host, state);
        ClearParts(state);
        state.EndInitialReveal();
        state.ReleasePointerWatch();
        state.ReleaseZoomTimer();
        state.PanOffset = new Point(0, 0);
        state.HasPendingRestore = false;
        state.LastRestoreTree = null;

        States.Remove(host);
    }

    private static void OnHostDisposed(object? sender, EventArgs e)
    {
        if (sender is Control host)
        {
            Detach(host);
        }
    }

    private static void OnHostHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control host)
        {
            EnsureParts(host, GetState(host));
            EnsureTree(host, GetState(host));
            Refresh(host);
        }
    }

    private static void OnHostLayout(object? sender, LayoutEventArgs e)
    {
        if (sender is Control host)
        {
            ScheduleLayout(host, GetState(host));
        }
    }

    private static void OnHostSizeChanged(object? sender, EventArgs e)
    {
        if (sender is Control host)
        {
            ScheduleLayout(host, GetState(host));
        }
    }

    private static void OnHostParentChanged(object? sender, EventArgs e)
    {
        if (sender is Control host)
        {
            ScheduleLayout(host, GetState(host));
        }
    }

    private static void OnHostControlAdded(object? sender, ControlEventArgs e)
    {
        if (sender is Control host)
        {
            ScheduleLayout(host, GetState(host));
        }
    }

    private static void OnHostControlRemoved(object? sender, ControlEventArgs e)
    {
        if (sender is Control host)
        {
            ScheduleLayout(host, GetState(host));
        }
    }

    private static void SubscribeDataContext(Control host, SurfaceState state)
    {
        state.DataContextHandler = OnHostDataContextChanged;

        // XAML Control 有自己的 DataContext 事件；原生宿主没有这个成员（只有 BindingContextChanged）。
        if (host is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.DataContextChanged += state.DataContextHandler;
        }
        else
        {
#if NET8_0_OR_GREATER
            host.DataContextChanged += state.DataContextHandler;
#else
            host.BindingContextChanged += state.DataContextHandler;
#endif
        }
    }

    private static void UnsubscribeDataContext(Control host, SurfaceState state)
    {
        if (state.DataContextHandler is null)
        {
            return;
        }

        if (host is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.DataContextChanged -= state.DataContextHandler;
        }
        else
        {
#if NET8_0_OR_GREATER
            host.DataContextChanged -= state.DataContextHandler;
#else
            host.BindingContextChanged -= state.DataContextHandler;
#endif
        }

        state.DataContextHandler = null;
    }

    private static void OnHostDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Control host)
        {
            var state = GetState(host);
            EnsureParts(host, state);
            EnsureTree(host, state);
            ScheduleLayout(host, state);
        }
    }

    // ── parts ────────────────────────────────────────────────────────────────

    private static void EnsureParts(Control host, SurfaceState state)
    {
        var scrollViewer = FindNamed(host, GetScrollViewerName(host)) ?? host;
        if (!ReferenceEquals(state.ScrollViewer, scrollViewer))
        {
            state.ScrollViewer = scrollViewer;
        }

        // 只有 XAML 层的 ScrollViewer 提供滚动条与 ScrollOffset；旧的 ScrollableControl 视口保持原样。
        var xamlViewer = scrollViewer as XamlScrollViewer;
        if (!ReferenceEquals(state.XamlScrollViewer, xamlViewer))
        {
            UnhookXamlScrollViewer(state);
            state.XamlScrollViewer = xamlViewer;
        }

        if (state.Attached && xamlViewer is not null && !state.XamlScrollViewerHooked)
        {
            HookXamlScrollViewer(state);
        }

        var canvas = FindNamed(host, GetCanvasName(host));
        if (!ReferenceEquals(state.Canvas, canvas))
        {
            if (state.Canvas is { } previousCanvas)
            {
                WorkflowLinkLayer.SetIsEnabled(previousCanvas, false);
                UnhookCanvas(state, previousCanvas);
                ViewPool.SetItemsSource(previousCanvas, null);
                if (state.PoolSelector is not null)
                {
                    ViewPool.SetTemplateSelector(previousCanvas, null);
                }

                state.PoolCanvas = null;
                state.PoolSelector = null;
                state.PoolItemsSource = null;
            }

            state.Canvas = canvas;
            state.CanvasHooked = false;
        }

        if (state.Attached && canvas is not null && !state.CanvasHooked)
        {
            HookCanvas(state, canvas);
            state.CanvasHooked = true;
        }

        // 连线不再按条池化成窗口：画布的 Paint 一趟画完（WorkflowLinkLayer），池的 ViewLinkLayer 只挡下连线项。
        if (state.Attached && canvas is not null)
        {
            WorkflowLinkLayer.SetIsEnabled(canvas, true);
            WorkflowLinkLayer.Refresh(canvas);
        }

        // 合成层仍由 NativeWindowStyleHelper 按子窗口数量决定：小型 workflow 表面启用 WS_EX_COMPOSITED，
        // 整棵窗口树一帧合成；大型工作台超过阈值时保留 WS_CLIPCHILDREN，避免 DWM 为几百个子窗口维护表面。
        var gridDecorator = FindNamed(host, GetGridDecoratorName(host));
        if (gridDecorator is null && canvas is IWorkflowGridDecorator)
        {
            // 旧式画布本身就是装饰器；新式模板会显式命名独立的装饰器。
            gridDecorator = canvas;
        }

        state.GridDecorator = gridDecorator;

        var minimap = FindNamed(host, GetMinimapOverlayName(host));
        if (!ReferenceEquals(state.MinimapOverlay, minimap))
        {
            if (state.MinimapHooked && state.MinimapOverlay is IWorkflowMinimapScrollSource previousMinimap)
            {
                previousMinimap.ViewportScrollRequested -= state.OnMinimapScrollRequested;
            }

            state.MinimapOverlay = minimap;
            state.MinimapHooked = false;
        }

        if (state.Attached && minimap is IWorkflowMinimapScrollSource source && !state.MinimapHooked)
        {
            source.ViewportScrollRequested += state.OnMinimapScrollRequested;
            state.MinimapHooked = true;
        }

        // 视口、画布与装饰器都是自绘/透明窗口：父窗口重画前剪掉子窗口区域，子窗口才会各自重画，
        // 不会被一次画布失效拖着整棵树一起重排重绘。句柄重建后样式会丢，Ensure* 自己挂在 HandleCreated 上。
        if (state.ScrollViewer is { } scrollViewerPart)
        {
            NativeWindowStyleHelper.EnsureClipChildren(scrollViewerPart);
        }

        if (canvas is { } canvasPart)
        {
            NativeWindowStyleHelper.EnsureClipChildren(canvasPart);
        }

        if (state.GridDecorator is { } gridPart)
        {
            NativeWindowStyleHelper.EnsureClipChildren(gridPart);
        }

        var pressSource = FindNamed(host, GetPointerPressSourceName(host));
        if (!ReferenceEquals(state.PointerPressSource, pressSource))
        {
            UnhookPressSource(state);
            state.PointerPressSource = pressSource;
            state.PressSourceHooked = false;
        }

        // 同一个控件已经按画布挂过一次，别再挂第二份处理器。
        if (state.Attached && pressSource is not null
            && !ReferenceEquals(pressSource, canvas) && !state.PressSourceHooked)
        {
            HookPressSource(state);
            state.PressSourceHooked = true;
        }

        if (state.Attached)
        {
            EnsureRulerOverlay(host, state);
        }
    }

    private static void ClearParts(SurfaceState state)
    {
        if (state.Canvas is { } canvas)
        {
            ViewPool.SetItemsSource(canvas, null);
            if (state.PoolSelector is not null)
            {
                ViewPool.SetTemplateSelector(canvas, null);
            }

            WorkflowLinkLayer.SetIsEnabled(canvas, false);
            UnhookCanvas(state, canvas);
            state.Canvas = null;
        }

        UnhookPressSource(state);
        state.PointerPressSource = null;

        if (state.MinimapHooked && state.MinimapOverlay is IWorkflowMinimapScrollSource minimap)
        {
            minimap.ViewportScrollRequested -= state.OnMinimapScrollRequested;
        }

        UnhookXamlScrollViewer(state);
        state.XamlScrollViewer = null;

        state.MinimapHooked = false;
        state.MinimapOverlay = null;
        state.CanvasHooked = false;
        state.PressSourceHooked = false;
        state.GridDecorator = null;
        state.ScrollViewer = null;
        state.PoolCanvas = null;
        state.PoolSelector = null;
        state.PoolItemsSource = null;

        DisposeRulerOverlay(state);
    }

    private static Control? FindNamed(Control root, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var control in EnumerateSelfAndDescendants(root))
        {
            if (string.Equals(control.Name, name, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
    {
        yield return root;
        foreach (var descendant in EnumerateDescendants(root))
        {
            yield return descendant;
        }
    }

    private static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateDescendants(child))
            {
                yield return nested;
            }
        }
    }

    // ── tree and view pool ───────────────────────────────────────────────────

    private static void EnsureTree(Control host, SurfaceState state)
    {
        var resolved = GetWorkflowTree(host) ?? ResolveTree(host);
        if (!ReferenceEquals(resolved, state.Tree))
        {
            AttachTree(host, state, resolved);
            return;
        }

        EnsurePool(host, state);
    }

    private static void AttachTree(Control host, SurfaceState state, ITreeComponent? tree)
    {
        DetachTree(host, state);
        state.Tree = tree;
        if (tree is null)
        {
            return;
        }

        if (tree is INotifyPropertyChanged notifier)
        {
            state.TreeNotifier = notifier;
            notifier.PropertyChanged += state.OnTreePropertyChanged;
        }

        RegisterScroller(state, tree);
        AttachLinkInput(host, state);
        EnsurePool(host, state);
        ScheduleLayout(host, state);
    }

    private static void DetachTree(Control host, SurfaceState state)
    {
        if (state.TreeNotifier is not null)
        {
            state.TreeNotifier.PropertyChanged -= state.OnTreePropertyChanged;
            state.TreeNotifier = null;
        }

        DetachLinkInput(state);
        ClearScroller(state);

        if (state.PoolCanvas is { } canvas)
        {
            ViewPool.SetItemsSource(canvas, null);
            if (state.PoolSelector is not null)
            {
                ViewPool.SetTemplateSelector(canvas, null);
            }
        }

        state.PoolCanvas = null;
        state.PoolSelector = null;
        state.PoolItemsSource = null;
        state.Tree = null;
    }

    private static void EnsurePool(Control host, SurfaceState state)
    {
        if (state.Canvas is not { } canvas || state.Tree is not { } tree)
        {
            return;
        }

        if (!ReferenceEquals(state.PoolCanvas, canvas))
        {
            state.PoolCanvas = canvas;
            state.PoolItemsSource = null;
            state.PoolSelector = null;
        }

        var selector = GetTemplateSelector(host) ?? BuildGeneratedSelector(host, state);
        if (selector is not null)
        {
            if (!ReferenceEquals(state.PoolSelector, selector))
            {
                ViewPool.SetTemplateSelector(canvas, selector);
                state.PoolSelector = selector;
            }
        }
        else if (state.PoolSelector is not null)
        {
            ViewPool.SetTemplateSelector(canvas, null);
            state.PoolSelector = null;
        }

        var items = tree.GetHelper().VisibleItems;
        if (!ReferenceEquals(state.PoolItemsSource, items))
        {
            // 首次换源：先冻结表面，等 Core 池把所有可见视图物化完再一次性显示。
            state.BeginInitialReveal(host, canvas, state.GridDecorator);
            ViewPool.SetItemsSource(canvas, items);
            state.PoolItemsSource = items;
        }
    }

    private static TemplateSelector? BuildGeneratedSelector(Control host, SurfaceState state)
    {
        var nodeType = GetNodeViewType(host);
        var linkType = GetLinkViewType(host);
        if (string.IsNullOrWhiteSpace(nodeType) && string.IsNullOrWhiteSpace(linkType))
        {
            state.GeneratedSelector = null;
            state.GeneratedNodeType = null;
            state.GeneratedLinkType = null;
            return null;
        }

        if (state.GeneratedSelector is not null
            && string.Equals(state.GeneratedNodeType, nodeType, StringComparison.Ordinal)
            && string.Equals(state.GeneratedLinkType, linkType, StringComparison.Ordinal))
        {
            return state.GeneratedSelector;
        }

        state.GeneratedNodeType = nodeType;
        state.GeneratedLinkType = linkType;
        state.GeneratedSelector = new SurfaceTemplateSelector(
            host.GetType().Assembly, host.GetType().Namespace, nodeType, linkType);
        return state.GeneratedSelector;
    }

    private static void RegisterScroller(SurfaceState state, ITreeComponent tree)
    {
        ClearScroller(state);
        var scroller = new SurfaceScroller(state);
        state.Scroller = scroller;

        var input = WorkflowInput.For(tree);
        input.Scroller = scroller;
        input.Navigator = scroller;
    }

    private static void ClearScroller(SurfaceState state)
    {
        if (state.Tree is { } tree && state.Scroller is { } scroller)
        {
            var input = WorkflowInput.For(tree);
            if (ReferenceEquals(input.Scroller, scroller))
            {
                input.Scroller = null;
                input.Navigator = null;
            }
        }

        state.Scroller = null;
    }

    // ── layout and pan ───────────────────────────────────────────────────────

    private static void ScheduleLayout(Control host, SurfaceState state)
    {
        if (state.LayoutPending || host.IsDisposed)
        {
            return;
        }

        state.LayoutPending = true;
        void Update()
        {
            state.LayoutPending = false;
            if (host.IsDisposed)
            {
                return;
            }

            EnsureParts(host, state);
            Refresh(host);
        }

        if (host.IsHandleCreated)
        {
            try
            {
                host.BeginInvoke(new Action(Update));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                // 窗口正在销毁：这一次布局不再排，控件已经没得画了。
                state.LayoutPending = false;
            }
        }
        else
        {
            state.LayoutPending = false;
        }
    }

    private static void ApplyCanvasSize(SurfaceState state)
    {
        if (state.Canvas is not { } canvas || (canvas.Width > 0 && canvas.Height > 0))
        {
            return;
        }

        canvas.Size = ResolveClientSize(state);
    }

    private static void ApplyPan(Control host, SurfaceState state)
    {
        if (host.IsDisposed)
        {
            return;
        }

        // 小地图拖动、布局刷新是「改一批子窗口几何 + 最后重画」，需要整帧提交。直接拖动画布是
        // 每帧都改 PanOffset 的手势，之前已经靠 canvas.Update 保证跟手；再套 WM_SETREDRAW 会在
        // 每次鼠标移动时冻/解整棵窗口树，反而造成高频闪烁，所以画笔手势走原路径。
        if (state.IsPanning || host.Capture)
        {
            ApplyPanCore(host, state);
            return;
        }

        // 逐个子窗口更新时，WinForms 可能先画到一半状态；这里把 host/canvas/grid/所有池化视图的
        // WM_SETREDRAW 一起关掉，最后逐级恢复并统一重绘一次。缩放/首次物化已有更外层 scope，
        // 引用计数会正确嵌套。
        var targets = new List<Control>();
        var seen = new HashSet<Control>();
        void Freeze(Control? control)
        {
            if (control is null || !seen.Add(control))
            {
                return;
            }

            targets.Add(control);
            NativeWindowStyleHelper.BeginRedraw(control);
        }

        Freeze(host);
        Freeze(state.Canvas);
        Freeze(state.GridDecorator);
        if (state.Canvas is { } canvasPart)
        {
            foreach (Control child in canvasPart.Controls)
            {
                Freeze(child);
            }
        }

        try
        {
            ApplyPanCore(host, state);
        }
        finally
        {
            for (var index = targets.Count - 1; index >= 0; index--)
            {
                NativeWindowStyleHelper.EndRedraw(targets[index]);
            }
        }
    }

    private static void ApplyPanCore(Control host, SurfaceState state)
    {
        ApplyCanvasSize(state);

        var tree = state.Tree;
        var layout = tree?.Layout;

        FoldPositivePan(state, layout);

        // 负 Pan = 视口向右/下滚。滚过 ActualSize 的右/下边界时，把超出的部分写进 PositiveOffset，
        // 否则画布范围不变，滚动条最大值也停在旧位置，向右侧延展后拖动条仍按旧范围走。
        if (layout is not null && state.PanOffset.X < 0 || layout is not null && state.PanOffset.Y < 0)
        {
            var viewport = ResolveClientSize(state);
            var maxX = Math.Max(0d, layout.ActualSize.Width - viewport.Width);
            var maxY = Math.Max(0d, layout.ActualSize.Height - viewport.Height);

            if (state.PanOffset.X < 0)
            {
                var desiredX = -state.PanOffset.X;
                var appliedX = WorkflowSurfaceMath.ClampScrollOffset(desiredX, maxX, layout, horizontal: true);
                if (appliedX != desiredX)
                {
                    // 正向越界刚刚写入 PositiveOffset，ActualSize 已经变大；旧的 max 不能继续
                    // 夹住目标，否则一次定位/拖动只能滚到旧边界，下一帧再从错误位置起算。
                    var grownMaxX = Math.Max(0d, layout.ActualSize.Width - viewport.Width);
                    appliedX = Math.Min(desiredX, grownMaxX);
                    state.PanOffset = new Point(-(int)Math.Round(appliedX), state.PanOffset.Y);
                }
            }

            if (state.PanOffset.Y < 0)
            {
                var desiredY = -state.PanOffset.Y;
                var appliedY = WorkflowSurfaceMath.ClampScrollOffset(desiredY, maxY, layout, horizontal: false);
                if (appliedY != desiredY)
                {
                    var grownMaxY = Math.Max(0d, layout.ActualSize.Height - viewport.Height);
                    appliedY = Math.Min(desiredY, grownMaxY);
                    state.PanOffset = new Point(state.PanOffset.X, -(int)Math.Round(appliedY));
                }
            }
        }

        if (state.Attached)
        {
            EnsureRulerOverlay(host, state);
        }

        var content = layout?.ActualOffset ?? new Offset();
        var contentVisual = VisualContentOffset(state);
        var rulerBand = contentVisual.Horizontal - content.Horizontal;

        // 先把滚动宿主的 Offset 落下去，再量节点/插槽：否则插槽锚点量到的是旧视口，
        // 而连线的模型坐标用新偏移还原，端点会随平移一帧一帧地漂，曲线的出线方向就会翻。
        if (state.XamlScrollViewer is { } viewportViewer)
        {
            SyncXamlScrollViewer(state, viewportViewer, layout, ResolveClientSize(state));
        }

        if (state.Canvas is { } canvas)
        {
            WorkflowLinkAttachment.BeginBatch();
            try
            {
                foreach (Control child in canvas.Controls.OfType<Control>().ToArray())
                {
                    HookView(state, child);

                    if (WorkflowNodeAttachment.For(child) is { } card)
                    {
                        card.ApplySurfacePosition(state.PanOffset, contentVisual);
                        WorkflowSlotLayoutBehavior.SyncNow(child);
                    }

                    WorkflowLinkAttachment.For(child)?.ApplySurfacePosition(state.PanOffset, contentVisual);
                }
            }
            finally
            {
                WorkflowLinkAttachment.EndBatch();
            }
        }

        if (state.GridDecorator is IWorkflowGridDecorator grid)
        {
            grid.ScrollOffsetX = -state.PanOffset.X;
            grid.ScrollOffsetY = -state.PanOffset.Y;
            grid.ContentOffsetX = contentVisual.Horizontal;
            grid.ContentOffsetY = contentVisual.Vertical;

            // 画布是透明的且铺满装饰器：它的 WM_PAINT 会把网格背景印进画布 DC，装饰器自己的
            // 同步重画是同一趟工作再做一遍。这里只排队，不再 Update()。
            state.GridDecorator.Invalidate();
        }

        // 连线的曲线几何在画布上单层绘制；装饰器的 scroll/content 已经更新，现在重发一次，
        // 命中与悬停不读上一帧的旧曲线。
        if (state.Canvas is { } linkCanvas)
        {
            WorkflowLinkLayer.Refresh(linkCanvas);

            // 重发之后再同步重画：拖动画布时鼠标消息比 WM_PAINT 先到，异步重绘会拖影；
            // 先 Refresh 再 Update，同一帧里画出来的就是这一步的新几何，而不是上一帧的。
            if (state.IsPanning || host.Capture)
            {
                linkCanvas.Update();
            }
        }

        SyncRulerOverlay(state, contentVisual);

        if (state.MinimapOverlay is IWorkflowMinimapOverlay minimap)
        {
            var size = ResolveClientSize(state);
            minimap.ScrollOffsetX = -state.PanOffset.X;
            minimap.ScrollOffsetY = -state.PanOffset.Y;
            minimap.ContentOffsetX = contentVisual.Horizontal;
            minimap.ContentOffsetY = contentVisual.Vertical;
            minimap.ViewportWidth = size.Width;
            minimap.ViewportHeight = size.Height;
            minimap.WorkflowTree = tree;
            state.MinimapOverlay.Invalidate();
        }

        if (tree is not null)
        {
            tree.SetVirtualizeInset(left: rulerBand, top: rulerBand);
            var size = ResolveClientSize(state);
            try
            {
                tree.GetHelper().Viewport = new Viewport(
                    -state.PanOffset.X - content.Horizontal,
                    -state.PanOffset.Y - content.Vertical,
                    size.Width,
                    size.Height);
            }
            catch
            {
                // 有些宿主上的树 helper 不支持写视口；忽略。
            }
        }

        WorkflowCanvasTransformBehavior.Apply(host, content);
        RefreshLinkHover(host, state);
    }

    // ScrollViewer 的 Offset 是「滚动坐标」：viewportWorld + ActualOffset（也就是 -PanOffset）。
    // 不能把 ActualOffset 再减一次；拿视图世界坐标直接喂滚动条，会在画布向左/上扩展后把 thumb
    // 钉回 0，拖动条自然从旧起点开始。
    private static void SyncXamlScrollViewer(
        SurfaceState state, XamlScrollViewer viewer, CanvasLayout? layout, System.Drawing.Size viewport)
    {
        if (state.SyncingXamlScrollViewer)
        {
            return;
        }

        state.SyncingXamlScrollViewer = true;
        try
        {
            viewer.ViewportWidth = viewport.Width;
            viewer.ViewportHeight = viewport.Height;
            viewer.ExtentWidth = layout?.ActualSize.Width ?? 0d;
            viewer.ExtentHeight = layout?.ActualSize.Height ?? 0d;
            viewer.ScrollOffset = new Point(
                (int)Math.Round((double)-state.PanOffset.X),
                (int)Math.Round((double)-state.PanOffset.Y));
        }
        finally
        {
            state.SyncingXamlScrollViewer = false;
        }
    }

    private static void OnXamlScrollViewerScrolled(SurfaceState state)
    {
        if (state.SyncingXamlScrollViewer || state.XamlScrollViewer is not { } viewer)
        {
            return;
        }

        var offset = viewer.ScrollOffset;
        SetScrollOffset(state.Host, state, offset.X, offset.Y);
    }

    private static void HookXamlScrollViewer(SurfaceState state)
    {
        if (state.XamlScrollViewer is not { } viewer)
        {
            return;
        }

        state.XamlScrollViewerScroll = (_, _) => OnXamlScrollViewerScrolled(state);
        viewer.Scroll += state.XamlScrollViewerScroll;
        state.XamlScrollViewerHooked = true;
    }

    private static void UnhookXamlScrollViewer(SurfaceState state)
    {
        if (state.XamlScrollViewerHooked
            && state.XamlScrollViewer is { } viewer
            && state.XamlScrollViewerScroll is { } handler)
        {
            viewer.Scroll -= handler;
        }

        state.XamlScrollViewerHooked = false;
        state.XamlScrollViewerScroll = null;
    }

    // 正向平移折进 NegativeOffset：画布只会往右下长，折进去之后 PanOffset 归零，锚点继续落在这个新原点附近。
    // 缩放枢轴目标（PivotCenterScroll）读的是 ActualOffset，所以必须在读它之前调用一次。
    private static void FoldPositivePan(SurfaceState state, CanvasLayout? layout)
    {
        if (layout is null || (state.PanOffset.X <= 0 && state.PanOffset.Y <= 0))
        {
            return;
        }

        var growX = Math.Max(0, state.PanOffset.X);
        var growY = Math.Max(0, state.PanOffset.Y);
        layout.NegativeOffset += new Offset(growX, growY);

        // 折进 NegativeOffset 之后，拖拽起点也要按同一量回退，否则下一次增量会把同一段再折一遍。
        // 不能改 PanPressScreen：下一帧公式是 PanOffsetAtPress + (cursor - PanPressScreen)，
        // 改起点而不改锚点，恰好会把 grow 再加回来。
        state.PanOffsetAtPress = new Point(
            state.PanOffsetAtPress.X - growX,
            state.PanOffsetAtPress.Y - growY);
        state.PanOffset = new Point(state.PanOffset.X - growX, state.PanOffset.Y - growY);
    }

    private static void SetScrollOffset(Control host, SurfaceState state, double scrollX, double scrollY)
    {
        state.PanOffset = new Point(
            (int)Math.Round(-scrollX),
            (int)Math.Round(-scrollY));
        ApplyPan(host, state);
    }

    private static void PersistViewport(SurfaceState state)
    {
        if (state.Tree?.Layout is not { } layout)
        {
            return;
        }

        layout.ViewportOffset = WorkflowSurfaceMath.ViewportOffsetFromScroll(
            -state.PanOffset.X, -state.PanOffset.Y, layout);
    }

    private static Offset VisualContentOffset(SurfaceState state)
    {
        var content = state.Tree?.Layout?.ActualOffset ?? new Offset();
        var band = (state.GridDecorator as IWorkflowGridDecorator)?.RulerBand ?? 0d;
        return new Offset(content.Horizontal + band, content.Vertical + band);
    }

    private static System.Drawing.Size ResolveClientSize(SurfaceState state)
    {
        if (state.ScrollViewer is { ClientSize.Width: > 0, ClientSize.Height: > 0 } viewer)
        {
            return viewer.ClientSize;
        }

        if (state.Canvas is { ClientSize.Width: > 0, ClientSize.Height: > 0 } canvas)
        {
            return canvas.ClientSize;
        }

        var host = state.Host;
        return host.ClientSize.Width > 0 ? host.ClientSize : host.Size;
    }

    // ── viewport restore ─────────────────────────────────────────────────────

    private static void CaptureViewportRestore(SurfaceState state)
    {
        var tree = state.Tree;
        if (ReferenceEquals(tree, state.LastRestoreTree))
        {
            return;
        }

        state.LastRestoreTree = tree;
        state.HasPendingRestore = false;

        if (tree is null || !WorkflowSurfaceMath.HasViewportRestore(tree.Layout))
        {
            return;
        }

        var scroll = WorkflowSurfaceMath.ViewportRestoreScroll(tree.Layout);
        state.PendingRestoreX = scroll.Horizontal;
        state.PendingRestoreY = scroll.Vertical;
        state.HasPendingRestore = true;
    }

    private static void QueueViewportRestore(Control host, SurfaceState state)
    {
        if (!state.HasPendingRestore || state.RestoreQueued || !host.IsHandleCreated)
        {
            return;
        }

        state.RestoreQueued = true;
        try
        {
            host.BeginInvoke(new Action(() =>
            {
                state.RestoreQueued = false;

                if (!state.HasPendingRestore || !state.Attached || host.IsDisposed)
                {
                    return;
                }

                state.HasPendingRestore = false;
                SetScrollOffset(host, state, state.PendingRestoreX, state.PendingRestoreY);
                Refresh(host);
            }));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // 窗口正在销毁：恢复留给下一次挂载，别在 teardown 里抛。
            state.RestoreQueued = false;
        }
    }

    // ── canvas hooks ─────────────────────────────────────────────────────────

    private static void HookCanvas(SurfaceState state, Control canvas)
    {
        canvas.MouseDown += state.OnCanvasMouseDown;
        canvas.MouseMove += state.OnCanvasMouseMove;
        canvas.MouseUp += state.OnCanvasMouseUp;
        canvas.MouseCaptureChanged += state.OnCanvasMouseCaptureChanged;
        canvas.MouseEnter += state.OnCanvasMouseEnter;
        canvas.MouseLeave += state.OnCanvasMouseLeave;
        canvas.KeyDown += state.OnCanvasKeyDown;
        canvas.KeyUp += state.OnCanvasKeyUp;
        canvas.ControlAdded += state.OnCanvasControlAdded;
    }

    private static void UnhookCanvas(SurfaceState state, Control canvas)
    {
        state.CanvasHooked = false;
        canvas.MouseDown -= state.OnCanvasMouseDown;
        canvas.MouseMove -= state.OnCanvasMouseMove;
        canvas.MouseUp -= state.OnCanvasMouseUp;
        canvas.MouseCaptureChanged -= state.OnCanvasMouseCaptureChanged;
        canvas.MouseEnter -= state.OnCanvasMouseEnter;
        canvas.MouseLeave -= state.OnCanvasMouseLeave;
        canvas.KeyDown -= state.OnCanvasKeyDown;
        canvas.KeyUp -= state.OnCanvasKeyUp;
        canvas.ControlAdded -= state.OnCanvasControlAdded;
    }

    private static void HookPressSource(SurfaceState state)
    {
        if (state.PointerPressSource is not { } source)
        {
            return;
        }

        source.MouseDown += state.OnCanvasMouseDown;
        source.MouseMove += state.OnCanvasMouseMove;
        source.MouseUp += state.OnCanvasMouseUp;
        source.MouseCaptureChanged += state.OnCanvasMouseCaptureChanged;
        source.MouseEnter += state.OnCanvasMouseEnter;
        source.MouseLeave += state.OnCanvasMouseLeave;
    }

    private static void UnhookPressSource(SurfaceState state)
    {
        state.PressSourceHooked = false;
        if (state.PointerPressSource is not { } source)
        {
            return;
        }

        source.MouseDown -= state.OnCanvasMouseDown;
        source.MouseMove -= state.OnCanvasMouseMove;
        source.MouseUp -= state.OnCanvasMouseUp;
        source.MouseCaptureChanged -= state.OnCanvasMouseCaptureChanged;
        source.MouseEnter -= state.OnCanvasMouseEnter;
        source.MouseLeave -= state.OnCanvasMouseLeave;
    }

    private static void HookView(SurfaceState state, Control view)
    {
        if (state.ViewHooks.Add(view))
        {
            view.VisibleChanged += state.OnCanvasViewVisibilityChanged;
        }

        if (WorkflowNodeAttachment.For(view) is { } node)
        {
            state.AnchorHooks.GetValue(node, _ =>
            {
                node.AnchorChanged += state.OnNodeAnchorChanged;
                return new object();
            });
        }
    }

    private static void ApplyViewProjection(SurfaceState state, Control view)
    {
        var contentVisual = VisualContentOffset(state);
        if (WorkflowNodeAttachment.For(view) is { } node)
        {
            node.ApplySurfacePosition(state.PanOffset, contentVisual);
            WorkflowSlotLayoutBehavior.SyncNow(view);
        }

        if (WorkflowLinkAttachment.For(view) is { } link)
        {
            link.ApplySurfacePosition(state.PanOffset, contentVisual);
            link.Refresh();
        }

        WorkflowNodeDragBehavior.RefreshHooks(view);
    }

    // ── ruler overlay ────────────────────────────────────────────────────────

    private static void EnsureRulerOverlay(Control host, SurfaceState state)
    {
        if (state.RulerOverlay is { } existing)
        {
            if (!existing.IsDisposed)
            {
                return;
            }

            // 句柄被重建后旧弹窗已销毁：先摘掉它挂在宿主/owner 上的订阅，再建新的。
            DisposeRulerOverlay(state);
        }

        if (state.GridDecorator is not IWorkflowGridDecorator grid || grid.RulerBand <= 0)
        {
            return;
        }

        if (host.IsDisposed || !host.IsHandleCreated)
        {
            return;
        }

        var owner = host.FindForm();
        if (owner is null || owner.IsDisposed)
        {
            return;
        }

        var target = state.ScrollViewer ?? state.Canvas ?? host;
        var overlay = new WorkflowRulerOverlayForm(target, grid.RulerBand);
        overlay.Show(owner);
        state.RulerOverlay = overlay;
        state.RulerHost = target;
        state.RulerOwner = owner;

        target.Resize += state.OnRulerHostChanged;
        target.LocationChanged += state.OnRulerHostChanged;
        owner.Move += state.OnRulerOwnerMoved;
        owner.Resize += state.OnRulerHostChanged;
        SyncRulerOverlay(state, VisualContentOffset(state));
    }

    private static void SyncRulerOverlay(SurfaceState state, Offset contentVisual)
    {
        if (state.RulerOverlay is not { IsDisposed: false } overlay)
        {
            return;
        }

        overlay.ScrollOffsetX = -state.PanOffset.X;
        overlay.ScrollOffsetY = -state.PanOffset.Y;
        overlay.ContentOffsetX = contentVisual.Horizontal;
        overlay.ContentOffsetY = contentVisual.Vertical;
        overlay.SyncSurface();
    }

    private static void DisposeRulerOverlay(SurfaceState state)
    {
        if (state.RulerHost is { } target)
        {
            target.Resize -= state.OnRulerHostChanged;
            target.LocationChanged -= state.OnRulerHostChanged;
        }

        if (state.RulerOwner is { } owner)
        {
            owner.Move -= state.OnRulerOwnerMoved;
            owner.Resize -= state.OnRulerHostChanged;
        }

        state.RulerHost = null;
        state.RulerOwner = null;

        if (state.RulerOverlay is { } overlay)
        {
            state.RulerOverlay = null;
            overlay.Dispose();
        }
    }

    // ── pointer and key routing ──────────────────────────────────────────────

    /// <summary>
    /// Routes one press that landed on a component view rather than on the surface's canvas.
    /// </summary>
    /// <param name="source">The control the press landed on.</param>
    /// <param name="target">The workflow component <paramref name="source"/> renders.</param>
    /// <param name="button">Which button went down.</param>
    /// <param name="clickCount">How many clicks this press completes.</param>
    /// <returns>The handle the route produced, or <see langword="null"/> when no surface or tree was found.</returns>
    internal static WorkflowEventHandle? RouteComponentPress(
        Control source, IWorkflowComponent target, PlatformInput.MouseButtons button, int clickCount)
    {
        var host = FindSurfaceHost(source);
        if (host is null)
        {
            return null;
        }

        var state = GetState(host);
        if (state.Tree is not { } tree)
        {
            return null;
        }

        var canvas = state.Canvas ?? host;
        var point = canvas.PointToClient(Cursor.Position);
        var handle = new WorkflowEventHandle();

        WorkflowInput.For(tree).Route(new Wf.PointerPressedEventArgs(
            new Anchor(point.X, point.Y, 0), Modifiers(), source, target, ToButton(button), clickCount, handle));
        return handle;
    }

    // 离开表面时必须把「原来那个 target」交给 Exited：Core 对显式 Exited 不再合成前一个目标的
    // Exited，target 传 null 只会通知树，节点/插槽的 Input.PointerExited 会漏掉。
    private static void RoutePointerExit(ITreeComponent tree, object? source)
    {
        var input = WorkflowInput.For(tree);
        input.Route(new Wf.PointerExitedEventArgs(
            new Anchor(), Modifiers(), source, input.PointerTarget, new WorkflowEventHandle()));
    }

    /// <summary>Routes one release that landed on a component view rather than on the surface's canvas.</summary>
    /// <param name="source">The control the release landed on.</param>
    /// <param name="target">The workflow component <paramref name="source"/> renders.</param>
    /// <param name="button">Which button came up.</param>
    /// <param name="clickCount">How many clicks the gesture that just ended had.</param>
    /// <returns>The handle the route produced, or <see langword="null"/> when no surface or tree was found.</returns>
    internal static WorkflowEventHandle? RouteComponentRelease(
        Control source, IWorkflowComponent target, PlatformInput.MouseButtons button, int clickCount)
    {
        var host = FindSurfaceHost(source);
        if (host is null)
        {
            return null;
        }

        var state = GetState(host);
        if (state.Tree is not { } tree)
        {
            return null;
        }

        var canvas = state.Canvas ?? host;
        var point = canvas.PointToClient(Cursor.Position);
        var handle = new WorkflowEventHandle();

        WorkflowInput.For(tree).Route(new Wf.PointerReleasedEventArgs(
            new Anchor(point.X, point.Y, 0), Modifiers(), source, target, ToButton(button), clickCount, handle));
        return handle;
    }

    internal static IWorkflowComponent? ResolveTarget(
        Control? hit, ITreeComponent tree, double x, double y, double radius)
    {
        for (var current = hit; current is not null; current = current.Parent)
        {
            // Tag 优先：节点/插槽附件把它写成所属模型；DataContext 可能继承的是树或别的
            // 上下文，非模型的值不能把同一控件上的 Tag 挡住。
            var model = current.Tag as IWorkflowComponent
                ?? ResolveValue(current, "ViewModel") as IWorkflowComponent
                ?? ResolveValue(current, "DataContext") as IWorkflowComponent
                ?? ResolveValue(current, "BindingContext") as IWorkflowComponent;

            switch (model)
            {
                case INodeComponent node:
                    return node;
                case ISlotComponent slot:
                    return slot;
            }
        }

        return tree.HitTestVisibleLinks(x, y, radius);
    }

    private static WorkflowEventHandle RoutePointer(
        SurfaceState state, ITreeComponent tree, Point point,
        Func<Anchor, IWorkflowComponent?, WorkflowEventHandle, Wf.PointerEventArgs> args)
    {
        var canvas = state.Canvas ?? state.Host;
        var hit = canvas.GetChildAtPoint(point);
        var anchor = new Anchor(point.X, point.Y, 0);
        var input = WorkflowInput.For(tree);
        var target = ResolveTarget(hit, tree, anchor.Horizontal, anchor.Vertical, input.HitRadius);
        var handle = new WorkflowEventHandle();

        input.Route(args(anchor, target, handle));

        if (input.HoveredLink is not null)
        {
            FocusCanvas(state);
        }

        return handle;
    }

    private static void RefreshLinkHover(Control host, SurfaceState state)
    {
        if (!state.PointerInside || state.Tree is not { } tree)
        {
            return;
        }

        RoutePointer(state, tree, state.LastPointerClient,
            (anchor, target, handle) => new Wf.PointerMovedEventArgs(anchor, Modifiers(), state.Canvas ?? host, target, handle));
    }

    private static void FocusCanvas(SurfaceState state)
    {
        if (state.Canvas is { CanFocus: true } canvas)
        {
            canvas.Focus();
        }
        else if (state.Host.CanFocus)
        {
            state.Host.Focus();
        }
    }

    private static WorkflowEventHandle RouteWheel(
        Control host, SurfaceState state, ITreeComponent tree, Control hit, double deltaX, double deltaY)
    {
        var canvas = state.Canvas ?? host;
        var point = canvas.PointToClient(Cursor.Position);
        var anchor = new Anchor(point.X, point.Y, 0);
        var input = WorkflowInput.For(tree);
        var target = ResolveTarget(hit, tree, anchor.Horizontal, anchor.Vertical, input.HitRadius);
        var handle = new WorkflowEventHandle();

        input.Route(new Wf.PointerWheelEventArgs(anchor, Modifiers(), hit ?? canvas, target, deltaX, deltaY, handle));
        return handle;
    }

    private static void ApplyZoomFactor(Control host, SurfaceState state, ITreeComponent tree, double factor)
    {
        // 缩放会同时改 Scale、节点卡片尺寸/字号、连线几何和 grid；这些更新必须在同一帧落地。
        // 只节流滚轮还不够：中间一帧会以「新尺寸 + 旧位置」或「旧后缓冲被拉伸」出现。整个事务停
        // 掉画布/节点/连线的重绘，最后统一 RedrawWindow 一次。
        var redrawTargets = new List<Control>();
        var redrawSeen = new HashSet<Control>();
        void Freeze(Control? control)
        {
            if (control is null || !redrawSeen.Add(control))
            {
                return;
            }

            redrawTargets.Add(control);
            NativeWindowStyleHelper.BeginRedraw(control);
        }

        Freeze(host);
        Freeze(state.Canvas);
        Freeze(state.GridDecorator);
        if (state.Canvas is { } frozenCanvas)
        {
            foreach (Control child in frozenCanvas.Controls)
            {
                Freeze(child);
            }
        }

        try
        {
            // Layout 是可写的模型属性；宿主在加载文档前把它清空时，这里不能直接解引用。
            if (tree.Layout is not { } layout)
            {
                return;
            }

            var next = Math.Max(0.1, Math.Min(10, layout.Scale.Horizontal * factor));
            var size = ResolveClientSize(state);

            if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
            {
                var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                    -state.PanOffset.X, -state.PanOffset.Y, size.Width, size.Height, layout);
                layout.CollapsePivot = new Anchor(wx, wy, 0);
                layout.Scale = new Scale(next, next);

                // 深度放大把负向内容折叠越过固定画布平移；扩大覆盖要在读取枢轴目标之前完成。
                WorkflowSurfaceMath.EnsureNegativeCover(tree);

                // ApplyPan 原本会把正向平移折进 NegativeOffset，而枢轴目标读的正是折叠后的 ActualOffset —— 这里
                // 先单独做这一步，再把最终滚动量一次性交给 ApplyPan。旧写法连着调 ApplyPan / SetScrollOffset /
                // Refresh，同一份结果算了三趟，每个滚轮格都同步重画三次整张表面。
                FoldPositivePan(state, layout);

                var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, size.Width, size.Height);
                state.PanOffset = new Point((int)Math.Round(-tx), (int)Math.Round(-ty));
                Refresh(host);
                return;
            }

            layout.Scale = new Scale(next, next);
            if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
            {
                Refresh(host);
            }
        }
        finally
        {
            // 子窗口先恢复，父窗口最后恢复；父窗口的 RDW_ALLCHILDREN 才不会在子窗口仍冻结时落下。
            for (var index = redrawTargets.Count - 1; index >= 0; index--)
            {
                NativeWindowStyleHelper.EndRedraw(redrawTargets[index]);
            }
        }
    }

    private static void FocusSurfaceForPress()
    {
        var under = SurfaceState.GetControlAtScreenPoint(Cursor.Position);
        if (under is null)
        {
            return;
        }

        var host = FindSurfaceHost(under);
        if (host is null)
        {
            return;
        }

        var state = GetState(host);
        if (state.Canvas is not { } canvas || !IsWithinCanvas(under, canvas) || IsInsideEditable(under))
        {
            return;
        }

        FocusCanvas(state);
    }

    private static bool IsWithinCanvas(Control? control, Control canvas)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, canvas))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideEditable(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Control? FindSurfaceHost(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (States.TryGetValue(current, out var state) && state.Attached)
            {
                return current;
            }
        }

        return null;
    }

    // 节点/插槽行为没有显式坐标宿时的兜底：沿父链找到本行为的表面，再用它的画布。
    internal static Control? FindSurfaceCanvas(Control control)
    {
        var host = FindSurfaceHost(control);
        return host is not null && States.TryGetValue(host, out var state) ? state.Canvas : null;
    }

    // 表面可能从 DataContext/Tag 解析出树，而没有写 WorkflowTree DP；连线层要读同一份解析结果，
    // 否则 DataContext 型宿主只有节点、没有连线。
    internal static ITreeComponent? GetSurfaceTree(Control control)
    {
        var host = FindSurfaceHost(control);
        return host is not null
               && States.TryGetValue(host, out var state)
               && state.Attached
            ? state.Tree
            : null;
    }

    private static ITreeComponent? ResolveTree(Control host)
    {
        if (GetWorkflowTree(host) is { } explicitTree)
        {
            return explicitTree;
        }

        var current = host;
        while (current is not null)
        {
            ITreeComponent? tree = ResolveValue(current, "ViewModel") as ITreeComponent;
            tree ??= ResolveValue(current, "DataContext") as ITreeComponent;
            tree ??= ResolveValue(current, "BindingContext") as ITreeComponent;
            tree ??= current.Tag as ITreeComponent;
            if (tree is not null)
            {
                return tree;
            }

            current = current.Parent;
        }

        return null;
    }

    private static object? ResolveValue(Control control, string propertyName)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static Wf.InputModifiers Modifiers()
    {
        var keys = Control.ModifierKeys;
        var modifiers = Wf.InputModifiers.None;
        if ((keys & Keys.Alt) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & Keys.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & Keys.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        return modifiers;
    }

    private static Wf.MouseButton ToButton(PlatformInput.MouseButtons button) => button switch
    {
        PlatformInput.MouseButtons.Left => Wf.MouseButton.Left,
        PlatformInput.MouseButtons.Right => Wf.MouseButton.Right,
        PlatformInput.MouseButtons.Middle => Wf.MouseButton.Middle,
        PlatformInput.MouseButtons.XButton1 => Wf.MouseButton.XButton1,
        PlatformInput.MouseButtons.XButton2 => Wf.MouseButton.XButton2,
        _ => Wf.MouseButton.None,
    };

    private static Wf.InputKey ToKey(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z) return Wf.InputKey.A + ((int)key - (int)Keys.A);
        if (key >= Keys.D0 && key <= Keys.D9) return Wf.InputKey.D0 + ((int)key - (int)Keys.D0);
        if (key >= Keys.F1 && key <= Keys.F12) return Wf.InputKey.F1 + ((int)key - (int)Keys.F1);

        return key switch
        {
            Keys.None => Wf.InputKey.None,
            Keys.Cancel => Wf.InputKey.Cancel,
            Keys.Back => Wf.InputKey.Back,
            Keys.Tab => Wf.InputKey.Tab,
            Keys.Enter => Wf.InputKey.Enter,
            Keys.Escape => Wf.InputKey.Escape,
            Keys.Space => Wf.InputKey.Space,
            Keys.PageUp => Wf.InputKey.PageUp,
            Keys.PageDown => Wf.InputKey.PageDown,
            Keys.End => Wf.InputKey.End,
            Keys.Home => Wf.InputKey.Home,
            Keys.Left => Wf.InputKey.Left,
            Keys.Up => Wf.InputKey.Up,
            Keys.Right => Wf.InputKey.Right,
            Keys.Down => Wf.InputKey.Down,
            Keys.Insert => Wf.InputKey.Insert,
            Keys.Delete => Wf.InputKey.Delete,
            Keys.LShiftKey => Wf.InputKey.LeftShift,
            Keys.RShiftKey => Wf.InputKey.RightShift,
            Keys.LControlKey => Wf.InputKey.LeftCtrl,
            Keys.RControlKey => Wf.InputKey.RightCtrl,
            Keys.LMenu => Wf.InputKey.LeftAlt,
            Keys.RMenu => Wf.InputKey.RightAlt,
            Keys.LWin => Wf.InputKey.LWin,
            Keys.RWin => Wf.InputKey.RWin,
            Keys.ShiftKey => Wf.InputKey.LeftShift,
            Keys.ControlKey => Wf.InputKey.LeftCtrl,
            Keys.Menu => Wf.InputKey.LeftAlt,
            _ => Wf.InputKey.Unknown,
        };
    }

    private static SurfaceState GetState(Control host)
        => States.GetValue(host, static control => new SurfaceState(control));

    // ── link menu ────────────────────────────────────────────────────────────

    private static void AttachLinkInput(Control host, SurfaceState state)
    {
        DetachLinkInput(state);
        if (state.Tree is not { } tree)
        {
            return;
        }

        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            state.MenuPressed = (_, e) => ShowLinkMenu(host, state, e);
            events.Input.PointerPressed += state.MenuPressed;
        }

        state.MenuLinkRemoved = (_, link) =>
        {
            if (!ReferenceEquals(state.MenuLink, link))
            {
                return;
            }

            state.LinkMenu?.Close();
        };
        tree.GetHelper().LinkRemoved += state.MenuLinkRemoved;
    }

    private static void DetachLinkInput(SurfaceState state)
    {
        if (state.Tree is { } tree)
        {
            var helper = tree.GetHelper();
            if (state.MenuPressed is not null && helper is Wf.IInputEvents events)
            {
                events.Input.PointerPressed -= state.MenuPressed;
            }

            if (state.MenuLinkRemoved is not null)
            {
                helper.LinkRemoved -= state.MenuLinkRemoved;
            }
        }

        state.MenuPressed = null;
        state.MenuLinkRemoved = null;
        state.MenuLink = null;

        if (state.LinkMenu is { } menu)
        {
            state.LinkMenu = null;
            menu.Close();
            menu.Dispose();
        }

        // 解绑前清掉指针目标：路由跟着树活着，比这次绑定久。
        if (state.Tree is { } detachedTree)
        {
            RoutePointerExit(detachedTree, state.Canvas ?? state.Host);
        }
    }

    private static void ShowLinkMenu(Control host, SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        if (e.Button != Wf.MouseButton.Right)
        {
            return;
        }

        if (e.Target is not ILinkComponent link)
        {
            return;
        }

        if (e.Handle.PreventDefault || state.LinkMenu?.Visible == true)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        GetLinkMenuBuilder(host)?.Invoke(menu, link);

        var canvas = state.Canvas ?? host;
        var screen = canvas.PointToScreen(new Point((int)e.Position.Horizontal, (int)e.Position.Vertical));

        menu.Closed += (_, _) =>
        {
            if (state.Tree is { } tree)
            {
                WorkflowInput.For(tree).IsSuspended = false;
            }

            if (ReferenceEquals(state.LinkMenu, menu))
            {
                state.LinkMenu = null;
                state.MenuLink = null;
            }

            if (!host.IsDisposed)
            {
                try
                {
                    host.BeginInvoke(new Action(menu.Dispose));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
                {
                    menu.Dispose();
                }
            }
        };

        state.LinkMenu = menu;
        state.MenuLink = link;

        if (state.Tree is { } suspendedTree)
        {
            WorkflowInput.For(suspendedTree).IsSuspended = true;
        }

        menu.Show(screen);
    }

    // ── scroller ─────────────────────────────────────────────────────────────

    private sealed class SurfaceScroller : IWorkflowSurfaceScroller, IWorkflowSurfaceNavigator
    {
        private const double Notch = 120d;
        private const double LineHeight = 16d;

        private readonly SurfaceState state;

        public SurfaceScroller(SurfaceState state) => this.state = state;

        public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
        {
            var lines = SystemInformation.MouseWheelScrollLines;
            var size = ResolveClientSize(state);
            var stepX = lines > 0 ? lines * LineHeight : size.Width;
            var stepY = lines > 0 ? lines * LineHeight : size.Height;

            var dx = wheelDeltaX != 0d ? (wheelDeltaX / Notch) * stepX : 0d;
            var dy = wheelDeltaY != 0d ? -(wheelDeltaY / Notch) * stepY : 0d;

            SetScrollOffset(
                state.Host,
                state,
                -state.PanOffset.X + dx,
                -state.PanOffset.Y + dy);
        }

        public void CenterOn(double worldX, double worldY)
        {
            if (state.Tree?.Layout is not { } layout)
            {
                return;
            }

            var size = ResolveClientSize(state);

            // 节点的 Anchor/Size 是「折叠后」的画布局部坐标；视口还必须加上 CanvasLayout.ActualOffset
            // （负向扩展后的画布原点）。只做 world - viewport/2 会在平移/缩放后把目标送偏同样的偏移。
            var (scrollX, scrollY) = WorkflowSurfaceMath.CenterScroll(
                worldX,
                worldY,
                size.Width,
                size.Height,
                layout);

            // 标尺浮层占据左上角 band 像素；卡片实际位置 = Anchor + Pan + ActualOffset + band。
            // 少了 band 这一项，定位结果会固定往左上偏一条标尺。
            var visualContent = VisualContentOffset(state);
            scrollX += visualContent.Horizontal - layout.ActualOffset.Horizontal;
            scrollY += visualContent.Vertical - layout.ActualOffset.Vertical;

            SetScrollOffset(state.Host, state, scrollX, scrollY);
        }
    }

    // ── template selector ────────────────────────────────────────────────────

    private sealed class SurfaceTemplateSelector : TemplateSelector
    {
        private readonly System.Reflection.Assembly assembly;
        private readonly string? namespaceName;
        private readonly DataTemplate? nodeTemplate;
        private readonly DataTemplate? linkTemplate;

        public SurfaceTemplateSelector(
            System.Reflection.Assembly assembly, string? namespaceName, string? nodeTypeName, string? linkTypeName)
        {
            this.assembly = assembly;
            this.namespaceName = namespaceName;
            nodeTemplate = string.IsNullOrWhiteSpace(nodeTypeName)
                ? null
                : new DataTemplate(() => Instantiate(nodeTypeName, "node"));
            linkTemplate = string.IsNullOrWhiteSpace(linkTypeName)
                ? null
                : new DataTemplate(() => Instantiate(linkTypeName, "link"));
        }

        public override DataTemplate? SelectTemplate(object? item) => item switch
        {
            INodeComponent => nodeTemplate,
            ILinkComponent => linkTemplate,
            _ => null,
        };

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming", "IL2072",
            Justification = "The view type is named by the host and ships with the application.")]
        private Control Instantiate(string? name, string role)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"No {role} view type is configured on the surface.");
            }

            var type = ResolveType(name!, role);
            return Activator.CreateInstance(type) as Control
                ?? throw new InvalidOperationException(
                    $"The {role} view type '{type.FullName}' is not a WinForms control or has no parameterless constructor.");
        }

        private Type ResolveType(string name, string role)
        {
            name = name.Trim();
            var resolved = assembly.GetType(name, throwOnError: false);

            if (resolved is null && name.IndexOf('.') < 0 && !string.IsNullOrEmpty(namespaceName))
            {
                resolved = assembly.GetType(namespaceName + "." + name, throwOnError: false);
            }

            return resolved ?? throw new InvalidOperationException(
                $"The {role} view type '{name}' was not found in '{assembly.GetName().Name}'.");
        }
    }

    // ── state ────────────────────────────────────────────────────────────────

    private sealed class SurfaceState : IMessageFilter
    {
        private static long lastHoverStamp;

        private const int WmMouseMove = 0x0200;
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHWheel = 0x020E;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmRightButtonDown = 0x0204;
        private const int WmMiddleButtonDown = 0x0207;
        private const int SbVert = 1;
        private const uint SifAll = 0x17;

        private bool filterAdded;

        // 滚轮是离散消息：高分辨率滚轮/触控板一帧能发很多笔。把缩放因子累到一个 16ms 的 UI 帧里，
        // 每帧最多做一次完整缩放，而不是每一笔都重排/重绘整张表面。
        private const int ZoomFrameIntervalMilliseconds = 16;
        private System.Windows.Forms.Timer? zoomTimer;

        // 首次物化会把节点/连线分多批塞进画布；在池排空之前冻结整棵表面，避免每批都露出一帧
        // 「已经到 0,0 / 还没摆好」的残影，最后统一提交一次。
        private const int InitialRevealSettleTicks = 2;
        private const int InitialRevealTimeoutMilliseconds = 2000;
        private System.Windows.Forms.Timer? initialRevealTimer;
        private readonly List<Control> initialRevealTargets = [];
        private readonly HashSet<Control> initialRevealSeen = new();
        private bool initialRevealHeld;
        private int initialRevealSettleTicks;
        private long initialRevealStarted;

        public SurfaceState(Control host) => Host = host;

        public void BeginInitialReveal(Control host, Control canvas, Control? grid)
        {
            if (initialRevealHeld)
            {
                return;
            }

            initialRevealHeld = true;
            initialRevealSettleTicks = 0;
            initialRevealStarted = TickCount64;

            HoldInitialReveal(host);
            HoldInitialReveal(canvas);
            HoldInitialReveal(grid);
            foreach (Control child in canvas.Controls)
            {
                HoldInitialReveal(child);
            }

            if (initialRevealTimer is null)
            {
                initialRevealTimer = new System.Windows.Forms.Timer
                {
                    Interval = ZoomFrameIntervalMilliseconds,
                };
                initialRevealTimer.Tick += OnInitialRevealTick;
            }

            initialRevealTimer.Start();
        }

        public void HoldInitialRevealView(Control view)
        {
            if (initialRevealHeld)
            {
                HoldInitialReveal(view);
            }
        }

        private void HoldInitialReveal(Control? control)
        {
            if (control is null || !initialRevealSeen.Add(control))
            {
                return;
            }

            initialRevealTargets.Add(control);
            NativeWindowStyleHelper.BeginRedraw(control);
        }

        private void OnInitialRevealTick(object? sender, EventArgs e)
        {
            if (!initialRevealHeld)
            {
                return;
            }

            var pending = Canvas is { } canvas ? ViewPool.GetPendingViewCount(canvas) : 0;
            if (pending == 0)
            {
                initialRevealSettleTicks++;
            }
            else
            {
                initialRevealSettleTicks = 0;
            }

            var timedOut = TickCount64 - initialRevealStarted >= InitialRevealTimeoutMilliseconds;
            if (initialRevealSettleTicks >= InitialRevealSettleTicks || timedOut)
            {
                EndInitialReveal();
            }
        }

        public void EndInitialReveal()
        {
            if (!initialRevealHeld)
            {
                return;
            }

            initialRevealHeld = false;
            if (initialRevealTimer is { } timer)
            {
                timer.Stop();
                timer.Tick -= OnInitialRevealTick;
                timer.Dispose();
                initialRevealTimer = null;
            }

            // 子窗口先恢复、父窗口最后恢复，随后一次重绘提交最终布局。
            for (var index = initialRevealTargets.Count - 1; index >= 0; index--)
            {
                NativeWindowStyleHelper.EndRedraw(initialRevealTargets[index]);
            }

            initialRevealTargets.Clear();
            initialRevealSeen.Clear();
        }

        public void QueueZoom(Control host, ITreeComponent tree, double factor)
        {
            pendingZoomFactor *= factor;

            if (zoomTimer is null)
            {
                zoomTimer = new System.Windows.Forms.Timer { Interval = ZoomFrameIntervalMilliseconds };
                zoomTimer.Tick += OnZoomTimerTick;
            }

            if (!zoomTimer.Enabled)
            {
                zoomTimer.Start();
            }
        }

        private void OnZoomTimerTick(object? sender, EventArgs e)
        {
            if (zoomTimer is not { } timer)
            {
                return;
            }

            timer.Stop();
            var factor = pendingZoomFactor;
            pendingZoomFactor = 1d;

            if (factor == 1d || Host.IsDisposed || Tree is not { } tree)
            {
                return;
            }

            ApplyZoomFactor(Host, this, tree, factor);
        }

        public void ReleaseZoomTimer()
        {
            if (zoomTimer is not { } timer)
            {
                return;
            }

            timer.Stop();
            timer.Tick -= OnZoomTimerTick;
            timer.Dispose();
            zoomTimer = null;
            pendingZoomFactor = 1d;
        }

        public Control Host { get; }

        public bool Attached { get; set; }

        public Control? ScrollViewer { get; set; }

        public XamlScrollViewer? XamlScrollViewer { get; set; }

        public bool XamlScrollViewerHooked { get; set; }

        public EventHandler? XamlScrollViewerScroll { get; set; }

        // 同步滚动条值时它会不会回抛 Scroll：这个标记把回程路径截断。
        public bool SyncingXamlScrollViewer { get; set; }

        public Control? Canvas { get; set; }

        public bool CanvasHooked { get; set; }

        public bool PressSourceHooked { get; set; }

        public bool MinimapHooked { get; set; }

        public Control? GridDecorator { get; set; }

        public Control? MinimapOverlay { get; set; }

        public Control? PointerPressSource { get; set; }

        public ITreeComponent? Tree { get; set; }

        public INotifyPropertyChanged? TreeNotifier { get; set; }

        public IWorkflowSurfaceScroller? Scroller { get; set; }

        public TemplateSelector? PoolSelector { get; set; }

        public Control? PoolCanvas { get; set; }

        public object? PoolItemsSource { get; set; }

        public SurfaceTemplateSelector? GeneratedSelector { get; set; }

        public string? GeneratedNodeType { get; set; }

        public string? GeneratedLinkType { get; set; }

        public bool LayoutPending { get; set; }

        private double pendingZoomFactor = 1d;

        public Point PanOffset { get; set; }

        public bool IsPanning { get; set; }

        public Point PanPressScreen { get; set; }

        public Point PanOffsetAtPress { get; set; }

        public Point LastPointerClient { get; set; }

        public bool PointerInside { get; set; }

        public ContextMenuStrip? LinkMenu { get; set; }

        public ILinkComponent? MenuLink { get; set; }

        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }

        public EventHandler<ILinkComponent>? MenuLinkRemoved { get; set; }

        public ITreeComponent? LastRestoreTree { get; set; }

        public bool HasPendingRestore { get; set; }

        public bool RestoreQueued { get; set; }

        public double PendingRestoreX { get; set; }

        public double PendingRestoreY { get; set; }

        public WorkflowRulerOverlayForm? RulerOverlay { get; set; }

        public Control? RulerHost { get; set; }

        public Form? RulerOwner { get; set; }

        public EventHandler? DataContextHandler { get; set; }

        public ConditionalWeakTable<WorkflowNodeAttachment, object> AnchorHooks { get; } = new();

        public HashSet<Control> ViewHooks { get; } = new();

        private System.Windows.Forms.Timer? pointerWatch;

        // 指针从子控件直接移出应用时，WinForms 不一定给表面根发 MouseLeave；用一个轻量计时器
        // 兜住「离开表面」这件事，保证 Core 的 PointerTarget 不会停在最后那个节点/插槽上。
        internal void EnsurePointerWatch()
        {
            if (pointerWatch is null)
            {
                pointerWatch = new System.Windows.Forms.Timer { Interval = 50 };
                pointerWatch.Tick += OnPointerWatchTick;
            }

            if (!pointerWatch.Enabled)
            {
                pointerWatch.Start();
            }
        }

        internal void ReleasePointerWatch()
        {
            if (pointerWatch is null) return;
            pointerWatch.Stop();
            pointerWatch.Tick -= OnPointerWatchTick;
            pointerWatch.Dispose();
            pointerWatch = null;
        }

        private void OnPointerWatchTick(object? sender, EventArgs e)
        {
            if (!Attached)
            {
                ReleasePointerWatch();
                return;
            }

            var under = GetControlAtScreenPoint(Cursor.Position);
            var host = ResolveSurfaceHost(under);
            if (host is not null && ReferenceEquals(host, Host))
            {
                return;
            }

            PointerInside = false;
            ReleasePointerWatch();
            if (Tree is { } tree)
            {
                RoutePointerExit(tree, null);
            }
        }

        internal void EnsureMessageFilter()
        {
            if (filterAdded)
            {
                return;
            }

            Application.AddMessageFilter(this);
            filterAdded = true;
        }

        internal void RemoveMessageFilter()
        {
            if (!filterAdded)
            {
                return;
            }

            Application.RemoveMessageFilter(this);
            filterAdded = false;
        }

        public void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ITreeComponent.Layout)
                or nameof(ITreeComponent.Nodes)
                or nameof(ITreeComponent.Links))
            {
                ScheduleLayout(Host, this);
            }
        }

        public void OnMinimapScrollRequested(double scrollX, double scrollY)
        {
            SetScrollOffset(Host, this, scrollX, scrollY);
        }

        public void OnNodeAnchorChanged()
        {
            if (MinimapOverlay is { IsDisposed: false } minimap)
            {
                minimap.Invalidate();
            }
        }

        public void OnRulerHostChanged(object? sender, EventArgs e) => SyncRulerOverlay(this, VisualContentOffset(this));

        public void OnRulerOwnerMoved(object? sender, EventArgs e) => SyncRulerOverlay(this, VisualContentOffset(this));

        public void OnCanvasControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control is not { } view)
            {
                return;
            }

            HoldInitialRevealView(view);
            HookView(this, view);

            // 池化视图是在 SetItemsSource/视口变化之后才创建的：Node 绑定只做了默认投影 (0,0)，
            // 必须在这里补上当前的 PanOffset + ActualOffset + ruler band，否则定位后新物化的节点
            // 会停在世界原点附近，看起来像其它节点没有重新测绘。
            ApplyViewProjection(this, view);
        }

        public void OnCanvasViewVisibilityChanged(object? sender, EventArgs e)
        {
            if (sender is not Control view || !view.Visible || view.IsDisposed)
            {
                return;
            }

            // 池回收只隐藏视图；再次进入可见区时不会触发 ControlAdded。这里补投影、槽布局和拖拽钩子，
            // 否则移出可见区再移回来的节点会停在旧投影且无法拖动，Link 也停留在未就绪状态。
            ApplyViewProjection(this, view);
        }

        public void OnCanvasMouseDown(object? sender, MouseEventArgs e)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var canvas = Canvas ?? Host;
            var point = canvas.PointToClient(Cursor.Position);

            if (e.Button == MouseButtons.Right)
            {
                RoutePointer(this, tree, point, (p, t, h) => new Wf.PointerPressedEventArgs(
                    p, Modifiers(), canvas, t, Wf.MouseButton.Right, 1, h));
                return;
            }

            if (e.Button == MouseButtons.Middle)
            {
                RoutePointer(this, tree, point, (p, t, h) => new Wf.PointerPressedEventArgs(
                    p, Modifiers(), canvas, t, Wf.MouseButton.Middle, 1, h));
                return;
            }

            if (e.Button != MouseButtons.Left || IsPanning)
            {
                return;
            }

            var press = RoutePointer(this, tree, point, (p, t, h) => new Wf.PointerPressedEventArgs(
                p, Modifiers(), canvas, t, Wf.MouseButton.Left, 1, h));

            if (press.PreventDefault)
            {
                return;
            }

            IsPanning = true;
            PanPressScreen = Cursor.Position;
            PanOffsetAtPress = PanOffset;
            canvas.Capture = true;
        }

        public void OnCanvasMouseMove(object? sender, MouseEventArgs e)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var canvas = Canvas ?? Host;
            LastPointerClient = canvas.PointToClient(Cursor.Position);

            if (IsPanning)
            {
                var current = Cursor.Position;
                PanOffset = new Point(
                    PanOffsetAtPress.X + (current.X - PanPressScreen.X),
                    PanOffsetAtPress.Y + (current.Y - PanPressScreen.Y));
                ApplyPan(Host, this);
            }

            // 指针移动由 SurfaceState 的消息过滤器统一路由：节点/插槽的子窗口在这里收不到 MouseMove。
        }

        public void OnCanvasMouseUp(object? sender, MouseEventArgs e)
        {
            if (Tree is { } tree)
            {
                var canvas = Canvas ?? Host;
                var point = canvas.PointToClient(Cursor.Position);
                RoutePointer(this, tree, point, (p, t, h) => new Wf.PointerReleasedEventArgs(
                    p, Modifiers(), canvas, t, ToButton(e.Button), 1, h));
            }

            if (!IsPanning)
            {
                return;
            }

            IsPanning = false;
            if (Canvas is { } canvasControl)
            {
                canvasControl.Capture = false;
            }

            ApplyPan(Host, this);
        }

        public void OnCanvasMouseCaptureChanged(object? sender, EventArgs e)
        {
            if (Canvas is { Capture: false })
            {
                IsPanning = false;
            }
        }

        public void OnCanvasMouseEnter(object? sender, EventArgs e)
        {
            PointerInside = true;
            LastPointerClient = (Canvas ?? Host).PointToClient(Cursor.Position);
            EnsurePointerWatch();
            // 进入/离开由 Core 根据 PointerMoved 的目标变化合成；这里不再单独 Route 一次。
        }

        public void OnCanvasMouseLeave(object? sender, EventArgs e)
        {
            // 指针从画布进入它的子控件时，WinForms 也会给画布发 MouseLeave；那不是离开表面。
            var under = GetControlAtScreenPoint(Cursor.Position);
            var host = ResolveSurfaceHost(under);
            if (host is not null && ReferenceEquals(host, Host))
            {
                return;
            }

            PointerInside = false;
            ReleasePointerWatch();
            if (Tree is { } tree)
            {
                RoutePointerExit(tree, Canvas ?? Host);
            }
        }

        public void OnCanvasKeyDown(object? sender, PlatformInput.KeyEventArgs e)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var input = WorkflowInput.For(tree);
            input.Route(new Wf.KeyDownEventArgs(
                ToKey(e.KeyCode), (int)e.KeyCode, Modifiers(), false,
                Canvas ?? Host, input.PointerTarget, new WorkflowEventHandle()));

            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void OnCanvasKeyUp(object? sender, PlatformInput.KeyEventArgs e)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var input = WorkflowInput.For(tree);
            input.Route(new Wf.KeyUpEventArgs(
                ToKey(e.KeyCode), (int)e.KeyCode, Modifiers(), false,
                Canvas ?? Host, input.PointerTarget, new WorkflowEventHandle()));
        }

        // 把光标所在的表面/目标翻译成 Core 的 PointerMoved。节点、插槽是子窗口，表面根的
        // MouseMove 收不到它们；消息过滤器则对所有窗口都可见，因此 target 的更新不再取决于是谁命中。
        private void RouteHoverFromCursor(IntPtr messageHandle)
        {
            var under = GetControlAtScreenPoint(Cursor.Position);
            var host = ResolveSurfaceHost(under) ?? ResolveSurfaceHost(messageHandle);
            if (host is null
                || !States.TryGetValue(host, out var state)
                || !state.Attached
                || state.Tree is not { } tree)
            {
                return;
            }

            var canvas = state.Canvas ?? host;
            var point = canvas.PointToClient(Cursor.Position);
            state.LastPointerClient = point;
            state.PointerInside = true;
            state.EnsurePointerWatch();

            var anchor = new Anchor(point.X, point.Y, 0);
            var input = WorkflowInput.For(tree);
            IWorkflowComponent? target = null;

            // 指针在画布之外（滚动条、小地图、表面边框）时没有组件目标；不要拿画布坐标
            // 去命中一条实际被覆盖住的连线。
            if (IsWithinCanvas(under, canvas))
            {
                // 用光标下真正的控件（可能是卡片的 Label、插槽的子控件）向上找模型；
                // GetChildAtPoint 只给直接子控件，深层子控件会漏成空目标。
                target = ResolveTarget(under, tree, anchor.Horizontal, anchor.Vertical, input.HitRadius);
            }

            input.Route(new Wf.PointerMovedEventArgs(
                anchor, Modifiers(), under ?? canvas, target, new WorkflowEventHandle()));

            if (input.HoveredLink is not null)
            {
                FocusCanvas(state);
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WmMouseMove)
            {
                // 节点/插槽是子窗口：表面根收不到它们上面的 MouseMove，Core 的 PointerTarget 就不会更新。
                // 与滚轮同一条路，在消息入口统一翻译成标准输入，再交给 WorkflowInput 路由。
                var stamp = ((long)m.Msg << 48) ^ m.LParam.ToInt64() ^ ((long)(uint)m.WParam.ToInt64() << 16) ^ m.HWnd.ToInt64();
                if (stamp != lastHoverStamp)
                {
                    lastHoverStamp = stamp;
                    RouteHoverFromCursor(m.HWnd);
                }

                return false;
            }

            if (m.Msg is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown)
            {
                FocusSurfaceForPress();
                return false;
            }

            if (m.Msg is not (WmMouseWheel or WmMouseHWheel))
            {
                return false;
            }

            var under = GetControlAtScreenPoint(Cursor.Position);
            var host = ResolveSurfaceHost(under) ?? ResolveSurfaceHost(m.HWnd);
            if (host is null || !States.TryGetValue(host, out var state))
            {
                return false;
            }

            var tree = state.Tree ?? ResolveTree(host);
            if (tree is null)
            {
                return false;
            }

            var delta = unchecked((short)((uint)m.WParam.ToInt64() >> 16));
            var deltaX = m.Msg == WmMouseHWheel ? delta : 0;
            var deltaY = m.Msg == WmMouseWheel ? delta : 0;

            if (InnerScrollableHasRoom(under, host, deltaX, deltaY))
            {
                return false;
            }

            var zoomGesture = m.Msg == WmMouseWheel
                && Control.ModifierKeys == Keys.Control
                && GetZoomEnabled(host);

            if (!zoomGesture)
            {
                var handle = RouteWheel(host, state, tree, under ?? host, deltaX, deltaY);

                // 订阅方在这笔滚轮上置了 PreventDefault 就由它接管；否则走适配器默认的平移步长。
                if (!handle.PreventDefault)
                {
                    state.Scroller?.ScrollBy(deltaX, deltaY);
                }

                m.Result = IntPtr.Zero;
                return true;
            }

            var zoomHandle = RouteWheel(host, state, tree, under ?? host, 0d, deltaY);
            if (zoomHandle.PreventDefault)
            {
                m.Result = IntPtr.Zero;
                return true;
            }

            // 每笔滚轮只累加目标因子；真正的缩放/重绘由 16ms UI 帧合并执行。
            state.QueueZoom(host, tree, delta > 0 ? 1d / 1.1d : 1.1d);
            m.Result = IntPtr.Zero;
            return true;
        }

        private static Control? ResolveSurfaceHost(IntPtr hwnd) => ResolveSurfaceHost(Control.FromHandle(hwnd));

        private static Control? ResolveSurfaceHost(Control? hit)
        {
            for (var current = hit; current is not null; current = current.Parent)
            {
                if (States.TryGetValue(current, out var state) && state.Attached)
                {
                    return current;
                }
            }

            return null;
        }

        internal static Control? GetControlAtScreenPoint(Point screenPoint)
        {
            var handle = WindowFromPoint(screenPoint);
            return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle) ?? Control.FromHandle(handle);
        }

        private static bool InnerScrollableHasRoom(Control? under, Control host, int deltaX, int deltaY)
        {
            if (under is null || (deltaX == 0 && deltaY == 0))
            {
                return false;
            }

            for (var current = under; current is not null && !ReferenceEquals(current, host); current = current.Parent)
            {
                switch (current)
                {
                    case TextBoxBase text when TextBoxHasRoom(text, deltaY):
                        return true;
                    case ScrollableControl scrollable when ScrollableControlHasRoom(scrollable, deltaX, deltaY):
                        return true;
                }
            }

            return false;
        }

        private static bool ScrollableControlHasRoom(ScrollableControl scrollable, int deltaX, int deltaY)
        {
            var vertical = scrollable.VerticalScroll;
            var horizontal = scrollable.HorizontalScroll;

            var verticalRoom = deltaY == 0
                || (vertical.Visible && (deltaY > 0
                    ? vertical.Value > vertical.Minimum
                    : vertical.Value < vertical.Maximum - vertical.LargeChange + 1));
            var horizontalRoom = deltaX == 0
                || (horizontal.Visible && (deltaX > 0
                    ? horizontal.Value < horizontal.Maximum - horizontal.LargeChange + 1
                    : horizontal.Value > horizontal.Minimum));

            return verticalRoom && horizontalRoom;
        }

        private static bool TextBoxHasRoom(TextBoxBase text, int deltaY)
        {
            if (!text.IsHandleCreated || deltaY == 0)
            {
                return false;
            }

            return WindowBarHasRoom(text.Handle, SbVert, deltaY);
        }

        private static bool WindowBarHasRoom(IntPtr hwnd, int bar, int delta)
        {
            var info = new ScrollInfo
            {
                cbSize = (uint)Marshal.SizeOf<ScrollInfo>(),
                fMask = SifAll,
            };

            if (!GetScrollInfo(hwnd, bar, ref info))
            {
                return false;
            }

            var max = info.nMax - Math.Max(0, info.nPage - 1);
            return delta > 0 ? info.nPos > info.nMin : info.nPos < max;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public int nPage;
            public int nPos;
            public int nTrackPos;
        }

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
    }
}
