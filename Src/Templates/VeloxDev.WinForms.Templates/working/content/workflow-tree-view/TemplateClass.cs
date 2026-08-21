// VeloxDev customization: A workflow surface that composes the GridDecorator,
// ScrollViewer, Canvas, Minimap, and ViewPool into a single control. Bind
// IWorkflowTreeViewModel via the ViewModel property (or Tag/DataContext) to
// start rendering. Generate the NodeView/SlotView/LinkView templates and wire
// their factories below when you rename the generated types.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(width, height)` unambiguous in generated code.
using Size = System.Drawing.Size;

namespace TemplateNamespace;

/// <summary>
/// A workflow tree surface composing a surface chrome, grid decorator, scroll
/// viewer, absolute-positioned canvas, minimap overlay, and pooled view manager.
/// Set <see cref="ViewModel"/> to a <see cref="IWorkflowTreeViewModel"/> to start
/// rendering nodes, slots, and links with the generated views.
/// </summary>
public sealed class TemplateClass : UserControl
{
    /// <summary>Scrollable viewport host (AutoScroll). The surface behavior reads scroll offsets from this control.</summary>
    public ScrollableControl PART_ScrollViewer { get; }

    /// <summary>
    /// Absolute-positioned canvas that hosts pooled node views and paints links.
    /// The canvas is opaque and draws the grid, rulers, and links itself (WinForms
    /// has no reliable transparent compositing, so the tree no longer layers a
    /// transparent canvas over a separate grid decorator).
    /// </summary>
    public Panel PART_Canvas { get; }

    /// <summary>
    /// Grid + ruler surface. The canvas implements <see cref="IWorkflowGridDecorator"/>
    /// and renders the grid/rulers in its own paint cycle; this property exposes the
    /// same surface for decorator offset pushes and compatibility.
    /// </summary>
    public Control PART_GridDecorator => PART_Canvas;

    /// <summary>Optional minimap overlay that implements <see cref="IWorkflowMinimapOverlay"/>.</summary>
    public Control? PART_MinimapOverlay { get; private set; }

    private readonly Color _surfaceBackground = ParseColor("TemplateSurfaceBackground");
    private readonly Color _surfaceBorderBrush = ParseColor("TemplateSurfaceBorderBrush");
    private readonly int _surfaceBorderThickness = int.Parse("TemplateSurfaceBorderThickness", CultureInfo.InvariantCulture);
    private readonly int _surfaceCornerRadius = int.Parse("TemplateSurfaceCornerRadius", CultureInfo.InvariantCulture);

    private IWorkflowTreeViewModel? _tree;
    private INotifyPropertyChanged? _notifier;
    private readonly TemplateSelector _selector;
    private bool _layoutPending;

    // Canvas pan state: dragging the empty canvas pans by moving the canvas with
    // a signed offset. AutoScroll is disabled because its scroll position is clamped
    // at the origin (>= 0), which would only allow panning down-right; a signed
    // offset pans freely in all four directions. Nodes and slots are children of the
    // canvas and get their own mouse events, so these handlers only run for
    // background drags.
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;
    private Point _panOffset;

    // Link renderers (VirtualLink + all real links) painted by the canvas OnPaint.
    // They are deliberately NOT child controls — mirroring the full demo, which draws
    // links on the canvas to avoid overlapping full-size transparent sibling windows
    // being clipped by WinForms WS_CLIPSIBLINGS (only the topmost paints), which is
    // what made nodes vanish during a connection drag.
    private readonly List<LinkView> _linkRenderers = [];
    private IWorkflowTreeViewModel? _linksSubscribedTree;

    /// <summary>Creates a workflow tree surface and wires the attached behaviors.</summary>
    public TemplateClass()
    {
        DoubleBuffered = true;
        BackColor = _surfaceBackground;

        // Surface chrome is drawn in OnPaintBackground (rounded border). Children
        // dock inside the client area, inset by the border thickness.
        Padding = new Padding(_surfaceBorderThickness);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // Scroll viewer: viewport host. Panning moves the canvas directly (see
        // ApplyPan); AutoScroll is disabled so the control never
        // repositions the canvas from its own (clamped) scroll position.
        // Opaque background: WinForms has no reliable transparent compositing, so
        // the tree no longer layers a transparent viewport over a grid decorator —
        // the canvas paints the grid and rulers itself.
        PART_ScrollViewer = new ScrollableControl
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = _surfaceBackground,
            Name = "PART_ScrollViewer",
        };

