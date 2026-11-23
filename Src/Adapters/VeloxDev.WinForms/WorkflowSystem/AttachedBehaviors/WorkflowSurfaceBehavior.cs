using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem.Platform;
using VeloxDev.WorkflowSystem.StandardEx;
using PlatformInput = System.Windows.Forms;
using Wf = VeloxDev.WorkflowSystem;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using VisualContextMenu = VeloxDev.WinForms.Xaml.Visuals.Controls.VisualContextMenu;
using VisualScrollViewer = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer;
using VisualScrollBar = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollBar;
using VisualScrollOffsetEventArgs = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollOffsetRequestedEventArgs;
using DrawingPointF = System.Drawing.PointF;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The visual WinForms workflow surface: one <see cref="VisualHost"/>, a camera on <c>PART_Canvas</c>, a
/// workflow-free <see cref="ViewPool"/> that materializes the tree's visible items, and input routing to Core.
/// </summary>
/// <remarks>
/// The surface owns no node, link or slot visuals. <see cref="ViewPool"/> creates one view per visible item from
/// the tree's selector; the templates bind model geometry and paint themselves. Slot measurement, node dragging and
/// slot connection live in their own attached behaviors.
/// </remarks>
public sealed class WorkflowSurfaceBehavior
{
    private static readonly ConditionalWeakTable<Control, SurfaceState> States = new();
    private static readonly ConditionalWeakTable<VisualElement, Wf.WorkflowEventHandle> PressHandles = new();

