using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem.Platform;
using VeloxDev.WorkflowSystem.StandardEx;
using PlatformInput = System.Windows.Forms;
using Wf = VeloxDev.WorkflowSystem;
using VisualCanvas = Easel.Visuals.Controls.Canvas;
using VisualContextMenu = Easel.Visuals.Controls.VisualContextMenu;
using VisualScrollViewer = Easel.Visuals.Controls.ScrollViewer;
using VisualScrollBar = Easel.Visuals.Controls.ScrollBar;
using VisualScrollOffsetEventArgs = Easel.Visuals.Controls.ScrollOffsetRequestedEventArgs;
using XamlVisibility = Easel.Controls.Visibility;
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
    /// <summary>Starts or stops the surface on the visual root.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false) { VisualPropertyChangedCallback = OnVisualIsEnabledChanged });

    /// <summary>Whether Ctrl + wheel zooms the camera.</summary>
    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Name of the visual viewport part.</summary>
    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Name of the visual canvas that hosts the pool.</summary>
    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Name of the grid/ruler decorator part.</summary>
    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Name of the minimap overlay part.</summary>
    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>
    /// Name of the visual subtree that is the workflow surface when the host's root also carries a workbench
    /// shell. Unset means the attached visual root itself is the surface.
    /// </summary>
    public static readonly DependencyProperty SurfaceNameProperty = DependencyProperty.RegisterAttached(
        "SurfaceName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Kept for WPF parity; input is routed by the visual host, so no part is used as a press source.</summary>
    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>The selector that creates the pooled views.</summary>
    public static readonly DependencyProperty VisualTemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "VisualTemplateSelector", typeof(VisualTemplateSelector), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Resource key of the tree's selector; defaults to <c>WorkflowTemplateSelector</c>.</summary>
    public static readonly DependencyProperty VisualTemplateSelectorKeyProperty = DependencyProperty.RegisterAttached(
        "VisualTemplateSelectorKey", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata("WorkflowTemplateSelector") { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Resting link colour.</summary>
    public static readonly DependencyProperty VisualLinkColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Resting link width.</summary>
    public static readonly DependencyProperty VisualLinkThicknessProperty = DependencyProperty.RegisterAttached(
        "VisualLinkThickness", typeof(float), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(2f) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Hovered link colour.</summary>
    public static readonly DependencyProperty VisualLinkHoverColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkHoverColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.White) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Hovered link width.</summary>
    public static readonly DependencyProperty VisualLinkHoverThicknessProperty = DependencyProperty.RegisterAttached(
        "VisualLinkHoverThickness", typeof(float), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(3.5f) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Virtual-link colour.</summary>
    public static readonly DependencyProperty VisualLinkVirtualColorProperty = DependencyProperty.RegisterAttached(
        "VisualLinkVirtualColor", typeof(Color), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Whether resting links glow.</summary>
    public static readonly DependencyProperty VisualLinkGlowProperty = DependencyProperty.RegisterAttached(
        "VisualLinkGlow", typeof(bool), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Fills the link context menu.</summary>
    public static readonly DependencyProperty LinkMenuBuilderProperty = DependencyProperty.RegisterAttached(
        "LinkMenuBuilder", typeof(Action<ContextMenuStrip, Wf.ILinkComponent>), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Resource key of a visual context menu opened on a right-pressed link.</summary>
    public static readonly DependencyProperty LinkMenuKeyProperty = DependencyProperty.RegisterAttached(
        "LinkMenuKey", typeof(string), typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualConfigurationChanged });

    /// <summary>Gets whether the surface is active.</summary>
    public static bool GetIsEnabled(VisualElement element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the surface.</summary>
    public static void SetIsEnabled(VisualElement element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets whether Ctrl + wheel zooms.</summary>
    public static bool GetZoomEnabled(VisualElement element) => (bool)GetRequired(element, ZoomEnabledProperty)!;

    /// <summary>Sets whether Ctrl + wheel zooms.</summary>
    public static void SetZoomEnabled(VisualElement element, bool value) => SetRequired(element, ZoomEnabledProperty, value);

    /// <summary>Gets the viewport part name.</summary>
    public static string? GetScrollViewerName(VisualElement element) => (string?)GetRequired(element, ScrollViewerNameProperty);

    /// <summary>Names the viewport part.</summary>
    public static void SetScrollViewerName(VisualElement element, string? value)
        => SetRequired(element, ScrollViewerNameProperty, value);

    /// <summary>Gets the canvas part name.</summary>
    public static string? GetCanvasName(VisualElement element) => (string?)GetRequired(element, CanvasNameProperty);

    /// <summary>Names the canvas part.</summary>
    public static void SetCanvasName(VisualElement element, string? value) => SetRequired(element, CanvasNameProperty, value);

    /// <summary>Gets the grid-decorator part name.</summary>
    public static string? GetGridDecoratorName(VisualElement element)
        => (string?)GetRequired(element, GridDecoratorNameProperty);

    /// <summary>Names the grid-decorator part.</summary>
    public static void SetGridDecoratorName(VisualElement element, string? value)
        => SetRequired(element, GridDecoratorNameProperty, value);

    /// <summary>Gets the minimap part name.</summary>
    public static string? GetMinimapOverlayName(VisualElement element)
        => (string?)GetRequired(element, MinimapOverlayNameProperty);

    /// <summary>Names the minimap part.</summary>
    public static void SetMinimapOverlayName(VisualElement element, string? value)
        => SetRequired(element, MinimapOverlayNameProperty, value);

    /// <summary>Gets the embedded surface subtree name.</summary>
    public static string? GetSurfaceName(VisualElement element) => (string?)GetRequired(element, SurfaceNameProperty);

    /// <summary>Names the visual subtree that is the surface inside a larger visual root.</summary>
    public static void SetSurfaceName(VisualElement element, string? value)
        => SetRequired(element, SurfaceNameProperty, value);

    /// <summary>Gets the WPF-parity press-source name (unused by the visual path).</summary>
    public static string? GetPointerPressSourceName(VisualElement element)
        => (string?)GetRequired(element, PointerPressSourceNameProperty);

    /// <summary>Sets the WPF-parity press-source name.</summary>
    public static void SetPointerPressSourceName(VisualElement element, string? value)
        => SetRequired(element, PointerPressSourceNameProperty, value);

    /// <summary>Gets the explicit visual selector.</summary>
    public static VisualTemplateSelector? GetVisualTemplateSelector(VisualElement element)
        => (VisualTemplateSelector?)GetRequired(element, VisualTemplateSelectorProperty);

    /// <summary>Sets the explicit visual selector; it wins over the tree resource key.</summary>
    public static void SetVisualTemplateSelector(VisualElement element, VisualTemplateSelector? value)
        => SetRequired(element, VisualTemplateSelectorProperty, value);

    /// <summary>Gets the selector resource key.</summary>
    public static string? GetVisualTemplateSelectorKey(VisualElement element)
        => (string?)GetRequired(element, VisualTemplateSelectorKeyProperty);

    /// <summary>Sets the selector resource key.</summary>
    public static void SetVisualTemplateSelectorKey(VisualElement element, string? value)
        => SetRequired(element, VisualTemplateSelectorKeyProperty, value);

    /// <summary>Gets the resting link colour.</summary>
    public static Color GetVisualLinkColor(VisualElement element) => (Color)GetRequired(element, VisualLinkColorProperty)!;

    /// <summary>Sets the resting link colour.</summary>
    public static void SetVisualLinkColor(VisualElement element, Color value)
        => SetRequired(element, VisualLinkColorProperty, value);

    /// <summary>Gets the resting link width.</summary>
    public static float GetVisualLinkThickness(VisualElement element)
        => (float)GetRequired(element, VisualLinkThicknessProperty)!;

    /// <summary>Sets the resting link width.</summary>
    public static void SetVisualLinkThickness(VisualElement element, float value)
        => SetRequired(element, VisualLinkThicknessProperty, value);

    /// <summary>Gets the hovered link colour.</summary>
    public static Color GetVisualLinkHoverColor(VisualElement element)
        => (Color)GetRequired(element, VisualLinkHoverColorProperty)!;

    /// <summary>Sets the hovered link colour.</summary>
    public static void SetVisualLinkHoverColor(VisualElement element, Color value)
        => SetRequired(element, VisualLinkHoverColorProperty, value);

    /// <summary>Gets the hovered link width.</summary>
    public static float GetVisualLinkHoverThickness(VisualElement element)
        => (float)GetRequired(element, VisualLinkHoverThicknessProperty)!;

    /// <summary>Sets the hovered link width.</summary>
    public static void SetVisualLinkHoverThickness(VisualElement element, float value)
        => SetRequired(element, VisualLinkHoverThicknessProperty, value);

    /// <summary>Gets the virtual-link colour.</summary>
    public static Color GetVisualLinkVirtualColor(VisualElement element)
        => (Color)GetRequired(element, VisualLinkVirtualColorProperty)!;

    /// <summary>Sets the virtual-link colour.</summary>
    public static void SetVisualLinkVirtualColor(VisualElement element, Color value)
        => SetRequired(element, VisualLinkVirtualColorProperty, value);

    /// <summary>Gets whether links glow.</summary>
    public static bool GetVisualLinkGlow(VisualElement element) => (bool)GetRequired(element, VisualLinkGlowProperty)!;

    /// <summary>Sets whether links glow.</summary>
    public static void SetVisualLinkGlow(VisualElement element, bool value)
        => SetRequired(element, VisualLinkGlowProperty, value);

    /// <summary>Gets the link-menu builder.</summary>
    public static Action<ContextMenuStrip, Wf.ILinkComponent>? GetLinkMenuBuilder(VisualElement element)
        => (Action<ContextMenuStrip, Wf.ILinkComponent>?)GetRequired(element, LinkMenuBuilderProperty);

    /// <summary>Sets the link-menu builder.</summary>
    public static void SetLinkMenuBuilder(VisualElement element, Action<ContextMenuStrip, Wf.ILinkComponent>? value)
        => SetRequired(element, LinkMenuBuilderProperty, value);

    /// <summary>Gets the visual link-menu resource key.</summary>
    public static string? GetLinkMenuKey(VisualElement element) => (string?)GetRequired(element, LinkMenuKeyProperty);

    /// <summary>Sets the visual link-menu resource key.</summary>
    public static void SetLinkMenuKey(VisualElement element, string? value)
        => SetRequired(element, LinkMenuKeyProperty, value);

    // ── host behavior bridge ────────────────────────────────────────────────

    private static readonly ConditionalWeakTable<VisualElement, WorkflowSurfaceController> Controllers = new();
    private static readonly ConditionalWeakTable<Control, SurfaceConfig> HostConfigs = new();
    private static readonly SurfaceConfig DefaultConfig = new();

    private static WorkflowSurfaceController GetOrCreateController(VisualElement root)
        => Controllers.GetValue(root, static element => new WorkflowSurfaceController(element));

    private static SurfaceConfig GetConfig(Control host)
        => HostConfigs.TryGetValue(host, out var config) ? config : DefaultConfig;

    private static void SetHostConfig(Control host, SurfaceConfig config)
    {
        if (HostConfigs.TryGetValue(host, out _))
        {
            HostConfigs.Remove(host);
        }

        HostConfigs.Add(host, config);
    }

    private static void OnVisualIsEnabledChanged(IWinformsObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not VisualElement root)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            root.AddHostBehavior(GetOrCreateController(root));
            return;
        }

        if (Controllers.TryGetValue(root, out var controller))
        {
            root.RemoveHostBehavior(controller);
        }
    }

    private static void OnVisualConfigurationChanged(IWinformsObject target, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        if (target is VisualElement root && Controllers.TryGetValue(root, out var controller))
        {
            controller.OnConfigurationChanged();
        }
    }

    // 视觉根 -> 宿主引擎的一张配置快照。引擎仍以宿主为键；控制器只负责把根上的属性搬过来。
    private sealed class SurfaceConfig
    {
        public string? ScrollViewerName { get; set; }

        public string? CanvasName { get; set; }

        public string? GridDecoratorName { get; set; }

        public string? MinimapOverlayName { get; set; }

        public string? SurfaceName { get; set; }

        public string? PointerPressSourceName { get; set; }

        public bool ZoomEnabled { get; set; }

        public VisualTemplateSelector? VisualTemplateSelector { get; set; }

        public string? VisualTemplateSelectorKey { get; set; } = "WorkflowTemplateSelector";

        public Color VisualLinkColor { get; set; } = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);

        public float VisualLinkThickness { get; set; } = 2f;

        public Color VisualLinkHoverColor { get; set; } = Color.White;

        public float VisualLinkHoverThickness { get; set; } = 3.5f;

        public Color VisualLinkVirtualColor { get; set; } = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);

        public bool VisualLinkGlow { get; set; }

        public Wf.ITreeComponent? WorkflowTree { get; set; }

        public Action<ContextMenuStrip, Wf.ILinkComponent>? LinkMenuBuilder { get; set; }

        public string? LinkMenuKey { get; set; }
    }

    private sealed class WorkflowSurfaceController(VisualElement root) : IVisualHostBehavior
    {
        private VisualHost? _host;
        private bool _dataContextQueued;

        // 根级相机/刷新重载要从视觉根找回宿主上的状态。
        internal VisualHost? Host => _host;

        public void Attach(VisualHost host)
        {
            _host = host;
            ApplyConfig(host);
            AttachCore(host);

            // DataContext 是树唯一的来源；继承自宿主的变化同样会从根这里进来。
            root.PropertyChanged += OnRootPropertyChanged;
        }

        public void Detach()
        {
            root.PropertyChanged -= OnRootPropertyChanged;
            _dataContextQueued = false;
            if (_host is { } host)
            {
                DetachCore(host);
                if (HostConfigs.TryGetValue(host, out _))
                {
                    HostConfigs.Remove(host);
                }
            }

            _host = null;
        }

        public void OnConfigurationChanged()
        {
            if (_host is not { } host || !States.TryGetValue(host, out var state) || !state.Attached)
            {
                return;
            }

            ApplyAndRefresh(host);
        }

        private void ApplyAndRefresh(VisualHost host)
        {
            ApplyConfig(host);
            Refresh(host);
        }

        // DataContext 是树唯一的来源；合并连续切换，排到消息泵之后，避免在布局中间重入。
        private void OnRootPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Property, VisualElement.DataContextProperty) || _dataContextQueued)
            {
                return;
            }

            if (_host is not { } host)
            {
                return;
            }

            _dataContextQueued = true;
            host.Post(FlushDataContext);
        }

        private void FlushDataContext()
        {
            _dataContextQueued = false;
            if (_host is not { } host || !States.TryGetValue(host, out var state) || !state.Attached)
            {
                return;
            }

            var tree = root.DataContext as Wf.ITreeComponent;
            if (ReferenceEquals(tree, state.Config.WorkflowTree))
            {
                return;
            }

            ApplyAndRefresh(host);
        }

        private void ApplyConfig(VisualHost host)
        {
            var config = new SurfaceConfig
            {
                ScrollViewerName = GetScrollViewerName(root),
                CanvasName = GetCanvasName(root),
                GridDecoratorName = GetGridDecoratorName(root),
                MinimapOverlayName = GetMinimapOverlayName(root),
                SurfaceName = GetSurfaceName(root),
                PointerPressSourceName = GetPointerPressSourceName(root),
                ZoomEnabled = GetZoomEnabled(root),
                VisualTemplateSelector = GetVisualTemplateSelector(root),
                VisualTemplateSelectorKey = GetVisualTemplateSelectorKey(root),
                VisualLinkColor = GetVisualLinkColor(root),
                VisualLinkThickness = GetVisualLinkThickness(root),
                VisualLinkHoverColor = GetVisualLinkHoverColor(root),
                VisualLinkHoverThickness = GetVisualLinkHoverThickness(root),
                VisualLinkVirtualColor = GetVisualLinkVirtualColor(root),
                VisualLinkGlow = GetVisualLinkGlow(root),
                WorkflowTree = root.DataContext as Wf.ITreeComponent,
                LinkMenuBuilder = GetLinkMenuBuilder(root),
                LinkMenuKey = GetLinkMenuKey(root),
            };

            // 嵌在大根树里的表面视图（工作台）靠名字从宿主根定位；单视图宿主沿用根本身。
            if (string.IsNullOrEmpty(config.SurfaceName) && !ReferenceEquals(host.Root, root))
            {
                config.SurfaceName = root.Name;
            }

            SetHostConfig(host, config);
        }
    }

    // 宿主键引擎入口；视觉根重载解析到宿主后复用。
    private static void Refresh(Control host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        var state = GetState(host);
        EnsureParts(host, state);
        EnsureTree(host, state);
        EnsurePool(host, state);
        ApplyCamera(state);
        EnsureAttachedBehaviors(state.Surface ?? state.Root);
    }

    /// <summary>Re-reads the parts, tree, selector and camera through the visual root's controller.</summary>
    public static void Refresh(VisualElement root)
    {
        if (ResolveHost(root) is { } host && States.TryGetValue(host, out _))
        {
            Refresh(host);
        }
    }

    private static double GetZoom(Control host)
        => States.TryGetValue(host, out var state) ? state.Zoom : 1d;

    /// <summary>The camera scale of the surface attached to the visual root, or one when none is attached.</summary>
    public static double GetZoom(VisualElement root)
        => ResolveState(root)?.Zoom ?? 1d;

    private static DrawingPointF GetPan(Control host)
        => States.TryGetValue(host, out var state) ? state.Pan : DrawingPointF.Empty;

    /// <summary>The pan of the surface attached to the visual root, or the origin when none is attached.</summary>
    public static DrawingPointF GetPan(VisualElement root)
        => ResolveState(root)?.Pan ?? DrawingPointF.Empty;

    private static void SetPan(Control host, DrawingPointF pan)
    {
        if (States.TryGetValue(host, out var state))
        {
            state.Pan = pan;
            ApplyCamera(state);
        }
    }

    /// <summary>Sets the pan translation of the surface attached to the visual root.</summary>
    public static void SetPan(VisualElement root, DrawingPointF pan)
    {
        if (ResolveState(root) is { } state)
        {
            state.Pan = pan;
            ApplyCamera(state);
        }
    }

    private static DrawingPointF SurfaceToWorld(Control host, DrawingPointF surfacePoint)
        => States.TryGetValue(host, out var state) ? SurfaceToWorld(state, surfacePoint) : surfacePoint;

    /// <summary>Converts a surface point of the visual root's surface into world coordinates.</summary>
    public static DrawingPointF SurfaceToWorld(VisualElement root, DrawingPointF surfacePoint)
        => ResolveState(root) is { } state ? SurfaceToWorld(state, surfacePoint) : surfacePoint;

    private static DrawingPointF WorldToSurface(Control host, DrawingPointF worldPoint)
        => States.TryGetValue(host, out var state) ? WorldToSurface(state, worldPoint) : worldPoint;

    /// <summary>Converts a world point into the visual root's surface coordinates.</summary>
    public static DrawingPointF WorldToSurface(VisualElement root, DrawingPointF worldPoint)
        => ResolveState(root) is { } state ? WorldToSurface(state, worldPoint) : worldPoint;

    private static Wf.Offset GetWorldViewport(Control host)
        => States.TryGetValue(host, out var state) ? GetWorldViewport(state) : new Wf.Offset();

    /// <summary>Returns the visual root's visible region's top-left in world coordinates.</summary>
    public static Wf.Offset GetWorldViewport(VisualElement root)
        => ResolveState(root) is { } state ? GetWorldViewport(state) : new Wf.Offset();

    private static void SetWorldViewport(Control host, Wf.Offset worldOffset)
    {
        if (States.TryGetValue(host, out var state))
        {
            SetWorldViewport(state, worldOffset);
        }
    }

    /// <summary>Moves the visual root's camera so its visible region starts at <paramref name="worldOffset"/>.</summary>
    public static void SetWorldViewport(VisualElement root, Wf.Offset worldOffset)
    {
        if (ResolveState(root) is { } state)
        {
            SetWorldViewport(state, worldOffset);
        }
    }

    private static void ZoomAt(Control host, DrawingPointF surfacePoint, double factor)
    {
        if (States.TryGetValue(host, out var state))
        {
            ZoomAt(state, surfacePoint, factor);
        }
    }

    /// <summary>Zooms the visual root's surface around a point, keeping the world point under it fixed.</summary>
    public static void ZoomAt(VisualElement root, DrawingPointF surfacePoint, double factor)
    {
        if (ResolveState(root) is { } state)
        {
            ZoomAt(state, surfacePoint, factor);
        }
    }

    private static bool ProcessWheel(Control host, DrawingPointF surfacePoint, int delta, Keys modifiers)
    {
        if (!States.TryGetValue(host, out var state) || state.Tree is null)
        {
            return false;
        }

        HandleWheel(state, surfacePoint, delta, modifiers);
        return true;
    }

    /// <summary>Routes one wheel turn through the visual root's camera or workflow scroller.</summary>
    public static bool ProcessWheel(VisualElement root, DrawingPointF surfacePoint, int delta, Keys modifiers)
    {
        if (ResolveState(root) is not { } state || state.Tree is null)
        {
            return false;
        }

        HandleWheel(state, surfacePoint, delta, modifiers);
        return true;
    }

    // 根级重载按「视觉根 -> 控制器 -> 宿主 -> 状态」解析；没有附着时调用方走各自的默认值。
    private static VisualHost? ResolveHost(VisualElement root)
        => root is not null && Controllers.TryGetValue(root, out var controller) ? controller.Host : null;

    private static SurfaceState? ResolveState(VisualElement root)
        => ResolveHost(root) is { } host && States.TryGetValue(host, out var state) ? state : null;

    private static DrawingPointF SurfaceToWorld(SurfaceState state, DrawingPointF surfacePoint)
    {
        var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
        var origin = SurfaceOrigin(state);
        return new DrawingPointF(
            (float)(((surfacePoint.X - origin.X) - state.Pan.X - state.Content.X) / zoom),
            (float)(((surfacePoint.Y - origin.Y) - state.Pan.Y - state.Content.Y) / zoom));
    }

    private static DrawingPointF WorldToSurface(SurfaceState state, DrawingPointF worldPoint)
    {
        var origin = SurfaceOrigin(state);
        return new DrawingPointF(
            (float)((worldPoint.X * state.Zoom) + state.Pan.X + state.Content.X) + origin.X,
            (float)((worldPoint.Y * state.Zoom) + state.Pan.Y + state.Content.Y) + origin.Y);
    }

    private static Wf.Offset GetWorldViewport(SurfaceState state)
    {
        var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
        return new Wf.Offset(
            (-state.Pan.X - state.Content.X) / zoom,
            (-state.Pan.Y - state.Content.Y) / zoom);
    }

    private static void SetWorldViewport(SurfaceState state, Wf.Offset worldOffset)
    {
        state.Pan = new DrawingPointF(
            (float)(-(worldOffset.Horizontal * state.Zoom) - state.Content.X),
            (float)(-(worldOffset.Vertical * state.Zoom) - state.Content.Y));
        ApplyCamera(state);
    }

    private static void ZoomAt(SurfaceState state, DrawingPointF surfacePoint, double factor)
    {
        var zoom = Math.Max(0.1d, Math.Min(10d, state.Zoom * factor));
        var world = SurfaceToWorld(state, surfacePoint);
        var origin = SurfaceOrigin(state);
        var local = new DrawingPointF(surfacePoint.X - origin.X, surfacePoint.Y - origin.Y);
        state.Zoom = zoom;
        state.Pan = new DrawingPointF(
            (float)(local.X - (world.X * zoom) - state.Content.X),
            (float)(local.Y - (world.Y * zoom) - state.Content.Y));
        ApplyCamera(state);
    }

    internal static Wf.WorkflowEventHandle? GetPressHandle(VisualElement element)
        => PressHandles.TryGetValue(element, out var handle) ? handle : null;

    // ── lifecycle ────────────────────────────────────────────────────────────

    private static void AttachCore(Control host)
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
        EnsureAttachedBehaviors(state.Surface ?? state.Root);
    }

    private static void DetachCore(Control host)
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
        UnhookInput(state);
        ReleaseParts(state);
        state.Root = null;
        state.Surface = null;
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
        var surface = ResolveSurface(state, root);
        if (!ReferenceEquals(root, state.Root) || !ReferenceEquals(surface, state.Surface))
        {
            UnhookInput(state);
            state.Root = root;
            state.Surface = surface;
            HookInput(state);
        }

        // 表面子树之外是外壳，部件只在表面里找。
        var scope = state.Surface ?? root;
        if (state.PartBindings.Count == 0)
        {
            BindParts(state);
        }

        var config = state.Config;
        // 每次 Ensure 都重解析；注册表按引用比较，没换过的部件不会重订任何东西。
        state.Parts.Set(WorkflowSurfacePart.Canvas, scope is null ? null : FindVisualNamed(scope, config.CanvasName));
        state.Parts.Set(WorkflowSurfacePart.ScrollViewer, scope is null ? null : FindVisualNamed(scope, config.ScrollViewerName));
        state.Parts.Set(WorkflowSurfacePart.GridDecorator, scope is null ? null : FindVisualNamed(scope, config.GridDecoratorName));
        state.Parts.Set(WorkflowSurfacePart.PointerPressSource, scope is null ? null : FindVisualNamed(scope, config.PointerPressSourceName));
        state.Parts.Set(WorkflowSurfacePart.MinimapOverlay, scope is null ? null : FindVisualNamed(scope, config.MinimapOverlayName));
        state.Info = scope is null ? null : FindVisualNamed(scope, "PART_InfoOverlay") as WorkflowInfoOverlay;
    }

    // 订阅随部件走：绑定负责「先退旧、再进新」，并把每个部件持有的处理器收在一条订阅里。
    private static void BindParts(SurfaceState state)
    {
        var parts = state.Parts;

        state.PartBindings.Add(new SurfacePartBinding<VisualElement>(
            parts,
            WorkflowSurfacePart.ScrollViewer,
            scroll =>
            {
                if (scroll is not VisualScrollViewer viewer)
                {
                    return;
                }

                viewer.IsExternalScroll = true;
                viewer.ScrollOffsetRequested += state.OnScrollOffsetRequested;
            },
            scroll =>
            {
                if (scroll is not VisualScrollViewer viewer)
                {
                    return;
                }

                viewer.ScrollOffsetRequested -= state.OnScrollOffsetRequested;
                viewer.IsExternalScroll = false;
            }));

        state.PartBindings.Add(new SurfacePartBinding<VisualElement>(
            parts,
            WorkflowSurfacePart.MinimapOverlay,
            minimap =>
            {
                if (minimap is IWorkflowMinimapScrollSource source)
                {
                    source.ViewportScrollRequested += state.OnMinimapScrollRequested;
                }
            },
            minimap =>
            {
                if (minimap is IWorkflowMinimapScrollSource source)
                {
                    source.ViewportScrollRequested -= state.OnMinimapScrollRequested;
                }
            }));
    }

    private static void ReleaseParts(SurfaceState state)
    {
        // 先释放绑定（各自退订当前部件），再清空注册表：Clear 时已经没人订阅，不会重复退订。
        foreach (var binding in state.PartBindings)
        {
            binding.Dispose();
        }

        state.PartBindings.Clear();
        state.Parts.Clear();
        state.Info = null;
    }

    // 没设表面名时整棵根树就是表面，保持单视图宿主的原契约。
    private static VisualElement? ResolveSurface(SurfaceState state, VisualElement? root)
    {
        if (root is null)
        {
            return null;
        }

        var name = state.Config.SurfaceName;
        return string.IsNullOrWhiteSpace(name) ? root : FindVisualNamed(root, name);
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
        var resolved = state.Config.WorkflowTree;
        if (ReferenceEquals(resolved, state.Tree))
        {
            return;
        }

        // 新树的相机从零开始：无树期间布局推过的 content/pan 不能带进新树，
        // 否则首次 DataContext 绑定的位置会被上一次空表面状态偏掉。
        if (resolved is not null)
        {
            state.Pan = default;
            state.Content = default;
        }

        DetachTree(host, state);
        state.Tree = resolved;
        if (resolved is null)
        {
            return;
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
            // Keep the pre-attach content offset: a zero ViewportOffset means the negative-offset
            // layout stays visible (world viewport -ActualOffset); subtracting a freshly computed
            // content here would center on world 0 and hide the upper/left nodes.
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

        // 小地图的流水线按树订阅节点属性；离树时先断掉，避免这张表面视图把整棵树钉住。
        if (state.Minimap is IWorkflowMinimapOverlay minimap)
        {
            minimap.WorkflowTree = null;
        }

        // 表面的 DataContext 归应用所有；引擎只清自己的池、滚动器、小地图与状态。
        state.Tree = null;
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
            var selector = ResolveVisualSelector(state);
            if (selector is not null)
            {
                ViewPool.SetTemplateSelector(canvas, selector);
            }
        }

        // 这次重建期间池会同步（无头宿主）或稍后（有消息泵）逐批通知；重建结束后统一走一遍行为与连线样式，
        // 重建产出的 Changed 不再各自扫树。
        state.RefreshingPool = true;
        try
        {
            ViewPool.Refresh(canvas);
        }
        finally
        {
            state.RefreshingPool = false;
        }

        EnsureAttachedBehaviors(state.Surface ?? state.Root);
        ApplyLinkStyles(state);
    }

    private static VisualTemplateSelector? ResolveVisualSelector(SurfaceState state)
    {
        if (state.Config.VisualTemplateSelector is { } explicitSelector)
        {
            return explicitSelector;
        }

        var key = state.Config.VisualTemplateSelectorKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if ((state.Surface ?? state.Root) is { } root
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

    // 首装与修复扫描：附着属性的变更回调（Easel 的 VisualPropertyChangedCallback）已经能安装后续变化，
    // 所以这一趟只负责「表面挂上来时，树上已经存在、但回调发生在它入树之前」的元素，以及池重建后的补装。
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
            WorkflowEvents.Ensure(element);
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
        var surfaceSize = SurfaceSize(state);
        var extentWidth = (float)Math.Max(surfaceSize.Width, (bounds.MaxX - bounds.MinX) * cameraZoom);
        var extentHeight = (float)Math.Max(surfaceSize.Height, (bounds.MaxY - bounds.MinY) * cameraZoom);
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

        // 网格画在相机矩阵之外，偏移仍是表面像素：scroll = -pan、content = 布局内容偏移。
        if (state.GridDecorator is { } grid)
        {
            var gridMetrics = new SurfaceMetrics
            {
                ScrollOffsetX = -state.Pan.X,
                ScrollOffsetY = -state.Pan.Y,
                ContentOffsetX = state.Content.X,
                ContentOffsetY = state.Content.Y,
                RulerBand = state.GridDecorator is IWorkflowGridDecorator gridDecorator ? gridDecorator.RulerBand : 0d,
            };
            SurfaceMetricsPush.PushGrid(gridMetrics, grid, state.Tree);

            if (state.Grid is { } gridVisual)
            {
                gridVisual.InvalidateVisual();
            }
        }

        // 小地图与树视口用世界坐标：可见世界左上角 = -(pan + content)/zoom。
        var zoom = state.Zoom <= 0d ? 1d : state.Zoom;
        var viewport = SurfaceViewport(state);
        var minimap = state.Minimap as IWorkflowMinimapOverlay;
        double overlayWidth = viewport.Width;
        double overlayHeight = viewport.Height;
        if (state.Minimap is { } minimapVisual)
        {
            if (minimapVisual.Bounds.Width > 0.5f)
            {
                overlayWidth = minimapVisual.Bounds.Width;
            }

            if (minimapVisual.Bounds.Height > 0.5f)
            {
                overlayHeight = minimapVisual.Bounds.Height;
            }
        }

        var (verticalBar, horizontalBar) = MeasureScrollBars(state);
        var band = state.GridDecorator is IWorkflowGridDecorator decorator ? decorator.RulerBand : 0d;
        var worldMetrics = SurfaceMetrics.Create(
            -(state.Pan.X + state.Content.X) / zoom,
            -(state.Pan.Y + state.Content.Y) / zoom,
            0d,
            0d,
            viewport.Width / zoom,
            viewport.Height / zoom,
            overlayWidth,
            overlayHeight,
            band,
            verticalBar,
            horizontalBar,
            minimap?.MinimapMargin ?? 0d);

        if (minimap is not null)
        {
            SurfaceMetricsPush.PushMinimap(worldMetrics, minimap, state.Tree);
            state.Minimap!.InvalidateVisual();
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
            // 视口与持久化位置都在世界坐标里；写入后节点的虚拟化可见集在同一帧刷新。
            SurfaceMetricsPush.PushViewport(worldMetrics, tree);
        }
    }

    // 滚动条是模板部件：可见时按排版后的条宽让位，部件还没量出宽度时退回系统条宽。
    private static (double Vertical, double Horizontal) MeasureScrollBars(SurfaceState state)
    {
        var vertical = 0d;
        var horizontal = 0d;
        if (state.Scroll is VisualScrollViewer viewer)
        {
            if (viewer.VerticalScrollBar is { Visibility: XamlVisibility.Visible } verticalBar)
            {
                vertical = verticalBar.Bounds.Width > 0.5f
                    ? verticalBar.Bounds.Width
                    : SystemInformation.VerticalScrollBarWidth;
            }

            if (viewer.HorizontalScrollBar is { Visibility: XamlVisibility.Visible } horizontalBar)
            {
                horizontal = horizontalBar.Bounds.Height > 0.5f
                    ? horizontalBar.Bounds.Height
                    : SystemInformation.HorizontalScrollBarHeight;
            }
        }

        return (vertical, horizontal);
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

        var client = SurfaceSize(state);
        return new DrawingSize(
            Math.Max(0, client.Width - (int)Math.Ceiling(state.Content.X)),
            Math.Max(0, client.Height - (int)Math.Ceiling(state.Content.Y)));
    }

    // 输入/命中用宿主坐标，相机与平移用表面内坐标；两者差的就是这个原点。
    private static DrawingPointF SurfaceOrigin(SurfaceState state)
        => state.Surface?.AbsoluteBounds.Location ?? DrawingPointF.Empty;

    // 排版前表面尺寸为零，此时沿用原来的整宿主行为。
    private static bool IsOverSurface(SurfaceState state, DrawingPointF hostPoint)
    {
        if (state.Surface is not { } surface)
        {
            return true;
        }

        var bounds = surface.AbsoluteBounds;
        return bounds.Width <= 0.5f || bounds.Height <= 0.5f || bounds.Contains(hostPoint);
    }

    // 排版前退回宿主客户区。
    private static DrawingSize SurfaceSize(SurfaceState state)
    {
        if (state.Surface is { } surface)
        {
            var bounds = surface.AbsoluteBounds;
            if (bounds.Width > 0.5f && bounds.Height > 0.5f)
            {
                return new DrawingSize(
                    (int)Math.Round(bounds.Width),
                    (int)Math.Round(bounds.Height));
            }
        }

        return state.VisualHost?.LogicalClientSize ?? DrawingSize.Empty;
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

        foreach (var view in EnumerateVisuals(canvas))
        {
            if (view.DataContext is not Wf.ILinkComponent link)
            {
                continue;
            }

            var config = state.Config;
            var changed = SetIfPresent(view, "LineColor", config.VisualLinkColor);
            changed |= SetIfPresent(view, "HighlightColor", config.VisualLinkHoverColor);
            changed |= SetIfPresent(view, "LineThickness", config.VisualLinkThickness);
            changed |= SetIfPresent(view, "HighlightThickness", config.VisualLinkHoverThickness);
            changed |= SetIfPresent(view, "VirtualColor", config.VisualLinkVirtualColor);
            changed |= SetIfPresent(view, "IsGlow", config.VisualLinkGlow);
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
        if (state.Surface is not { } root)
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
        if (state.Surface is not { } root)
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
            // Raw wheel delta (one notch = 120), the same scale ScrollBy consumes and the same
            // PointerWheelEventArgs carries on the other adapters. Normalizing here made a host's
            // documented `ScrollBy(-e.DeltaY, 0)` divide by 120 a second time (barely any movement).
            horizontalWheel ? delta : 0d,
            horizontalWheel ? 0d : delta,
            wheelHandle));

        if (wheelHandle.PreventDefault)
        {
            return;
        }

        if (!horizontalWheel && (modifiers & Keys.Control) != 0 && state.Config.ZoomEnabled)
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

    private static object? GetRequired(VisualElement element, DependencyProperty property)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return element.GetValue(property);
    }

    private static void SetRequired(VisualElement element, DependencyProperty property, object? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        element.SetValue(property, value);
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

        if (ResolveVisualLinkMenu(state) is { } visualMenu)
        {
            var surfacePoint = WorldToSurface(host, new DrawingPointF((float)e.Position.Horizontal, (float)e.Position.Vertical));
            var target = state.Canvas is { } canvas
                ? FindLinkView(canvas, link) ?? state.Surface ?? state.Root
                : state.Surface ?? state.Root;
            if (target is not null)
            {
                visualMenu.DataContext = link;
                visualMenu.OpenAt(target, surfacePoint);
                e.Handle.PreventDefault = true;
            }

            return;
        }

        var menu = new ContextMenuStrip();
        state.Config.LinkMenuBuilder?.Invoke(menu, link);
        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        state.LinkMenu?.Dispose();
        state.LinkMenu = menu;
        menu.Show(Cursor.Position);
    }

    private static VisualContextMenu? ResolveVisualLinkMenu(SurfaceState state)
    {
        var key = state.Config.LinkMenuKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if ((state.Surface ?? state.Root) is { } root
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
            var client = SurfaceSize(state);
            var stepX = lines > 0 ? lines * LineHeight : client.Width;
            var stepY = lines > 0 ? lines * LineHeight : client.Height;
            var dx = wheelDeltaX != 0d ? (wheelDeltaX / Notch) * stepX : 0d;
            var dy = wheelDeltaY != 0d ? (wheelDeltaY / Notch) * stepY : 0d;
            state.Pan = new DrawingPointF(state.Pan.X - (float)dx, state.Pan.Y + (float)dy);
            ApplyCamera(state);
        }

        public void CenterOn(double localX, double localY)
        {
            var client = SurfaceSize(state);
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

        // 重建池期间池会连续发批通知；重建方自己在 Refresh 返回后统一扫树，这些通知不用再各扫一遍。
        public bool RefreshingPool { get; set; }

        public SurfaceState(Control host)
        {
            Host = host;
        }

        public Control Host { get; }

        public SurfaceConfig Config => GetConfig(Host);

        public bool Attached { get; set; }

        public VisualHost? VisualHost { get; set; }

        public VisualElement? Root { get; set; }

        // 根树里的工作流子树；单视图宿主时等于 Root。
        public VisualElement? Surface { get; set; }

        // 部件解析与订阅配对都在 Core 的注册表里：同引用不重订，换件先退旧再进新，Detach 时逐条释放。
        public SurfacePartRegistry Parts { get; } = new();

        public List<IDisposable> PartBindings { get; } = [];

        public VisualElement? Canvas => Parts.Get<VisualElement>(WorkflowSurfacePart.Canvas);

        public VisualElement? Scroll => Parts.Get<VisualElement>(WorkflowSurfacePart.ScrollViewer);

        public VisualElement? Grid => Parts.Get<VisualElement>(WorkflowSurfacePart.GridDecorator);

        public IWorkflowGridDecorator? GridDecorator => Grid as IWorkflowGridDecorator;

        public VisualElement? Minimap => Parts.Get<VisualElement>(WorkflowSurfacePart.MinimapOverlay);

        public WorkflowInfoOverlay? Info { get; set; }

        public VisualElement? PressSource => Parts.Get<VisualElement>(WorkflowSurfacePart.PointerPressSource);

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
            var scale = host.DpiScale <= 0f ? 1f : host.DpiScale;
            if (!IsOverSurface(this, new DrawingPointF(client.X / scale, client.Y / scale)))
            {
                return false;
            }

            var delta = unchecked((short)((long)m.WParam >> 16));
            var point = new DrawingPointF(client.X, client.Y);
            if (m.Msg == WmMouseHWheel)
            {
                return host.ProcessPointerHWheel(point, delta);
            }

            // Shift+vertical wheel stays a VERTICAL report carrying the Shift modifier: the adapter's own
            // wheel path and the host's documented `ScrollBy(-e.DeltaY, 0)` both expect DeltaY. Turning it
            // into a horizontal report here left DeltaY at 0, so a host that refused the default scrolled by 0.
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
                DetachCore(Host);
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
            if (RefreshingPool)
            {
                return;
            }

            if (!ReferenceEquals(e.Host, Canvas)
                && !ReferenceEquals(e.Host, Root)
                && !ReferenceEquals(e.Host, Surface))
            {
                return;
            }

            // Changed 是每批一次；仍留到本消息轮的下一拍，连着来的几批只扫一遍树。
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
                            EnsureAttachedBehaviors(Surface ?? Root);
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
            EnsureAttachedBehaviors(Surface ?? Root);
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
            // 首次拖拽时 Stopwatch 还没启动，Elapsed 恒为 0；若只比较帧间隔，首个手势会一直 pending，
            // 直到 MouseUp 的 FlushCamera 才跳一下。首次调用必须立即应用并把时钟启动起来。
            if (!_cameraClock.IsRunning || _cameraClock.Elapsed.TotalMilliseconds >= 1000d / 60d)
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