        // Canvas: viewport-sized host for pooled node views. It stays docked over
        // the viewport; the pan is expressed as a world-origin translation applied
        // to each node view (see ApplyPan / NodeView.ApplyPosition). A content-sized
        // sheet that moves with the pan instead clipped nodes whose canvas-local
        // position went negative, which made the left/top/top-left regions appear to
        // have no canvas at all. The canvas is opaque and paints the grid, rulers,
        // and links in one pass — no transparent siblings to clip (WS_CLIPSIBLINGS)
        // and no multi-pass compositing that flickers during drags.
        var canvas = new SurfaceCanvas
        {
            Dock = DockStyle.Fill,
            Location = Point.Empty,
            Name = "PART_Canvas",
        };
        canvas.LinkRenderers = _linkRenderers;
        PART_Canvas = canvas;
        PART_ScrollViewer.Controls.Add(PART_Canvas);
        Controls.Add(PART_ScrollViewer);

        // View pool: nodes go into the canvas; links are painted as renderers by the
        // canvas OnPaint (see RebuildLinkRenderers), so only the node factory is needed.
        _selector = new TemplateSelector();
        _selector.NodeViewFactory = CreateNodeView;

        WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        // The canvas renders the grid/rulers, so it is also the grid decorator.
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(this, "PART_Canvas");

        // Pan: dragging the empty canvas moves the canvas by a signed offset,
        // then pushes the new world origin into the grid
        // decorator + minimap so they track.
        PART_Canvas.MouseDown += OnCanvasMouseDown;
        PART_Canvas.MouseMove += OnCanvasMouseMove;
        PART_Canvas.MouseUp += OnCanvasMouseUp;
        PART_Canvas.MouseCaptureChanged += OnCanvasMouseCaptureChanged;