    /// <summary>Whether the surface lifecycle is active on the control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Whether Ctrl + wheel zooms the camera.</summary>
    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnConfigurationChanged));

    /// <summary>Name of the visual viewport part.</summary>
    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Name of the visual canvas that hosts the pool.</summary>
    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Name of the grid/ruler decorator part.</summary>
    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Name of the minimap overlay (an HWND sibling outside the visual tree).</summary>
    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Kept for WPF parity; input is routed by the visual host, so no part is used as a press source.</summary>
    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>The selector that creates the pooled views.</summary>
    public static readonly DependencyProperty VisualTemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "VisualTemplateSelector", typeof(VisualTemplateSelector), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Resource key of the tree's selector; defaults to <c>WorkflowTemplateSelector</c>.</summary>
    public static readonly DependencyProperty VisualTemplateSelectorKeyProperty = DependencyProperty.RegisterAttached(
        "VisualTemplateSelectorKey", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata("WorkflowTemplateSelector", OnConfigurationChanged));

    /// <summary>Resting link colour.</summary>
    public static readonly DependencyProperty VisualLinkColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF), OnLinkStyleChanged));

    /// <summary>Resting link width.</summary>
    public static readonly DependencyProperty VisualLinkThicknessProperty = DependencyProperty.RegisterAttached(
        "VisualLinkThickness", typeof(float), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(2f, OnLinkStyleChanged));

    /// <summary>Hovered link colour.</summary>
    public static readonly DependencyProperty VisualLinkHoverColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkHoverColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.White, OnLinkStyleChanged));

    /// <summary>Hovered link width.</summary>
    public static readonly DependencyProperty VisualLinkHoverThicknessProperty = DependencyProperty.RegisterAttached(
        "VisualLinkHoverThickness", typeof(float), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(3.5f, OnLinkStyleChanged));

    /// <summary>Virtual-link colour.</summary>
    public static readonly DependencyProperty VisualLinkVirtualColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkVirtualColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF), OnLinkStyleChanged));

    /// <summary>Whether resting links glow.</summary>
    public static readonly DependencyProperty VisualLinkGlowProperty = DependencyProperty.RegisterAttached(
        "VisualLinkGlow", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnLinkStyleChanged));

    /// <summary>The tree bound to the surface when no data context carries one.</summary>
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.RegisterAttached(
        "WorkflowTree", typeof(Wf.ITreeComponent), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Fills the link context menu.</summary>
    public static readonly DependencyProperty LinkMenuBuilderProperty = DependencyProperty.RegisterAttached(
        "LinkMenuBuilder", typeof(Action<ContextMenuStrip, Wf.ILinkComponent>), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    /// <summary>Resource key of a visual context menu opened on a right-pressed link.</summary>
    public static readonly DependencyProperty LinkMenuKeyProperty = DependencyProperty.RegisterAttached(
        "LinkMenuKey", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    /// <summary>Gets whether the surface is active.</summary>
    public static bool GetIsEnabled(Control element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the surface.</summary>
    public static void SetIsEnabled(Control element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets whether Ctrl + wheel zooms.</summary>
    public static bool GetZoomEnabled(Control element) => (bool)GetRequired(element, ZoomEnabledProperty)!;

    /// <summary>Sets whether Ctrl + wheel zooms.</summary>
    public static void SetZoomEnabled(Control element, bool value) => SetRequired(element, ZoomEnabledProperty, value);

    /// <summary>Gets the viewport part name.</summary>
    public static string? GetScrollViewerName(Control element) => (string?)GetRequired(element, ScrollViewerNameProperty);

    /// <summary>Names the viewport part.</summary>
    public static void SetScrollViewerName(Control element, string? value)
        => SetRequired(element, ScrollViewerNameProperty, value);

    /// <summary>Gets the canvas part name.</summary>
    public static string? GetCanvasName(Control element) => (string?)GetRequired(element, CanvasNameProperty);

    /// <summary>Names the canvas part.</summary>
    public static void SetCanvasName(Control element, string? value) => SetRequired(element, CanvasNameProperty, value);

    /// <summary>Gets the grid-decorator part name.</summary>
    public static string? GetGridDecoratorName(Control element)
        => (string?)GetRequired(element, GridDecoratorNameProperty);

    /// <summary>Names the grid-decorator part.</summary>
    public static void SetGridDecoratorName(Control element, string? value)
        => SetRequired(element, GridDecoratorNameProperty, value);

    /// <summary>Gets the minimap part name.</summary>
    public static string? GetMinimapOverlayName(Control element)
        => (string?)GetRequired(element, MinimapOverlayNameProperty);

    /// <summary>Names the minimap part.</summary>
    public static void SetMinimapOverlayName(Control element, string? value)
        => SetRequired(element, MinimapOverlayNameProperty, value);

    /// <summary>Gets the WPF-parity press-source name (unused by the visual path).</summary>
    public static string? GetPointerPressSourceName(Control element)
        => (string?)GetRequired(element, PointerPressSourceNameProperty);

    /// <summary>Sets the WPF-parity press-source name.</summary>
    public static void SetPointerPressSourceName(Control element, string? value)
        => SetRequired(element, PointerPressSourceNameProperty, value);

    /// <summary>Gets the explicit visual selector.</summary>
    public static VisualTemplateSelector? GetVisualTemplateSelector(Control element)
        => (VisualTemplateSelector?)GetRequired(element, VisualTemplateSelectorProperty);

    /// <summary>Sets the explicit visual selector; it wins over the tree resource key.</summary>
    public static void SetVisualTemplateSelector(Control element, VisualTemplateSelector? value)
        => SetRequired(element, VisualTemplateSelectorProperty, value);

    /// <summary>Gets the selector resource key.</summary>
    public static string? GetVisualTemplateSelectorKey(Control element)
        => (string?)GetRequired(element, VisualTemplateSelectorKeyProperty);

    /// <summary>Sets the selector resource key.</summary>
    public static void SetVisualTemplateSelectorKey(Control element, string? value)
        => SetRequired(element, VisualTemplateSelectorKeyProperty, value);

    /// <summary>Gets the resting link colour.</summary>
    public static Color GetVisualLinkColor(Control element) => (Color)GetRequired(element, VisualLinkColorProperty)!;

    /// <summary>Sets the resting link colour.</summary>
    public static void SetVisualLinkColor(Control element, Color value)
        => SetRequired(element, VisualLinkColorProperty, value);

    /// <summary>Gets the resting link width.</summary>
    public static float GetVisualLinkThickness(Control element)
        => (float)GetRequired(element, VisualLinkThicknessProperty)!;

    /// <summary>Sets the resting link width.</summary>
    public static void SetVisualLinkThickness(Control element, float value)
        => SetRequired(element, VisualLinkThicknessProperty, value);

    /// <summary>Gets the hovered link colour.</summary>
    public static Color GetVisualLinkHoverColor(Control element)
        => (Color)GetRequired(element, VisualLinkHoverColorProperty)!;

    /// <summary>Sets the hovered link colour.</summary>
    public static void SetVisualLinkHoverColor(Control element, Color value)
        => SetRequired(element, VisualLinkHoverColorProperty, value);

    /// <summary>Gets the hovered link width.</summary>
    public static float GetVisualLinkHoverThickness(Control element)
        => (float)GetRequired(element, VisualLinkHoverThicknessProperty)!;

    /// <summary>Sets the hovered link width.</summary>
    public static void SetVisualLinkHoverThickness(Control element, float value)
        => SetRequired(element, VisualLinkHoverThicknessProperty, value);

    /// <summary>Gets the virtual-link colour.</summary>
    public static Color GetVisualLinkVirtualColor(Control element)
        => (Color)GetRequired(element, VisualLinkVirtualColorProperty)!;

    /// <summary>Sets the virtual-link colour.</summary>
    public static void SetVisualLinkVirtualColor(Control element, Color value)
        => SetRequired(element, VisualLinkVirtualColorProperty, value);

    /// <summary>Gets whether links glow.</summary>
    public static bool GetVisualLinkGlow(Control element) => (bool)GetRequired(element, VisualLinkGlowProperty)!;

    /// <summary>Sets whether links glow.</summary>
    public static void SetVisualLinkGlow(Control element, bool value)
        => SetRequired(element, VisualLinkGlowProperty, value);

    /// <summary>Gets the explicitly bound tree.</summary>
    public static Wf.ITreeComponent? GetWorkflowTree(Control element)
        => (Wf.ITreeComponent?)GetRequired(element, WorkflowTreeProperty);

    /// <summary>Binds a tree explicitly.</summary>
    public static void SetWorkflowTree(Control element, Wf.ITreeComponent? value)
        => SetRequired(element, WorkflowTreeProperty, value);

    /// <summary>Gets the link-menu builder.</summary>
    public static Action<ContextMenuStrip, Wf.ILinkComponent>? GetLinkMenuBuilder(Control element)
        => (Action<ContextMenuStrip, Wf.ILinkComponent>?)GetRequired(element, LinkMenuBuilderProperty);

    /// <summary>Sets the link-menu builder.</summary>
    public static void SetLinkMenuBuilder(Control element, Action<ContextMenuStrip, Wf.ILinkComponent>? value)
        => SetRequired(element, LinkMenuBuilderProperty, value);

    /// <summary>Gets the visual link-menu resource key.</summary>
    public static string? GetLinkMenuKey(Control element) => (string?)GetRequired(element, LinkMenuKeyProperty);

    /// <summary>Sets the visual link-menu resource key.</summary>
    public static void SetLinkMenuKey(Control element, string? value)
        => SetRequired(element, LinkMenuKeyProperty, value);

    /// <summary>Re-reads the parts, tree, selector and camera.</summary>
    public static void Refresh(Control host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        var state = GetState(host);
        EnsureParts(host, state);
        EnsureTree(host, state);
        EnsurePool(host, state);
        ApplyCamera(state);
        EnsureAttachedBehaviors(state.Root);
    }

    /// <summary>The camera scale; one means world units equal surface units.</summary>
    public static double GetZoom(Control host)
        => States.TryGetValue(host, out var state) ? state.Zoom : 1d;

    /// <summary>The signed pan translation applied after scaling.</summary>
    public static DrawingPointF GetPan(Control host)
        => States.TryGetValue(host, out var state) ? state.Pan : DrawingPointF.Empty;

    /// <summary>Sets the pan translation.</summary>
    public static void SetPan(Control host, DrawingPointF pan)
    {
        if (States.TryGetValue(host, out var state))
        {
            state.Pan = pan;
            ApplyCamera(state);
        }
    }

    /// <summary>Converts a surface point into world coordinates.</summary>
    public static DrawingPointF SurfaceToWorld(Control host, DrawingPointF surfacePoint)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return surfacePoint;
        }

        var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
        return new DrawingPointF(
            (float)((surfacePoint.X - state.Pan.X - state.Content.X) / zoom),
            (float)((surfacePoint.Y - state.Pan.Y - state.Content.Y) / zoom));
    }

    /// <summary>Converts a world point into surface coordinates.</summary>
    public static DrawingPointF WorldToSurface(Control host, DrawingPointF worldPoint)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return worldPoint;
        }

        return new DrawingPointF(
            (float)((worldPoint.X * state.Zoom) + state.Pan.X + state.Content.X),
            (float)((worldPoint.Y * state.Zoom) + state.Pan.Y + state.Content.Y));
    }

    /// <summary>Returns the visible region's top-left in world coordinates.</summary>
    public static Wf.Offset GetWorldViewport(Control host)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return new Wf.Offset();
        }

        var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
        return new Wf.Offset(
            (-state.Pan.X - state.Content.X) / zoom,
            (-state.Pan.Y - state.Content.Y) / zoom);
    }

    /// <summary>Moves the camera so its visible region starts at <paramref name="worldOffset"/>.</summary>
    public static void SetWorldViewport(Control host, Wf.Offset worldOffset)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return;
        }

        state.Pan = new DrawingPointF(
            (float)(-(worldOffset.Horizontal * state.Zoom) - state.Content.X),
            (float)(-(worldOffset.Vertical * state.Zoom) - state.Content.Y));
        ApplyCamera(state);
    }

    /// <summary>Zooms around a surface point, keeping the world point under it fixed.</summary>
    public static void ZoomAt(Control host, DrawingPointF surfacePoint, double factor)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return;
        }

        var zoom = XamlMath.Clamp(state.Zoom * factor, 0.1d, 10d);
        var world = SurfaceToWorld(host, surfacePoint);
        state.Zoom = zoom;
        state.Pan = new DrawingPointF(
            (float)(surfacePoint.X - (world.X * zoom) - state.Content.X),
            (float)(surfacePoint.Y - (world.Y * zoom) - state.Content.Y));
        ApplyCamera(state);
    }

    /// <summary>Routes one wheel turn through the camera or the workflow scroller.</summary>
    public static bool ProcessWheel(Control host, DrawingPointF surfacePoint, int delta, Keys modifiers)
    {
        if (!States.TryGetValue(host, out var state) || state.Tree is null)
        {
            return false;
        }

        HandleWheel(state, surfacePoint, delta, modifiers);
        return true;
    }

    internal static Wf.WorkflowEventHandle? GetPressHandle(VisualElement element)
        => PressHandles.TryGetValue(element, out var handle) ? handle : null;

    // ── lifecycle ────────────────────────────────────────────────────────────

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

    private static void OnConfigurationChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        if (!States.TryGetValue(target, out var state) || !state.Attached)
        {
            return;
        }

        EnsureParts(target, state);
        EnsureTree(target, state);
        EnsurePool(target, state);
        ApplyCamera(state);
        EnsureAttachedBehaviors(state.Root);
    }

    private static void OnLinkStyleChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        if (States.TryGetValue(target, out var state) && state.Attached)
        {
            ApplyLinkStyles(state);
        }
    }

    private static void Attach(Control host)
    {
        var state = GetState(host);
        if (state.Attached)
        {
            return;
        }

        if (host is not VisualHost visualHost)
        {
            return;
        }

        state.Attached = true;
        state.VisualHost = visualHost;
        Application.AddMessageFilter(state);
        NativeWindowStyleHelper.EnsureClipChildren(host);
        NativeWindowStyleHelper.EnsureComposited(host);

        host.HandleCreated += state.OnHandleCreated;
        host.Disposed += state.OnDisposed;
        host.Layout += state.OnLayout;
        host.SizeChanged += state.OnSizeChanged;
        visualHost.LayoutUpdated += state.OnLayoutUpdated;
        visualHost.MouseMove += state.OnHostMouseMove;
        visualHost.MouseUp += state.OnHostMouseUp;
        ViewPool.Changed += state.OnPoolChanged;

        EnsureParts(host, state);
        EnsureTree(host, state);
        EnsurePool(host, state);
        ApplyCamera(state);
        EnsureAttachedBehaviors(state.Root);
    }

    private static void Detach(Control host)
    {
        if (!States.TryGetValue(host, out var state) || !state.Attached)
        {
            return;
        }

        state.Attached = false;
        Application.RemoveMessageFilter(state);
        host.HandleCreated -= state.OnHandleCreated;
        host.Disposed -= state.OnDisposed;
        host.Layout -= state.OnLayout;
        host.SizeChanged -= state.OnSizeChanged;
        if (state.VisualHost is { } visualHost)
        {
            visualHost.LayoutUpdated -= state.OnLayoutUpdated;
            visualHost.MouseMove -= state.OnHostMouseMove;
            visualHost.MouseUp -= state.OnHostMouseUp;
            ViewPool.Changed -= state.OnPoolChanged;
        }

        DetachTree(host, state);
        ClearParts(state);
        ClearPressHandles(state);
        if (state.VisualHost is { } visual)
        {
            visual.InvalidateSurface();
        }

        States.Remove(host);
    }

    private static SurfaceState GetState(Control host)
        => States.GetValue(host, static control => new SurfaceState(control));

    // ── parts ────────────────────────────────────────────────────────────────

    private static void EnsureParts(Control host, SurfaceState state)
    {
        if (host is not VisualHost visualHost)
        {
            return;
        }

        var root = visualHost.Root as VisualElement;
        if (!ReferenceEquals(root, state.Root))
        {
            UnhookInput(state);
            state.Root = root;
            HookInput(state);
        }

        state.Canvas = root is null ? null : FindVisualNamed(root, GetCanvasName(host));
        var scroll = root is null ? null : FindVisualNamed(root, GetScrollViewerName(host));
        if (!ReferenceEquals(state.Scroll, scroll))
        {
            if (state.Scroll is VisualScrollViewer previous)
            {
                previous.ScrollOffsetRequested -= state.OnScrollOffsetRequested;
                previous.IsExternalScroll = false;
            }

            state.Scroll = scroll;
            if (scroll is VisualScrollViewer viewer)
            {
                viewer.IsExternalScroll = true;
                viewer.ScrollOffsetRequested += state.OnScrollOffsetRequested;
            }
        }
        state.Grid = root is null ? null : FindVisualNamed(root, GetGridDecoratorName(host));
        state.GridDecorator = state.Grid as IWorkflowGridDecorator;
        state.PressSource = root is null ? null : FindVisualNamed(root, GetPointerPressSourceName(host));
        state.Info = root is null ? null : FindVisualNamed(root, "PART_InfoOverlay") as WorkflowInfoOverlay;

        var minimap = root is null ? null : FindVisualNamed(root, GetMinimapOverlayName(host));
        if (!ReferenceEquals(state.Minimap, minimap))
        {
            if (state.Minimap is IWorkflowMinimapScrollSource previous)
            {
                previous.ViewportScrollRequested -= state.OnMinimapScrollRequested;
            }

            state.Minimap = minimap;
            if (minimap is IWorkflowMinimapScrollSource source)
            {
                source.ViewportScrollRequested += state.OnMinimapScrollRequested;
            }
        }
    }

    private static void ClearParts(SurfaceState state)
    {
        UnhookInput(state);
        if (state.Minimap is IWorkflowMinimapScrollSource minimap)
        {
            minimap.ViewportScrollRequested -= state.OnMinimapScrollRequested;
        }

        if (state.Scroll is VisualScrollViewer viewer)
        {
            viewer.ScrollOffsetRequested -= state.OnScrollOffsetRequested;
            viewer.IsExternalScroll = false;
        }

        state.Minimap = null;
        state.Info = null;
        state.PressSource = null;
        state.Root = null;
        state.Canvas = null;
        state.Scroll = null;
        state.Grid = null;
        state.GridDecorator = null;
    }

    private static VisualElement? FindVisualNamed(VisualElement root, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindVisualNamed(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ── tree / pool ──────────────────────────────────────────────────────────

    private static void EnsureTree(Control host, SurfaceState state)
    {
#if NET8_0_OR_GREATER
        var dataTree = host.DataContext as Wf.ITreeComponent;
#else
        // Older WinForms TFMs have no Control.DataContext; hosts there bind the tree explicitly.
        var dataTree = host.Tag as Wf.ITreeComponent;
#endif
        var resolved = GetWorkflowTree(host) ?? dataTree;
        if (ReferenceEquals(resolved, state.Tree))
        {
            return;
        }

        DetachTree(host, state);
        state.Tree = resolved;
        if (resolved is null)
        {
            return;
        }

        if (state.Root is { } root)
        {
            root.DataContext = resolved;
        }

        // The visual surface zooms through its camera, not through the model's collapse factor.
        if (resolved.Layout is { } layout
            && (Math.Abs(layout.Scale.Horizontal - 1d) > 0.0001d || Math.Abs(layout.Scale.Vertical - 1d) > 0.0001d))
        {
            layout.Scale = new Wf.Scale(1d, 1d);
        }

        HookMenu(state);
        RegisterScroller(state, resolved);
        if (resolved.Layout is { } treeLayout)
        {
            state.Pan = new DrawingPointF(
                (float)(-(treeLayout.ViewportOffset.Horizontal * state.Zoom) - state.Content.X),
                (float)(-(treeLayout.ViewportOffset.Vertical * state.Zoom) - state.Content.Y));
        }

        EnsurePool(host, state);
    }

    private static void DetachTree(Control host, SurfaceState state)
    {
        if (state.Tree is { } tree)
        {
            if (tree.Layout is { } layout)
            {
                layout.ViewportOffset = new Wf.Offset(
                    (-state.Pan.X - state.Content.X) / (state.Zoom <= 0d ? 1d : state.Zoom),
                    (-state.Pan.Y - state.Content.Y) / (state.Zoom <= 0d ? 1d : state.Zoom));
            }

            if (state.Canvas is { } canvas)
            {
                ViewPool.SetItemsSource(canvas, null);
            }

            ClearScroller(state, tree);
            UnhookMenu(state);
        }

        state.Tree = null;
        if (state.Root is { } root)
        {
            root.DataContext = null;
        }
    }

    private static void EnsurePool(Control host, SurfaceState state)
    {
        if (state.Canvas is not { } canvas || state.Tree is not { } tree)
        {
            return;
        }

        if (ViewPool.GetItemsSource(canvas) is null)
        {
            ViewPool.SetItemsSource(canvas, tree.GetHelper().VisibleItems);
        }

        if (ViewPool.GetTemplateSelector(canvas) is null)
        {
            var selector = ResolveVisualSelector(host, state);
            if (selector is not null)
            {
                ViewPool.SetTemplateSelector(canvas, selector);
            }
        }

        ViewPool.Refresh(canvas);
        EnsureAttachedBehaviors(state.Root);
        ApplyLinkStyles(state);
    }

    private static VisualTemplateSelector? ResolveVisualSelector(Control host, SurfaceState state)
    {
        if (GetVisualTemplateSelector(host) is { } explicitSelector)
        {
            return explicitSelector;
        }

        var key = GetVisualTemplateSelectorKey(host);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (state.Root is { } root
            && VisualResourceLookup.FindResource(root, key!) is VisualTemplateSelector treeSelector)
        {
            return treeSelector;
        }

        return state.VisualHost is { } visualHost
               && visualHost.Resources.TryGetValue(key!, out var value)
               && value is VisualTemplateSelector surfaceSelector
            ? surfaceSelector
            : null;
    }

    private static void EnsureAttachedBehaviors(VisualElement? root)
    {
        if (root is null)
        {
            return;
        }

        Walk(root);
        return;

        void Walk(VisualElement element)
        {
            WorkflowSlotLayoutBehavior.Ensure(element);
            WorkflowNodeDragBehavior.Ensure(element);
            WorkflowSlotConnectionBehavior.Ensure(element);
            foreach (var child in element.Children)
            {
                if (child is VisualElement nested)
                {
                    Walk(nested);
                }
            }
        }
    }

    // ── camera / decorations ─────────────────────────────────────────────────

    private static void ApplyCamera(SurfaceState state)
    {
        if (state.Canvas is not { } canvas)
        {
            return;
        }

        // 画布尺寸就是 ScrollViewer 的内容尺寸：世界范围 × 相机缩放，至少铺满视口。
        // 滚动条因此由内容自身的测量结果驱动，节点/缩放改变范围时条会跟着变。
        var cameraZoom = state.Zoom <= 0d ? 1d : state.Zoom;
        var content = VisualContentOffset(state);
        state.Content = new DrawingPointF((float)content.Horizontal, (float)content.Vertical);
        var bounds = ScrollWorldBounds(state);
        var hostClient = state.VisualHost?.LogicalClientSize ?? DrawingSize.Empty;
        var extentWidth = (float)Math.Max(hostClient.Width, (bounds.MaxX - bounds.MinX) * cameraZoom);
        var extentHeight = (float)Math.Max(hostClient.Height, (bounds.MaxY - bounds.MinY) * cameraZoom);
        if (double.IsNaN(canvas.Width) || Math.Abs(canvas.Width - extentWidth) > 0.01)
        {
            canvas.Width = extentWidth;
        }

        if (double.IsNaN(canvas.Height) || Math.Abs(canvas.Height - extentHeight) > 0.01)
        {
            canvas.Height = extentHeight;
        }

        state.ExternalOriginX = (float)(bounds.MinX * cameraZoom);
        state.ExternalOriginY = (float)(bounds.MinY * cameraZoom);
        canvas.Transform = new Matrix(
            (float)state.Zoom,
            0f,
            0f,
            (float)state.Zoom,
            state.Pan.X + state.Content.X,
            state.Pan.Y + state.Content.Y);
        canvas.InvalidateVisual();

        if (state.Scroll is VisualScrollViewer viewer)
        {
            viewer.SetExternalOffset(
                -state.Pan.X - state.Content.X - state.ExternalOriginX,
                -state.Pan.Y - state.Content.Y - state.ExternalOriginY);
        }

        if (state.GridDecorator is { } grid)
        {
            grid.ScrollOffsetX = -state.Pan.X;
            grid.ScrollOffsetY = -state.Pan.Y;
            grid.ContentOffsetX = state.Content.X;
            grid.ContentOffsetY = state.Content.Y;
            if (state.Grid is { } gridVisual)
            {
                gridVisual.InvalidateVisual();
            }
        }

        if (state.Minimap is IWorkflowMinimapOverlay minimap)
        {
            var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
            var viewport = SurfaceViewport(state);
            minimap.ScrollOffsetX = -(state.Pan.X + state.Content.X) / zoom;
            minimap.ScrollOffsetY = -(state.Pan.Y + state.Content.Y) / zoom;
            minimap.ContentOffsetX = 0d;
            minimap.ContentOffsetY = 0d;
            minimap.ViewportWidth = viewport.Width / zoom;
            minimap.ViewportHeight = viewport.Height / zoom;
            minimap.WorkflowTree = state.Tree;
            state.Minimap.InvalidateVisual();
        }

        if (state.Info is { } info)
        {
            info.ScrollOffsetX = -state.Pan.X;
            info.ScrollOffsetY = -state.Pan.Y;
            info.ContentOffsetX = state.Content.X;
            info.ContentOffsetY = state.Content.Y;
            info.Zoom = state.Zoom;
            info.InvalidateVisual();
        }

        if (state.Tree is { } tree && state.VisualHost is not null)
        {
            var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
            var viewport = SurfaceViewport(state);
            var band = state.GridDecorator?.RulerBand ?? 0d;
            tree.SetVirtualizeInset(left: band, top: band);
            tree.GetHelper().Viewport = new Wf.Viewport(
                (-state.Pan.X - state.Content.X) / zoom,
                (-state.Pan.Y - state.Content.Y) / zoom,
                viewport.Width / zoom,
                viewport.Height / zoom);
        }
    }

    // 表面视口是 ScrollViewer 的 presenter 区域（滚动条占位后剩下的部分）；还没排版时退回客户区。
    private static DrawingSize SurfaceViewport(SurfaceState state)
    {
        if (state.Scroll is VisualScrollViewer viewer
            && viewer.ViewportWidth > 1f
            && viewer.ViewportHeight > 1f)
        {
            return new DrawingSize(
                (int)Math.Round(viewer.ViewportWidth),
                (int)Math.Round(viewer.ViewportHeight));
        }

        var client = state.VisualHost?.LogicalClientSize ?? DrawingSize.Empty;
        return new DrawingSize(
            Math.Max(0, client.Width - (int)Math.Ceiling(state.Content.X)),
            Math.Max(0, client.Height - (int)Math.Ceiling(state.Content.Y)));
    }

    private static Wf.Offset VisualContentOffset(SurfaceState state)
    {
        var content = state.Tree?.Layout?.ActualOffset ?? new Wf.Offset();
        var band = state.GridDecorator?.RulerBand ?? 0d;
        return new Wf.Offset(content.Horizontal + band, content.Vertical + band);
    }

    // 滚动范围取「原点尺寸 ∪ 所有节点」，节点被拖到原点外或新增在右侧时滚动条自动长出去。
    private static (double MinX, double MinY, double MaxX, double MaxY) ScrollWorldBounds(SurfaceState state)
    {
        var layout = state.Tree?.Layout;
        var minX = 0d;
        var minY = 0d;
        var maxX = Math.Max(0d, layout?.ActualSize.Width ?? 0d);
        var maxY = Math.Max(0d, layout?.ActualSize.Height ?? 0d);

        if (state.Tree is { } tree && tree.Nodes is { } nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Anchor is not { } anchor || node.Size is not { } size)
                {
                    // 反序列化/构造中的节点可能暂时没有几何；没有几何就不参与滚动范围。
                    continue;
                }

                minX = Math.Min(minX, anchor.Horizontal);
                minY = Math.Min(minY, anchor.Vertical);
                maxX = Math.Max(maxX, anchor.Horizontal + size.Width);
                maxY = Math.Max(maxY, anchor.Vertical + size.Height);
            }
        }

        // 换树时从内容范围重新起算；同一棵树里一旦被相机拖出去过，就保留扩展后的范围（WPF 的
        // Negative/PositiveOffset 也是只长不回），拖回内容区时条不会缩回原长度。
        if (!state.WorldBoundsValid || !ReferenceEquals(state.WorldBoundsTree, state.Tree))
        {
            state.WorldBoundsTree = state.Tree;
            state.WorldBoundsValid = true;
            state.WorldMinX = minX;
            state.WorldMinY = minY;
            state.WorldMaxX = maxX;
            state.WorldMaxY = maxY;
        }
        else
        {
            state.WorldMinX = Math.Min(state.WorldMinX, minX);
            state.WorldMinY = Math.Min(state.WorldMinY, minY);
            state.WorldMaxX = Math.Max(state.WorldMaxX, maxX);
            state.WorldMaxY = Math.Max(state.WorldMaxY, maxY);
        }

        // 相机拖出内容边缘时，把可见的内容区也纳入范围：Canvas/ScrollViewer 的 extent 随拖动长大，
        // 条不会在内容边缘之外固定住（WPF 的画布也是靠 Negative/PositiveOffset 这样长出去的）。
        // 左上先扣掉标尺带，初始视口不算作越界。
        if (state.VisualHost is not null)
        {
            var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
            var viewport = SurfaceViewport(state);
            var viewLeft = -state.Pan.X / zoom;
            var viewTop = -state.Pan.Y / zoom;
            state.WorldMinX = Math.Min(state.WorldMinX, viewLeft);
            state.WorldMinY = Math.Min(state.WorldMinY, viewTop);
            state.WorldMaxX = Math.Max(state.WorldMaxX, viewLeft + (viewport.Width / zoom));
            state.WorldMaxY = Math.Max(state.WorldMaxY, viewTop + (viewport.Height / zoom));
        }

        return (state.WorldMinX, state.WorldMinY, state.WorldMaxX, state.WorldMaxY);
    }

    private static void ApplyLinkStyles(SurfaceState state)
    {
        if (state.Canvas is not { } canvas)
        {
            return;
        }

        var host = state.Host;
        foreach (var view in EnumerateVisuals(canvas))
        {
            if (view.DataContext is not Wf.ILinkComponent link)
            {
                continue;
            }

            var changed = SetIfPresent(view, "LineColor", GetVisualLinkColor(host));
            changed |= SetIfPresent(view, "HighlightColor", GetVisualLinkHoverColor(host));
            changed |= SetIfPresent(view, "IsVirtual", ReferenceEquals(link, state.Tree?.VirtualLink));
            if (changed)
            {
                view.InvalidateVisual();
            }
        }
    }

    private static void UpdateHover(SurfaceState state)
    {
        if (state.Canvas is not { } canvas || state.Tree is not { } tree)
        {
            return;
        }

        var hovered = Wf.WorkflowInput.For(tree).HoveredLink;
        foreach (var view in EnumerateVisuals(canvas))
        {
            if (view.DataContext is Wf.ILinkComponent link)
            {
                var highlighted = ReferenceEquals(link, hovered);
                if (SetIfPresent(view, "IsHighlighted", highlighted))
                {
                    view.InvalidateVisual();
                }
            }
        }
    }

    private static bool SetIfPresent(VisualElement view, string propertyName, object? value)
    {
        var property = view.GetType().GetProperty(propertyName);
        if (property is null || !property.CanWrite || value is null || !property.PropertyType.IsInstanceOfType(value))
        {
            return false;
        }

        if (Equals(property.GetValue(view), value))
        {
            return false;
        }

        property.SetValue(view, value);
        return true;
    }

    private static IEnumerable<VisualElement> EnumerateVisuals(VisualElement root)
    {
        foreach (var child in root.Children)
        {
            if (child is VisualElement element)
            {
                yield return element;
                foreach (var nested in EnumerateVisuals(element))
                {
                    yield return nested;
                }
            }
        }
    }

    // ── input ────────────────────────────────────────────────────────────────

    private static void HookInput(SurfaceState state)
    {
        if (state.Root is not { } root)
        {
            return;
        }

        root.PreviewPointerPressed += state.OnPreviewPressed;
        root.PointerMoved += state.OnPointerMoved;
        root.PointerReleased += state.OnPointerReleased;
        root.PointerWheel += state.OnPointerWheel;
        root.PointerExited += state.OnPointerExited;
        root.KeyDown += state.OnKeyDown;
        root.KeyUp += state.OnKeyUp;
    }

    private static void UnhookInput(SurfaceState state)
    {
        if (state.Root is not { } root)
        {
            return;
        }

        root.PreviewPointerPressed -= state.OnPreviewPressed;
        root.PointerMoved -= state.OnPointerMoved;
        root.PointerReleased -= state.OnPointerReleased;
        root.PointerWheel -= state.OnPointerWheel;
        root.PointerExited -= state.OnPointerExited;
        root.KeyDown -= state.OnKeyDown;
        root.KeyUp -= state.OnKeyUp;
    }

    private static void RoutePressed(SurfaceState state, DrawingPointF surfacePoint, VisualPointerEventArgs args)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var world = SurfaceToWorld(state.Host!, surfacePoint);
        var target = ResolveTarget(state, surfacePoint);
        var handle = new Wf.WorkflowEventHandle();
        Wf.WorkflowInput.For(tree).Route(new Wf.PointerPressedEventArgs(
            new Wf.Anchor(world.X, world.Y, 0),
            ToModifiers(args.Modifiers),
            state.VisualHost,
            target,
            ToButton(args.Button),
            args.ClickCount,
            handle));

        StorePressHandles(state, surfacePoint, handle);

        if (args.Button == MouseButtons.Left
            && target is null
            && !handle.PreventDefault
            && !IsOverScrollBar(state, surfacePoint)
            && IsWithinPressSource(state, state.VisualHost?.FindElementAt(surfacePoint)))
        {
            state.Panning = true;
            state.PanStart = surfacePoint;
            state.PanStartPan = state.Pan;
            if (state.VisualHost is { } host)
            {
                host.Capture = true;
            }
        }
    }

    private static void RouteMoved(SurfaceState state, DrawingPointF surfacePoint, VisualPointerEventArgs args)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var world = SurfaceToWorld(state.Host!, surfacePoint);
        tree.SetPointerCommand.Execute(new Wf.Anchor(world.X, world.Y, 0));

        if (tree.VirtualLink is { IsVisible: true })
        {
            tree.StandardSetPreviewReceiver(Wf.SlotHitTestEx.HitTest(tree, world.X, world.Y));
            return;
        }

        Wf.WorkflowInput.For(tree).Route(new Wf.PointerMovedEventArgs(
            new Wf.Anchor(world.X, world.Y, 0),
            ToModifiers(args.Modifiers),
            state.VisualHost,
            ResolveTarget(state, surfacePoint),
            new Wf.WorkflowEventHandle()));
        UpdateHover(state);
    }

    private static void RouteReleased(SurfaceState state, DrawingPointF surfacePoint, VisualPointerEventArgs args)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var world = SurfaceToWorld(state.Host!, surfacePoint);
        var target = ResolveTarget(state, surfacePoint);
        Wf.WorkflowInput.For(tree).Route(new Wf.PointerReleasedEventArgs(
            new Wf.Anchor(world.X, world.Y, 0),
            ToModifiers(args.Modifiers),
            state.VisualHost,
            target,
            ToButton(args.Button),
            args.ClickCount,
            new Wf.WorkflowEventHandle()));

        if (tree.VirtualLink is { IsVisible: true })
        {
            // The release may arrive on the captured sender; resolve the receiver from the release point.
            var receiver = Wf.SlotHitTestEx.HitTest(tree, world.X, world.Y);
            var sender = Wf.WorkflowInput.For(tree).ConnectionSender;
            if (receiver is not null && !ReferenceEquals(receiver, sender))
            {
                receiver.ReceiveConnectionCommand.Execute(null);
            }
            else if (target is not Wf.ISlotComponent)
            {
                tree.ResetVirtualLinkCommand.Execute(null);
            }
        }

        ClearPressHandles(state);
        UpdateHover(state);
    }

    private static void HandleWheel(
        SurfaceState state,
        DrawingPointF surfacePoint,
        int delta,
        Keys modifiers,
        bool horizontalWheel = false)
    {
        if (state.Tree is not { } tree || state.Host is not { } host)
        {
            return;
        }

        var world = SurfaceToWorld(state.Host!, surfacePoint);
        var wheelHandle = new Wf.WorkflowEventHandle();
        Wf.WorkflowInput.For(tree).Route(new Wf.PointerWheelEventArgs(
            new Wf.Anchor(world.X, world.Y, 0),
            ToModifiers(modifiers),
            state.VisualHost,
            ResolveTarget(state, surfacePoint),
            horizontalWheel ? delta / 120d : 0d,
            horizontalWheel ? 0d : delta / 120d,
            wheelHandle));

        if (wheelHandle.PreventDefault)
        {
            return;
        }

        if (!horizontalWheel && (modifiers & Keys.Control) != 0 && GetZoomEnabled(host))
        {
            ZoomAt(host, surfacePoint, delta > 0 ? 1.1d : 1d / 1.1d);
            return;
        }

        var input = Wf.WorkflowInput.For(tree);
        // SurfaceScroller speaks raw wheel delta (one notch = 120). Passing the normalized 1.0 here made the
        // scroller divide by 120 a second time, so a notch moved less than one pixel.
        if (horizontalWheel || (modifiers & Keys.Shift) != 0)
        {
            var horizontalDelta = horizontalWheel ? delta : -delta;
            if (input.Scroller is { } horizontal)
            {
                horizontal.ScrollBy(horizontalDelta, 0d);
            }
            else
            {
                state.Pan = new DrawingPointF(
                    state.Pan.X + (float)((horizontalDelta / 120d) * 60d), state.Pan.Y);
            }
        }
        else if (input.Scroller is { } vertical)
        {
            vertical.ScrollBy(0d, delta);
        }
        else
        {
            state.Pan = new DrawingPointF(state.Pan.X, state.Pan.Y + (float)((delta / 120d) * 60d));
        }

        ApplyCamera(state);
    }

    // 滚动条是画出来的，不参与视觉命中；按在拇指/轨道上时把整笔手势让给 ScrollViewer，别同时平移相机。
    private static bool IsOverScrollBar(SurfaceState state, DrawingPointF surfacePoint)
    {
        if (state.Scroll is not VisualScrollViewer viewer || state.VisualHost is not { } host)
        {
            return false;
        }

        for (var current = host.FindElementAt(surfacePoint); current is not null; current = current.ParentElement)
        {
            // 滚动条现在是模板部件：命中的就是 ScrollBar（只在自己的轨道条带内应答）。
            if (current is VisualScrollBar)
            {
                return true;
            }

            if (ReferenceEquals(current, viewer))
            {
                return viewer.IsOverScrollBar(viewer.PointFromSurface(surfacePoint));
            }
        }

        return false;
    }

    private static bool IsWithinPressSource(SurfaceState state, VisualElement? hit)
    {
        if (state.PressSource is null)
        {
            return true;
        }

        for (var current = hit; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, state.PressSource))
            {
                return true;
            }
        }

        return false;
    }

    private static Wf.IWorkflowComponent? ResolveTarget(SurfaceState state, DrawingPointF surfacePoint)
    {
        if (state.VisualHost is { } host)
        {
            var element = host.FindElementAt(surfacePoint);
            for (var current = element; current is not null; current = current.ParentElement)
            {
                // The tree itself is the root's data context; it is not a pointer target. Only node, slot and
                // link views answer, so an empty-canvas press falls through to the link hit test (and then null).
                if (current.DataContext is Wf.INodeComponent node)
                {
                    return node;
                }

                if (current.DataContext is Wf.ISlotComponent slot)
                {
                    return slot;
                }

                if (current.DataContext is Wf.ILinkComponent link)
                {
                    return link;
                }
            }
        }

        if (state.Tree is { } tree)
        {
            var world = SurfaceToWorld(state.Host!, surfacePoint);
            var radius = Wf.WorkflowInput.For(tree).HitRadius;
            return tree.HitTestVisibleLinks(world.X, world.Y, radius);
        }

        return null;
    }

    private static void StorePressHandles(SurfaceState state, DrawingPointF surfacePoint, Wf.WorkflowEventHandle handle)
    {
        ClearPressHandles(state);
        if (state.VisualHost is not { } host)
        {
            return;
        }

        var element = host.FindElementAt(surfacePoint);
        for (var current = element; current is not null; current = current.ParentElement)
        {
            PressHandles.Remove(current);
            PressHandles.Add(current, handle);
            state.AddPressKey(current);
        }

        state.PressHandle = handle;
    }

    private static void ClearPressHandles(SurfaceState state)
    {
        foreach (var key in state.PressHandleKeys)
        {
            PressHandles.Remove(key);
        }

        state.ClearPressKeys();
        state.PressHandle = null;
    }

    private static Wf.InputModifiers ToModifiers(Keys keys)
    {
        var modifiers = Wf.InputModifiers.None;
        if ((keys & Keys.Alt) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & Keys.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & Keys.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        return modifiers;
    }

    private static Wf.MouseButton ToButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => Wf.MouseButton.Left,
        MouseButtons.Right => Wf.MouseButton.Right,
        MouseButtons.Middle => Wf.MouseButton.Middle,
        MouseButtons.XButton1 => Wf.MouseButton.XButton1,
        MouseButtons.XButton2 => Wf.MouseButton.XButton2,
        _ => Wf.MouseButton.None,
    };

    internal static Wf.InputKey ToKey(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z) return Wf.InputKey.A + ((int)key - (int)Keys.A);
        if (key >= Keys.D0 && key <= Keys.D9) return Wf.InputKey.D0 + ((int)key - (int)Keys.D0);
        if (key >= Keys.F1 && key <= Keys.F12) return Wf.InputKey.F1 + ((int)key - (int)Keys.F1);

        return key switch
        {
            Keys.None => Wf.InputKey.None,
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
            _ => Wf.InputKey.Unknown,
        };
    }

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

    // ── link menu / scroller ────────────────────────────────────────────────

    private static void HookMenu(SurfaceState state)
    {
        if (state.Tree?.GetHelper() is Wf.IInputEvents events)
        {
            state.MenuPressed = (_, e) => ShowLinkMenu(state, e);
            events.Input.PointerPressed += state.MenuPressed;
        }
    }

    private static void UnhookMenu(SurfaceState state)
    {
        if (state.MenuPressed is not null && state.Tree?.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed -= state.MenuPressed;
        }

        state.MenuPressed = null;
        state.LinkMenu?.Dispose();
        state.LinkMenu = null;
    }

    private static void ShowLinkMenu(SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        if (e.Button != Wf.MouseButton.Right
            || e.Target is not Wf.ILinkComponent link
            || e.Handle.PreventDefault
            || state.Host is not { } host)
        {
            return;
        }

        if (ResolveVisualLinkMenu(host, state) is { } visualMenu)
        {
            var surfacePoint = WorldToSurface(host, new DrawingPointF((float)e.Position.Horizontal, (float)e.Position.Vertical));
            var target = state.Canvas is { } canvas ? FindLinkView(canvas, link) ?? state.Root : state.Root;
            if (target is not null)
            {
                visualMenu.DataContext = link;
                visualMenu.OpenAt(target, surfacePoint);
                e.Handle.PreventDefault = true;
            }

            return;
        }

        var menu = new ContextMenuStrip();
        GetLinkMenuBuilder(host)?.Invoke(menu, link);
        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        state.LinkMenu?.Dispose();
        state.LinkMenu = menu;
        menu.Show(Cursor.Position);
    }

    private static VisualContextMenu? ResolveVisualLinkMenu(Control host, SurfaceState state)
    {
        var key = GetLinkMenuKey(host);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (state.Root is { } root
            && VisualResourceLookup.FindResource(root, key!) is VisualContextMenu treeMenu)
        {
            return treeMenu;
        }

        return state.VisualHost is { } visualHost
               && visualHost.Resources.TryGetValue(key!, out var value)
               && value is VisualContextMenu surfaceMenu
            ? surfaceMenu
            : null;
    }

    private static VisualElement? FindLinkView(VisualElement root, Wf.ILinkComponent link)
    {
        foreach (var element in EnumerateVisuals(root))
        {
            if (ReferenceEquals(element.DataContext, link))
            {
                return element;
            }
        }

        return null;
    }

    private static void RegisterScroller(SurfaceState state, Wf.ITreeComponent tree)
    {
        var scroller = new SurfaceScroller(state);
        state.Scroller = scroller;
        var input = Wf.WorkflowInput.For(tree);
        input.Scroller = scroller;
        input.Navigator = scroller;
    }

    private static void ClearScroller(SurfaceState state, Wf.ITreeComponent tree)
    {
        if (state.Scroller is { } scroller)
        {
            var input = Wf.WorkflowInput.For(tree);
            if (ReferenceEquals(input.Scroller, scroller))
            {
                input.Scroller = null;
                input.Navigator = null;
            }
        }

        state.Scroller = null;
    }

    private sealed class SurfaceScroller(SurfaceState state) : Wf.IWorkflowSurfaceScroller, Wf.IWorkflowSurfaceNavigator
    {
        private const double Notch = 120d;
        private const double LineHeight = 16d;

        public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
        {
            var lines = SystemInformation.MouseWheelScrollLines;
            var client = state.VisualHost?.LogicalClientSize ?? DrawingSize.Empty;
            var stepX = lines > 0 ? lines * LineHeight : client.Width;
            var stepY = lines > 0 ? lines * LineHeight : client.Height;
            var dx = wheelDeltaX != 0d ? (wheelDeltaX / Notch) * stepX : 0d;
            var dy = wheelDeltaY != 0d ? (wheelDeltaY / Notch) * stepY : 0d;
            state.Pan = new DrawingPointF(state.Pan.X - (float)dx, state.Pan.Y + (float)dy);
            ApplyCamera(state);
        }

        public void CenterOn(double localX, double localY)
        {
            var client = state.VisualHost?.LogicalClientSize ?? DrawingSize.Empty;
            state.Pan = new DrawingPointF(
                (float)((client.Width / 2d) - (localX * state.Zoom) - state.Content.X),
                (float)((client.Height / 2d) - (localY * state.Zoom) - state.Content.Y));
            ApplyCamera(state);
        }
    }

    // ── state ────────────────────────────────────────────────────────────────

    private sealed class SurfaceState : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHWheel = 0x020E;

        private readonly List<VisualElement> pressHandleKeys = [];

        private bool poolRefreshPending;

        public SurfaceState(Control host)
        {
            Host = host;
        }

        public Control Host { get; }

        public bool Attached { get; set; }

        public VisualHost? VisualHost { get; set; }

        public VisualElement? Root { get; set; }

        public VisualElement? Canvas { get; set; }

        public VisualElement? Scroll { get; set; }

        public VisualElement? Grid { get; set; }

        public IWorkflowGridDecorator? GridDecorator { get; set; }

        public VisualElement? Minimap { get; set; }

        public WorkflowInfoOverlay? Info { get; set; }

        public VisualElement? PressSource { get; set; }

        public Wf.ITreeComponent? Tree { get; set; }

        public object? WorldBoundsTree { get; set; }

        public bool WorldBoundsValid { get; set; }

        public double WorldMinX { get; set; }

        public double WorldMinY { get; set; }

        public double WorldMaxX { get; set; }

        public double WorldMaxY { get; set; }

        public Wf.IWorkflowSurfaceScroller? Scroller { get; set; }

        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }

        public ContextMenuStrip? LinkMenu { get; set; }

        public double Zoom { get; set; } = 1d;

        public DrawingPointF Pan { get; set; }

        public DrawingPointF Content { get; set; }

        // 滚动范围的世界原点（世界坐标 × 当前缩放），请求偏移时用来还原 pan。
        public float ExternalOriginX { get; set; }

        public float ExternalOriginY { get; set; }

        public bool Panning { get; set; }

        public DrawingPointF PanStart { get; set; }

        public DrawingPointF PanStartPan { get; set; }

        public DrawingPointF PointerPosition { get; set; }

        public Wf.WorkflowEventHandle? PressHandle { get; set; }

        public IReadOnlyList<VisualElement> PressHandleKeys => pressHandleKeys;

        public void AddPressKey(VisualElement element) => pressHandleKeys.Add(element);

        public void ClearPressKeys() => pressHandleKeys.Clear();

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is not (WmMouseWheel or WmMouseHWheel)
                || !Attached
                || VisualHost is not { IsDisposed: false, Visible: true } host)
            {
                return false;
            }

            var screen = Cursor.Position;
            if (!host.RectangleToScreen(host.ClientRectangle).Contains(screen))
            {
                return false;
            }

            var client = host.PointToClient(screen);
            var delta = unchecked((short)((long)m.WParam >> 16));
            var point = new DrawingPointF(client.X, client.Y);
            if (m.Msg == WmMouseHWheel)
            {
                return host.ProcessPointerHWheel(point, delta);
            }

            // Some Windows configurations deliver Shift+vertical wheel as a horizontal message elsewhere; when the
            // vertical message does arrive, route it to the same axis instead of letting WinForms swallow it.
            if ((Control.ModifierKeys & Keys.Shift) != 0)
            {
                return host.ProcessPointerHWheel(point, -delta);
            }

            return host.ProcessPointerWheel(point, delta);
        }

        public void OnHandleCreated(object? sender, EventArgs e)
        {
            if (Host.Parent is not null)
            {
                ApplyCamera(this);
            }
        }

        public void OnDisposed(object? sender, EventArgs e)
        {
            if (Attached)
            {
                Detach(Host);
            }
        }

        public void OnLayout(object? sender, LayoutEventArgs e) => ApplyCamera(this);

        public void OnSizeChanged(object? sender, EventArgs e) => ApplyCamera(this);

        public void OnLayoutUpdated(object? sender, EventArgs e)
        {
            // 相机每帧都要跟；但整棵树的行为检查与连线样式推送只在池变化/样式变化时做，
            // 否则一次节点拖动会把每个节点和每条线都重新走一遍并强制重画。
            ApplyCamera(this);
        }

        // ViewPool materializes a card/slot view after the surface's initial walk; ensure its attached behaviors
        // now, otherwise a pooled title row never subscribes to the drag behavior.
        public void OnPoolChanged(object? sender, ViewPoolChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Host, Canvas) && !ReferenceEquals(e.Host, Root))
            {
                return;
            }

            // 一次批量物化会对每个视图各发一次 Changed；整棵树只扫一遍，留到本消息轮的下一拍。
            if (poolRefreshPending)
            {
                return;
            }

            poolRefreshPending = true;
            if (VisualHost is { IsHandleCreated: true, IsDisposed: false } visual)
            {
                try
                {
                    visual.BeginInvoke(new Action(() =>
                    {
                        poolRefreshPending = false;
                        if (Attached)
                        {
                            EnsureAttachedBehaviors(Root);
                            ApplyLinkStyles(this);
                        }
                    }));
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            poolRefreshPending = false;
            EnsureAttachedBehaviors(Root);
            ApplyLinkStyles(this);
        }

        public void OnHostMouseMove(object? sender, MouseEventArgs e)
        {
            if (!Panning || VisualHost is not { } host)
            {
                return;
            }

            var scale = host.DpiScale <= 0f ? 1f : host.DpiScale;
            PointerPosition = new DrawingPointF(e.X / scale, e.Y / scale);
            var delta = new DrawingPointF(PointerPosition.X - PanStart.X, PointerPosition.Y - PanStart.Y);
            Pan = new DrawingPointF(PanStartPan.X + delta.X, PanStartPan.Y + delta.Y);
            ApplyCameraThrottled();
        }

        public void OnHostMouseUp(object? sender, MouseEventArgs e)
        {
            Panning = false;
            if (VisualHost is { } host)
            {
                host.Capture = false;
            }

            ClearPressHandles(this);
            if (Tree is { } tree && tree.VirtualLink is { IsVisible: true })
            {
                var scale = VisualHost?.DpiScale ?? 1f;
                var client = VisualHost?.PointToClient(Cursor.Position) ?? System.Drawing.Point.Empty;
                var surfacePoint = new DrawingPointF(client.X / scale, client.Y / scale);
                var world = SurfaceToWorld(Host, surfacePoint);
                var receiver = Wf.SlotHitTestEx.HitTest(tree, world.X, world.Y);
                if (receiver is not null)
                {
                    receiver.ReceiveConnectionCommand.Execute(null);
                }
                else
                {
                    tree.ResetVirtualLinkCommand.Execute(null);
                }
            }

            FlushCamera();
            UpdateHover(this);
        }

        public void OnPreviewPressed(object? sender, VisualPointerEventArgs args)
        {
            PointerPosition = args.Position;
            RoutePressed(this, args.Position, args);
        }

        public void OnPointerMoved(object? sender, VisualPointerEventArgs args)
        {
            if (Panning)
            {
                return;
            }

            PointerPosition = args.Position;
            RouteMoved(this, args.Position, args);
        }

        public void OnPointerReleased(object? sender, VisualPointerEventArgs args)
        {
            PointerPosition = args.Position;
            Panning = false;
            RouteReleased(this, args.Position, args);
        }

        public void OnPointerWheel(object? sender, VisualPointerEventArgs args)
        {
            PointerPosition = args.Position;
            HandleWheel(this, args.Position, args.Delta, args.Modifiers, args.IsHorizontal);
            args.Handled = true;
        }

        public void OnPointerExited(object? sender, VisualPointerEventArgs args)
        {
            if (Tree is { } tree)
            {
                var input = Wf.WorkflowInput.For(tree);
                input.Route(new Wf.PointerExitedEventArgs(
                    new Wf.Anchor(0d, 0d, 0),
                    Wf.InputModifiers.None,
                    VisualHost,
                    null,
                    new Wf.WorkflowEventHandle()));
            }

            UpdateHover(this);
        }

        public void OnKeyDown(object? sender, VisualKeyEventArgs args)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var input = Wf.WorkflowInput.For(tree);
            input.Route(new Wf.KeyDownEventArgs(
                ToKey(args.KeyCode),
                (int)args.KeyCode,
                ToModifiers(args.Modifiers),
                args.IsRepeat,
                VisualHost,
                input.PointerTarget,
                new Wf.WorkflowEventHandle()));
        }

        public void OnKeyUp(object? sender, VisualKeyEventArgs args)
        {
            if (Tree is not { } tree)
            {
                return;
            }

            var input = Wf.WorkflowInput.For(tree);
            input.Route(new Wf.KeyUpEventArgs(
                ToKey(args.KeyCode),
                (int)args.KeyCode,
                ToModifiers(args.Modifiers),
                false,
                VisualHost,
                input.PointerTarget,
                new Wf.WorkflowEventHandle()));
        }

        public void OnMinimapScrollRequested(double scrollX, double scrollY)
        {
            Pan = new DrawingPointF(
                (float)(-(scrollX * Zoom) - Content.X),
                (float)(-(scrollY * Zoom) - Content.Y));
            ApplyCameraThrottled();
        }

        // 滚动条偏移是「可见世界左上角 − 世界原点」的表面像素；pan 从它反推。
        public void OnScrollOffsetRequested(object? sender, VisualScrollOffsetEventArgs e)
        {
            Pan = new DrawingPointF(
                -e.HorizontalOffset - Content.X - ExternalOriginX,
                -e.VerticalOffset - Content.Y - ExternalOriginY);
            ApplyCameraThrottled();
        }

        // 指针拖动可以到 100+Hz；相机每帧重绘整面，按 60Hz 合并位移，松手时再补最后一帧。
        private readonly System.Diagnostics.Stopwatch _cameraClock = new();
        private bool _cameraPending;

        private void ApplyCameraThrottled()
        {
            if (_cameraClock.Elapsed.TotalMilliseconds >= 1000d / 60d)
            {
                _cameraClock.Restart();
                _cameraPending = false;
                ApplyCamera(this);
                return;
            }

            _cameraPending = true;
        }

        private void FlushCamera()
        {
            if (!_cameraPending)
            {
                return;
            }

            _cameraPending = false;
            _cameraClock.Restart();
            ApplyCamera(this);
        }
    }
}