        HandleCreated += OnHandleCreated;
        Resize += OnSurfaceResize;
    }

    /// <summary>
    /// The canvas layer: an opaque surface that hosts pooled node views and paints
    /// the grid, rulers, and link renderers in a single paint pass. Node cards are
    /// children of the canvas and repaint after it, so links render behind the nodes.
    /// There is no transparency anywhere in this stack — WinForms has no reliable
    /// transparent compositing, and an opaque single-layer canvas avoids both the
    /// multi-pass flicker of transparent layering and the WS_CLIPSIBLINGS clipping
    /// of overlapping full-size sibling windows.
    /// </summary>
    private sealed class SurfaceCanvas : Panel, IWorkflowGridDecorator
    {
        // Grid/ruler metrics, shared with the full demo's self-drawn canvas.
        private const double DefaultRulerThickness = 36;
        private const double GridSpacing = 40;
        private const int MajorFreq = 5;
        private const double Eps = 0.001;

        private readonly Color _gridBackground = ParseColor("TemplateSurfaceBackground");
        private readonly Color _rulerBackground = ParseColor("#252526");
        private readonly Color _labelColor = ParseColor("#888888");
        private readonly Color _minorGridColor = ParseColor("#2A2D2E");
        private readonly Color _majorGridColor = ParseColor("#3A3D40");
        private readonly Color _axisColor = ParseColor("#4D4D4D");
        private readonly Color _tickColor = ParseColor("#555555");
        private readonly Color _dividerColor = ParseColor("#3A3D40");
        private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);

        public SurfaceCanvas()
        {
            DoubleBuffered = true;
            BackColor = _gridBackground;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<LinkView>? LinkRenderers { get; set; }

        /// <summary>
        /// Current signed pan offset (world-origin translation). Node views read
        /// this (reflectively, via their parent) to position themselves at
        /// <c>node.Anchor + PanOffset</c> in canvas-local coordinates. Keeping the
        /// canvas fixed over the viewport and translating node positions — rather
        /// than moving the canvas — guarantees every node in the visible world
        /// region lands inside the canvas window, so nothing vanishes after panning
        /// into negative coordinates.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Point PanOffset { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetX { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetY { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetX { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetY { get; set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The grid and rulers render in the background pass so they sit under
            // the node cards (child windows) exactly like the old grid decorator
            // layer, but in the same opaque surface as the links.
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0, 0, Width, Height);
            var ruler = (float)DefaultRulerThickness;
            var contentRect = new RectangleF(
                ruler, ruler,
                Math.Max(0, bounds.Width - ruler),
                Math.Max(0, bounds.Height - ruler));

            using var bgBrush = new SolidBrush(_gridBackground);
            using var rulerBrush = new SolidBrush(_rulerBackground);
            g.FillRectangle(bgBrush, bounds);
            g.FillRectangle(rulerBrush, 0, 0, bounds.Width, ruler);
            g.FillRectangle(rulerBrush, 0, 0, ruler, bounds.Height);

            if (contentRect.Width > 0 && contentRect.Height > 0)
            {
                var saved = g.Save();
                g.SetClip(contentRect);
                DrawGrid(g, contentRect);
                g.Restore(saved);
            }

            DrawRulers(g, bounds, contentRect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var renderers = LinkRenderers;
            if (renderers is null || renderers.Count == 0) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var lv in renderers)
            {
                lv.Render(g);
            }
        }

        private void DrawGrid(Graphics g, RectangleF contentRect)
        {
            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorFreq);
            var worldLeft = ScrollOffsetX - ContentOffsetX;
            var worldTop = ScrollOffsetY - ContentOffsetY;
            var worldRight = worldLeft + contentRect.Width;
            var worldBottom = worldTop + contentRect.Height;

            using var minorPen = new Pen(_minorGridColor, 1f);
            using var majorPen = new Pen(_majorGridColor, 1f);
            using var axisPen = new Pen(_axisColor, 1.2f);

            var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)(contentRect.X + (value - worldLeft));
                var pen = SelectPen(value, majorStep, minorPen, majorPen, axisPen);
                g.DrawLine(pen, x, contentRect.Y, x, contentRect.Bottom);
            }

            var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)(contentRect.Y + (value - worldTop));
                var pen = SelectPen(value, majorStep, minorPen, majorPen, axisPen);
                g.DrawLine(pen, contentRect.X, y, contentRect.Right, y);
            }
        }

        private void DrawRulers(Graphics g, RectangleF bounds, RectangleF contentRect)
        {
            var ruler = (float)DefaultRulerThickness;
            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorFreq);
            var worldLeft = ScrollOffsetX - ContentOffsetX;
            var worldTop = ScrollOffsetY - ContentOffsetY;
            var worldRight = worldLeft + contentRect.Width;
            var worldBottom = worldTop + contentRect.Height;

            using var dividerPen = new Pen(_dividerColor, 1f);
            using var tickPen = new Pen(_tickColor, 1f);
            using var axisPen = new Pen(_axisColor, 1f);
            using var labelBrush = new SolidBrush(_labelColor);
            using var format = new StringFormat(StringFormat.GenericTypographic);

            g.DrawLine(dividerPen, ruler, 0, ruler, bounds.Height);
            g.DrawLine(dividerPen, 0, ruler, bounds.Width, ruler);

            // Top ruler.
            var saved = g.Save();
            g.SetClip(new RectangleF(ruler, 0, contentRect.Width, ruler));
            var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)(contentRect.X + (value - worldLeft));
                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = IsNearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, x, ruler, x, (float)(ruler - tickLength));

                if (isMajor)
                {
                    var text = FormatGridValue(value);
                    g.DrawString(text, _labelFont, labelBrush, x + 3, 2, format);
                }
            }
            g.Restore(saved);

            // Left ruler.
            saved = g.Save();
            g.SetClip(new RectangleF(0, ruler, ruler, contentRect.Height));
            var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)(contentRect.Y + (value - worldTop));
                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = IsNearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, ruler, y, (float)(ruler - tickLength), y);

                if (isMajor)
                {
                    var text = FormatGridValue(value);
                    g.DrawString(text, _labelFont, labelBrush, 3, y + 2, format);
                }
            }
            g.Restore(saved);
        }

        private Pen SelectPen(double value, double majorStep, Pen minorPen, Pen majorPen, Pen axisPen)
            => IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;

        private static bool IsMajorLine(double value, double majorStep)
            => majorStep > 0
                && (Math.Abs(value % majorStep) < Eps
                    || Math.Abs(value % majorStep - majorStep) < Eps
                    || Math.Abs(value % majorStep + majorStep) < Eps);

        private static bool IsNearZero(double value)
            => Math.Abs(value) < Eps;

        private static string FormatGridValue(double value)
        {
            var abs = Math.Abs(value);
            if (abs < 10000)
            {
                return Math.Round(value).ToString(CultureInfo.InvariantCulture);
            }

            if (abs < 1000000)
            {
                return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
            }

            return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
        }
    }

    /// <summary>Gets or sets the workflow tree bound to this surface.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? ViewModel
    {
        get => _tree;
        set
        {
            if (ReferenceEquals(_tree, value)) return;

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnTreeChanged;
                _notifier = null;
            }

            _tree = value;
            Tag = value;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnTreeChanged;
            }

            AttachTree();
            ScheduleLayout();
        }
    }

    /// <summary>Gets or sets the minimap overlay (set to null to hide it).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? MinimapOverlay
    {
        get => PART_MinimapOverlay;
        set
        {
            if (ReferenceEquals(PART_MinimapOverlay, value)) return;
            if (PART_MinimapOverlay is not null)
            {
                if (PART_MinimapOverlay is IWorkflowMinimapScrollSource old)
                {
                    old.ViewportScrollRequested -= OnMinimapScrollRequested;
                }

                Controls.Remove(PART_MinimapOverlay);
                PART_MinimapOverlay = null;
            }

            if (value is null) return;

            PART_MinimapOverlay = value;
            if (value is IWorkflowMinimapScrollSource minimap)
            {
                minimap.ViewportScrollRequested += OnMinimapScrollRequested;
            }

            value.Visible = true;
            value.Name = "PART_MinimapOverlay";
            Controls.Add(value);
            value.BringToFront();
            WorkflowSurfaceBehavior.SetMinimapOverlayName(this, "PART_MinimapOverlay");
        }
    }

    // Dragging the minimap viewport requests a scroll: invert the desired world
    // origin into the signed pan offset (ApplyPan pushes -panOffset back into the
    // minimap, so the viewport tracks the drag).
    private void OnMinimapScrollRequested(double sx, double sy)
    {
        _panOffset = new Point((int)Math.Round(-sx), (int)Math.Round(-sy));
        ApplyPan();
    }

    private void AttachTree()
    {
        WorkflowSurfaceBehavior.SetWorkflowTree(this, _tree);

        // Keep the links subscription tied to the current tree across swaps.
        if (!ReferenceEquals(_linksSubscribedTree, _tree))
        {
            if (_linksSubscribedTree is not null)
            {
                _linksSubscribedTree.Links.CollectionChanged -= OnLinksCollectionChanged;
            }

            _linksSubscribedTree = _tree;
            if (_tree is not null)
            {
                _tree.Links.CollectionChanged += OnLinksCollectionChanged;
            }
        }

        // Reconfigure the node pool (detaches the previous manager, then re-attaches).
        ViewPool.SetItemsSource(PART_Canvas, _tree?.Nodes);
        ViewPool.SetTemplateSelector(PART_Canvas, _selector);

        RebuildLinkRenderers();

        if (_tree is not null && PART_MinimapOverlay is not null)
        {
            WorkflowSurfaceBehavior.Refresh(this);
        }
    }

    /// <summary>
    /// Rebuilds the canvas link renderers: the VirtualLink gesture first, then every
    /// real link. Renderers are <see cref="LinkView"/> objects bound to a link but
    /// never added to the control tree — the canvas paints them in OnPaint, mirroring
    /// the full demo (avoids overlapping full-size transparent sibling windows being
    /// clipped by WS_CLIPSIBLINGS). Rebuilt on any tree.Links change.
    /// </summary>
    private void RebuildLinkRenderers()
    {
        foreach (var lv in _linkRenderers)
        {
            lv.Dispose();
        }

        _linkRenderers.Clear();
        if (_tree is not null)
        {
            _linkRenderers.Add(CreateLinkRenderer(_tree.VirtualLink));
            foreach (var link in _tree.Links)
            {
                _linkRenderers.Add(CreateLinkRenderer(link));
            }
        }

        PART_Canvas.Invalidate();
    }

    private void OnLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new NotifyCollectionChangedEventHandler(OnLinksCollectionChanged), sender, e);
            return;
        }

        RebuildLinkRenderers();
    }

    private LinkView CreateLinkRenderer(IWorkflowLinkViewModel link)
    {
        var view = new LinkView
        {
            ExternalInvalidate = () => { if (!IsDisposed) PART_Canvas.Invalidate(); },
        };
        view.Bind(link);
        return view;
    }

    private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowTreeViewModel.Layout)
            or nameof(IWorkflowTreeViewModel.Nodes)
            or nameof(IWorkflowTreeViewModel.Links))
        {
            ScheduleLayout();
        }
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        HandleCreated -= OnHandleCreated;
        if (Tag is IWorkflowTreeViewModel tagged)
        {
            ViewModel = tagged;
        }

        ScheduleLayout();
    }

    private void OnSurfaceResize(object? sender, EventArgs e) => ScheduleLayout();

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _isPanning) return;

        _isPanning = true;
        _panPressScreen = Cursor.Position;
        _panOffsetAtPress = _panOffset;
        PART_Canvas.Capture = true;
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var current = Cursor.Position;
        _panOffset = new Point(
            _panOffsetAtPress.X + (current.X - _panPressScreen.X),
            _panOffsetAtPress.Y + (current.Y - _panPressScreen.Y));
        ApplyPan();
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        PART_Canvas.Capture = false;
        ApplyPan();
    }

    private void OnCanvasMouseCaptureChanged(object? sender, EventArgs e)
    {
        // Capture was stolen or released outside the canvas (e.g. alt-tab);
        // stop panning so the next press starts a fresh drag.
        if (!PART_Canvas.Capture)
        {
            _isPanning = false;
        }
    }

    /// <summary>
    /// Applies the signed pan offset by moving the canvas + links host and pushing
    /// the resulting world origin into the grid decorator, minimap, and tree
    /// viewport. The surface behavior only refreshes these on layout cycles, so a
    /// pan needs an explicit push to keep the rulers/grid and minimap viewport
    /// tracking. Content offset shifts the content independently, so the visible
    /// world origin is <c>-panOffset - content</c>.
    /// </summary>
    private void ApplyPan()
    {
        if (IsDisposed) return;

        // The canvas stays fixed over the viewport; the pan is a world-origin
        // translation applied to every node view. Publish it for node views added
        // later, reposition existing ones, then re-measure slot anchors so links
        // track the pan (the layout behavior computes anchors from the slots' screen
        // position, which changed when the nodes moved). Repositioning is synchronous
        // here; the anchor re-sync is deferred to the message loop (ScheduleSync),
        // which coalesces during a drag and runs before the canvas repaints.
        ((SurfaceCanvas)PART_Canvas).PanOffset = _panOffset;
        foreach (Control child in PART_Canvas.Controls)
        {
            if (child is NodeView nodeView)
            {
                nodeView.ApplyPosition();
                WorkflowSlotLayoutBehavior.Refresh(nodeView);
            }
        }

        var content = new Offset(
            _tree?.Layout?.ActualOffset.Horizontal ?? 0,
            _tree?.Layout?.ActualOffset.Vertical ?? 0);

        if (PART_GridDecorator is IWorkflowGridDecorator grid)
        {
            grid.ScrollOffsetX = -_panOffset.X;
            grid.ScrollOffsetY = -_panOffset.Y;
            grid.ContentOffsetX = content.Horizontal;
            grid.ContentOffsetY = content.Vertical;
            PART_GridDecorator.Invalidate();
        }

        if (PART_MinimapOverlay is IWorkflowMinimapOverlay minimap)
        {
            minimap.ScrollOffsetX = -_panOffset.X;
            minimap.ScrollOffsetY = -_panOffset.Y;
            minimap.ContentOffsetX = content.Horizontal;
            minimap.ContentOffsetY = content.Vertical;
            minimap.ViewportWidth = PART_ScrollViewer.ClientSize.Width;
            minimap.ViewportHeight = PART_ScrollViewer.ClientSize.Height;
            PART_MinimapOverlay.Invalidate();
        }

        PART_Canvas.Invalidate();

        // Invalidate only queues; during high-frequency panning WM_PAINT is deferred, so stale
        // node positions and links are not erased in time and leave ghosting. Repaint the canvas
        // (links) and the grid decorator synchronously to keep every frame clean.
        PART_Canvas.Update();
        PART_GridDecorator.Update();

        try
        {
            _tree?.GetHelper().Viewport = new Viewport(
                -_panOffset.X - content.Horizontal,
                -_panOffset.Y - content.Vertical,
                PART_ScrollViewer.ClientSize.Width,
                PART_ScrollViewer.ClientSize.Height);
        }
        catch
        {
            // The tree helper may not support viewport writes on some hosts; ignore.
        }
    }

    private void ScheduleLayout()
    {
        if (_layoutPending || IsDisposed) return;
        _layoutPending = true;

        Action update = () =>
        {
            _layoutPending = false;
            if (IsDisposed) return;
            ApplyCanvasSize();
            WorkflowSurfaceBehavior.Refresh(this);
        };

        if (IsHandleCreated)
        {
            BeginInvoke(update);
        }
        else
        {
            _layoutPending = false;
        }
    }

    private void ApplyCanvasSize()
    {
        // The canvas is docked fill over the viewport and node views are translated
        // by the pan offset, so it always covers the visible region — there is no
        // content-sized canvas to keep in sync with the layout. Just guard against a
        // first-layout zero-size canvas (docking fills it on the next layout pass).
        if (PART_Canvas.Width < 1 || PART_Canvas.Height < 1)
        {
            PART_Canvas.Size = PART_ScrollViewer.ClientSize;
        }
        // Note: AutoScrollMinSize is deliberately NOT set — assigning it calls
        // AdjustScrollbars which re-enables AutoScroll, fighting the manual pan.
    }

    // ── View factories (NodeView/SlotView/LinkView templates) ─────────────────

    private Control CreateNodeView(IWorkflowNodeViewModel node)
    {
        // Slot population moved into NodeView.RebuildSlots so the node owns its
        // layout: a single input/output sit on the card edges (matching the full
        // demo's overlay slot buttons), while enumerated/dynamic outputs render
        // inside the card as labeled rows.
        var view = new NodeView { ViewModel = node };
        // The minimap reads node anchors directly; a node drag changes the anchor
        // without panning, so repaint it as the node moves.
        view.AnchorChanged += () =>
        {
            if (!IsDisposed && PART_MinimapOverlay is not null)
            {
                PART_MinimapOverlay.Invalidate();
            }
        };
        return view;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, Width, Height);
        using var path = RoundRect(bounds, _surfaceCornerRadius);
        using var brush = new SolidBrush(_surfaceBackground);
        using var pen = new Pen(_surfaceBorderBrush, _surfaceBorderThickness);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var d = 2 * r;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ViewPool.SetItemsSource(PART_Canvas, null);
            ViewPool.SetTemplateSelector(PART_Canvas, null);
            if (_linksSubscribedTree is not null)
            {
                _linksSubscribedTree.Links.CollectionChanged -= OnLinksCollectionChanged;
                _linksSubscribedTree = null;
            }

            foreach (var lv in _linkRenderers) lv.Dispose();
            _linkRenderers.Clear();

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnTreeChanged;
                _notifier = null;
            }
        }

        base.Dispose(disposing);
    }
}
