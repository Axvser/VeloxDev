using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// Workflow canvas control.
///
/// Design principles (pure WinForms practice):
///   - Fully self-drawn grid, Bézier links, and slot circles; node cards are added as child controls
///   - Canvas panning: dragging the background updates <see cref="_panOffset"/>, and all child controls shift with it
///   - Node dragging: when the mouse presses on a node card's header area, <see cref="IWorkflowNodeViewModel.MoveCommand"/>
///     is invoked continuously in MouseMove and the card is re-laid out
///   - Slot anchors: computed directly from control screen coordinates before each layout/draw, no separate Behavior needed
///   - Canvas size: computed dynamically from node coordinates; scrollbars appear when content exceeds the window area
/// </summary>
public sealed class WorkflowCanvas : Panel, IWorkflowGridDecorator
{
    // ── Grid parameters ─────────────────────────────────────────────────────────
    private const int GridSpacing = 40;
    private const int MajorFreq = 5;
    private const double Eps = 0.001;
    // Ruler band thickness. Other solutions also use 28px, but users reported WinForms
    // looked too small visually, so it was enlarged to 36px.
    private const int RulerThickness = 36;

    // ── State ──────────────────────────────────────────────────────────────────
    private WorkflowDemoSession? _session;
    private readonly Dictionary<IWorkflowNodeViewModel, WorkflowNodeCard> _cards = [];

    // Link renderer: template LinkView (VirtualLink + all real links). They are not child
    // controls; they reuse the template LinkView geometry (Render), drawn uniformly by the
    // canvas OnPaint — this avoids overlapping full-size transparent sibling windows in
    // WinForms being clipped by WS_CLIPSIBLINGS (only the topmost one would be drawn).
    private readonly List<Views.LinkView> _linkRenderers = [];

    // Panning
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;
    // Pixel position of the world-coordinate origin in the client area. It defaults to the
    // content area's top-left corner (RulerThickness, RulerThickness), matching the other
    // solutions: content starts to the right/below the ruler band, and tick "0" appears at
    // the content boundary rather than at the top-left corner junction.
    private Point _panOffset = new(RulerThickness, RulerThickness);

    // Minimap: hosted in splitContainer.Panel2 (non-scrolling area), synced manually via SyncMinimap.
    // SetMinimapOverlayName is not used — Refresh's ResolveScrollOffset only returns
    // -AutoScrollPosition and does not include _panOffset; manual sync is deterministic.
    private Control? _minimap;

    // Floating translucent ruler overlay (owned popup), created once the canvas is parented.
    // The ruler bands/ticks/labels moved out of OnPaintBackground into this WS_EX_LAYERED
    // window so they composite ABOVE the opaque node-card children (cards dimmed under the
    // band, matching the other frameworks). See EnsureRulerOverlay / SyncRulerOverlay.
    private RulerOverlayForm? _rulerOverlay;

    // ── IWorkflowGridDecorator ──────────────────────────────────────────────
    // WorkflowSurfaceBehavior.Refresh pushes scroll/content offsets here on every refresh cycle
    // for external decorators or diagnostics; the canvas itself still draws using the internal _panOffset.
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

    // ── Public properties ─────────────────────────────────────────────────────────────

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowDemoSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            DetachSession(_session);
            _session = value;
            AttachSession(value);
        }
    }

    /// <summary>
    /// Optional minimap overlay. The host should place it in a non-scrolling area (such as
    /// splitContainer.Panel2) and call <c>BringToFront</c> so it stays fixed while the canvas
    /// pans/scrolls. The canvas syncs the visible region and responds to the minimap's viewport
    /// drag requests.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? MinimapOverlay
    {
        get => _minimap;
        set
        {
            if (ReferenceEquals(_minimap, value)) return;
            if (_minimap is not null)
            {
                if (_minimap is Views.IWorkflowMinimapScrollSource oldSrc)
                {
                    oldSrc.ViewportScrollRequested -= OnMinimapScrollRequested;
                }

                if (_minimap is IWorkflowMinimapOverlay oldMm)
                {
                    oldMm.WorkflowTree = null;
                }
            }

            _minimap = value;
            if (value is not null)
            {
                if (value is Views.IWorkflowMinimapScrollSource src)
                {
                    src.ViewportScrollRequested += OnMinimapScrollRequested;
                }

                if (value is IWorkflowMinimapOverlay mm)
                {
                    mm.WorkflowTree = _session?.Tree;
                }

                SyncMinimap();
            }
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────────────────
    public WorkflowCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 30, 30); // #1E1E1E template grid decorator default background
        AutoScroll = true;

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetZoomEnabled(this, true);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

    }

    // ── Session lifecycle ───────────────────────────────────────────────────────
    private void AttachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, s.Tree);
        s.Tree.Nodes.CollectionChanged += OnNodesChanged;
        s.Tree.Links.CollectionChanged += OnLinksChanged;
        s.Controller.PropertyChanged += OnControllerPropertyChanged;

        foreach (var node in s.Tree.Nodes) AddCard(node);
        UpdateCanvasMinSize();

        // Link renderer: VirtualLink (first) + all real links → drawn uniformly by the canvas OnPaint.
        AttachLinksPool();
        SyncMinimap();

        // Delayed sync: wait for WinForms to complete the first layout before computing SlotView screen coordinates
        if (IsHandleCreated)
            BeginInvoke(InitialSync);
        else
            HandleCreated += OnHandleCreatedForInitialSync;
    }

    /// <summary>
    /// Builds the canvas link renderers (VirtualLink + all real links). Renderers reuse the template
    /// LinkView but are not added to the control tree; the canvas OnPaint calls their
    /// <see cref="Views.LinkView.Render"/> uniformly. Link add/remove rebuilds them via <see cref="OnLinksChanged"/>.
    /// </summary>
    private void AttachLinksPool()
    {
        RebuildLinkRenderers();
    }

    private void RebuildLinkRenderers()
    {
        foreach (var lv in _linkRenderers) lv.Dispose();
        _linkRenderers.Clear();
        if (_session is null) return;

        _linkRenderers.Add(CreateLinkRenderer(_session.Tree.VirtualLink));
        foreach (var link in _session.Tree.Links)
        {
            _linkRenderers.Add(CreateLinkRenderer(link));
        }
    }

    private static Views.LinkView CreateLinkRenderer(IWorkflowLinkViewModel link)
    {
        var view = new Views.LinkView();
        view.ViewModel = link;
        return view;
    }

    private void OnHandleCreatedForInitialSync(object? sender, EventArgs e)
    {
        HandleCreated -= OnHandleCreatedForInitialSync;
        BeginInvoke(InitialSync);
    }

    private void InitialSync()
    {
        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
    }

    // ── Minimap sync ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Pushes the current visible world region to the minimap. Node cards are positioned by
    /// anchor + panOffset + scroll (<see cref="NodeBounds"/>); node.Anchor is already the final
    /// screen-world coordinate — this self-drawn canvas does not apply an additional content
    /// offset pan, so the minimap ContentOffset is always 0, and
    /// ScrollOffset = -(panOffset + scroll) exactly equals the world coordinate at the left screen edge.
    /// </summary>
    private void SyncMinimap()
    {
        if (_minimap is not IWorkflowMinimapOverlay m) return;

        var scroll = AutoScrollPosition;
        m.ScrollOffsetX = -(_panOffset.X + scroll.X);
        m.ScrollOffsetY = -(_panOffset.Y + scroll.Y);
        m.ContentOffsetX = 0;
        m.ContentOffsetY = 0;
        m.ViewportWidth = ClientSize.Width;
        m.ViewportHeight = ClientSize.Height;
        m.WorkflowTree = _session?.Tree;
        _minimap.Invalidate();
    }

    /// <summary>
    /// Minimap drag requests scrolling: the minimap expresses an absolute world visible region,
    /// so first reset AutoScroll to zero (after wheel scrolling AutoScrollPosition is non-zero,
    /// which conflicts with panning, and it would be re-clamped in RelayoutAllCards →
    /// UpdateCanvasMinSize, causing the viewport block to bounce back and look undraggable),
    /// then invert ScrollOffset = -panOffset to get panOffset = -sx,
    /// and finally re-layout the cards (which also re-syncs the minimap).
    /// </summary>
    private void OnMinimapScrollRequested(double sx, double sy)
    {
        // Compensate the pan for the current AutoScrollPosition instead of resetting it: resetting
        // fires a (deferred) Scroll event that re-runs SyncMinimap from the pre-pan scroll and
        // overwrites the minimap's ScrollOffset back to its old spot — the "stuck until you drag"
        // symptom. With the compensation the total pan (panOffset + AutoScrollPosition) equals the
        // requested ScrollOffset, so the block follows the very first press.
        var scroll = AutoScrollPosition;
        _panOffset = new Point(
            (int)Math.Round(-sx - scroll.X),
            (int)Math.Round(-sy - scroll.Y));
        RelayoutAllCards();

        // RelayoutAllCards → SyncMinimap can still read the stale AutoScrollPosition, so force the
        // minimap to the requested scroll now for immediate feedback; it converges on the pan.
        if (_minimap is IWorkflowMinimapOverlay m)
        {
            m.ScrollOffsetX = sx;
            m.ScrollOffsetY = sy;
            m.ViewportWidth = ClientSize.Width;
            m.ViewportHeight = ClientSize.Height;
            _minimap.Invalidate();
        }

        // While dragging on the minimap, the mouse capture is held by the minimap, not the canvas —
        // the synchronous redraw branch in Refresh's host.Capture never fires, only async Invalidate.
        // During high-frequency dragging WM_PAINT is continually deferred by WM_MOUSEMOVE, so old node
        // positions and old links are not erased in time, leaving ghost trails; this synchronously
        // redraws the canvas (grid/rulers/links), matching Trimmed's ApplyPan Update.
        Update();
    }

    // ── Floating ruler overlay ───────────────────────────────────────────────────
    /// <summary>
    /// Creates the floating ruler overlay once the canvas is parented (FindForm needs the
    /// top-level window). Called from OnHandleCreated and lazily from SyncRulerOverlay callers,
    /// so it appears even if a tree is attached before the control is added to a form.
    /// </summary>
    private void EnsureRulerOverlay()
    {
        if (_rulerOverlay is not null || IsDisposed || !IsHandleCreated) return;
        var owner = FindForm();
        if (owner is null || owner.IsDisposed) return;

        _rulerOverlay = new RulerOverlayForm { RulerThickness = RulerThickness };
        _rulerOverlay.Show(owner);                   // owned popup: above owner, follows moves
        SyncRulerOverlay();

        Resize += OnRulerOverlayHostChanged;
        LocationChanged += OnRulerOverlayHostChanged;
        owner.Move += OnRulerOverlayOwnerMoved;      // owned window auto-moves; re-ULW at the new rect
        owner.Resize += OnRulerOverlayHostChanged;   // docked canvas resizes with the window
    }

    /// <summary>
    /// Keeps the overlay sized/positioned over this canvas's client area and repaints it with the
    /// current world origin. The overlay reads the same origin as the grid
    /// (<c>ScrollOffset = -(panOffset + scroll)</c>, content offset 0 — matching DrawGrid), so its
    /// ticks stay aligned with the grid lines while content scrolls under the viewport-fixed band.
    /// </summary>
    private void SyncRulerOverlay()
    {
        if (_rulerOverlay is null || _rulerOverlay.IsDisposed) return;
        if (Width < 1 || Height < 1) return;

        var scroll = AutoScrollPosition;
        _rulerOverlay.Location = PointToScreen(Point.Empty);
        _rulerOverlay.Size = ClientSize;
        _rulerOverlay.ScrollOffsetX = -(_panOffset.X + scroll.X);
        _rulerOverlay.ScrollOffsetY = -(_panOffset.Y + scroll.Y);
        _rulerOverlay.ContentOffsetX = 0;
        _rulerOverlay.ContentOffsetY = 0;
        _rulerOverlay.RefreshSurface();
    }

    private void OnRulerOverlayHostChanged(object? sender, EventArgs e) => SyncRulerOverlay();
    private void OnRulerOverlayOwnerMoved(object? sender, EventArgs e) => SyncRulerOverlay();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnsureRulerOverlay();
    }

    private void DetachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, null);
        HandleCreated -= OnHandleCreatedForInitialSync;
        s.Tree.Nodes.CollectionChanged -= OnNodesChanged;
        s.Tree.Links.CollectionChanged -= OnLinksChanged;
        s.Controller.PropertyChanged -= OnControllerPropertyChanged;

        foreach (var lv in _linkRenderers) lv.Dispose();
        _linkRenderers.Clear();

        foreach (var card in _cards.Values)
        {
            Controls.Remove(card);
            card.Dispose();
        }

        _cards.Clear();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    // ── Node collection changes ──────────────────────────────────────────────────────────
    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnNodesChanged(sender, e))); return; }

        if (e.OldItems is not null)
        {
            foreach (var n in e.OldItems.OfType<IWorkflowNodeViewModel>())
                RemoveCard(n);
        }

        if (e.NewItems is not null)
        {
            foreach (var n in e.NewItems.OfType<IWorkflowNodeViewModel>())
                AddCard(n);
        }

        SyncAllSlotAnchors();
        UpdateCanvasMinSize();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnLinksChanged(sender, e))); return; }
        // Rebuild renderers and re-sync all slot anchors when links are added/removed to keep endpoint coordinates correct
        RebuildLinkRenderers();
        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ControllerViewModel.IsActive)) return;
        if (InvokeRequired) { BeginInvoke(new Action(RefreshAllCards)); return; }
        RefreshAllCards();
    }

    // ── Node card management ──────────────────────────────────────────────────────────
    private void AddCard(IWorkflowNodeViewModel node)
    {
        if (_cards.ContainsKey(node)) return;

        var card = new WorkflowNodeCard();
        card.Bind(node);

        // Subscribe to node position/size changes
        if (node is INotifyPropertyChanged n) n.PropertyChanged += OnNodePropertyChanged;

        _cards[node] = card;
        Controls.Add(card);
        LayoutCard(node, card);
        card.BringToFront();
    }

    private void RemoveCard(IWorkflowNodeViewModel node)
    {
        if (!_cards.TryGetValue(node, out var card)) return;
        if (node is INotifyPropertyChanged n) n.PropertyChanged -= OnNodePropertyChanged;
        card.Unbind();
        Controls.Remove(card);
        card.Dispose();
        _cards.Remove(node);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new PropertyChangedEventHandler(OnNodePropertyChanged), sender, e); return; }
        if (sender is not IWorkflowNodeViewModel node || !_cards.TryGetValue(node, out var card)) return;

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            LayoutCard(node, card);
            SyncAllSlotAnchors();
            UpdateCanvasMinSize();
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            SyncMinimap();
            // One canvas refresh per wheel notch (coalesced across the 2×N Anchor/Size events) fills
            // the region a moved card vacated — the self-drawn canvas redraws the grid + links there,
            // erasing the pre-zoom ghost. Card subtrees repaint on their own child-window cycle.
            ScheduleZoomRedraw();
        }
        else
        {
            card.Refresh(node);
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            SyncMinimap();
        }
    }

    private bool _zoomCanvasRefreshPending;

    /// <summary>
    /// Coalesces the 2×N Anchor/Size events of one wheel notch into a single canvas refresh, so the
    /// pre-zoom card positions get erased on the self-drawn canvas (the grid + links fill the region a
    /// moved child window vacates — WinForms does not invalidate it). Synchronous like the Trimmed
    /// demo's ApplyPan Update: one sync redraw per notch keeps the zoom ghost-free, and coalescing the
    /// burst into one means the UI thread blocks once per notch, not 2×N times.
    /// </summary>
    private void ScheduleZoomRedraw()
    {
        if (_zoomCanvasRefreshPending) return;
        _zoomCanvasRefreshPending = true;

        void RefreshCanvas()
        {
            _zoomCanvasRefreshPending = false;
            if (IsDisposed || !IsHandleCreated) return;
            Invalidate();
            Update();
        }

        BeginInvoke(RefreshCanvas);
    }

    private void RefreshAllCards()
    {
        foreach (var (node, card) in _cards)
        {
            card.RefreshVisual();
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
    }

    /// <summary>Positions a node card at the client location corresponding to its canvas coordinate.</summary>
    private void LayoutCard(IWorkflowNodeViewModel node, WorkflowNodeCard card)
    {
        card.Bounds = NodeBounds(node);
    }

    private Rectangle NodeBounds(IWorkflowNodeViewModel node)
    {
        var scroll = AutoScrollPosition;
        return new Rectangle(
            (int)Math.Round(node.Anchor.Horizontal + _panOffset.X + scroll.X),
            (int)Math.Round(node.Anchor.Vertical + _panOffset.Y + scroll.Y),
            (int)Math.Round(node.Size.Width),
            (int)Math.Round(node.Size.Height));
    }

    private void RelayoutAllCards()
    {
        foreach (var (node, card) in _cards)
        {
            LayoutCard(node, card);
        }

        UpdateCanvasMinSize();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
        SyncRulerOverlay();
    }

    // ── Slot anchor sync ────────────────────────────────────────────────────────
    /// <summary>
    /// Computes the live world coordinates of all SlotViews, writes them to
    /// IWorkflowSlotViewModel.Anchor, and returns a slot→world-coordinate snapshot
    /// dictionary for drawing.
    /// </summary>
    private Dictionary<IWorkflowSlotViewModel, PointF> BuildSlotWorldMap()
    {
        var scroll = AutoScrollPosition;
        var map = new Dictionary<IWorkflowSlotViewModel, PointF>(ReferenceEqualityComparer.Instance);
        foreach (var (_, card) in _cards)
            CollectNodeSlotPositions(card, map, scroll, _panOffset);
        return map;
    }

    private void SyncAllSlotAnchors()
    {
        var scroll = AutoScrollPosition;
        foreach (var (_, card) in _cards)
        {
            var map = new Dictionary<IWorkflowSlotViewModel, PointF>(ReferenceEqualityComparer.Instance);
            CollectNodeSlotPositions(card, map, scroll, _panOffset);
            foreach (var (slot, pt) in map)
            {
                slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);
            }
        }
    }

    private static void CollectNodeSlotPositions(
        WorkflowNodeCard card,
        Dictionary<IWorkflowSlotViewModel, PointF> map,
        Point scroll,
        Point panOffset)
    {
        // card.Left = node.Anchor.Horizontal + panOffset.X + scroll.X
        // World coordinate (used after TranslateTransform(origin)) = node.Anchor.Horizontal + cx
        //           = card.Left - scroll.X - panOffset.X + cx
        var cardOriginX = card.Left - scroll.X - panOffset.X;
        var cardOriginY = card.Top - scroll.Y - panOffset.Y;

        CollectSlotButton(card.InputSlotButton, card, cardOriginX, cardOriginY, map);
        CollectSlotButton(card.OutputSlotButton, card, cardOriginX, cardOriginY, map);
        foreach (var btn in EnumerateSlotButtons(card))
            CollectSlotButton(btn, card, cardOriginX, cardOriginY, map);
    }

    private static void CollectSlotButton(
        Views.SlotView? btn,
        WorkflowNodeCard card,
        float cardOriginX,
        float cardOriginY,
        Dictionary<IWorkflowSlotViewModel, PointF> map)
    {
        if (btn is null || btn.ViewModel is null || !btn.Visible) return;
        if (map.ContainsKey(btn.ViewModel)) return;

        // Walk from btn up to card, accumulating each level's Left/Top to get btn's center relative to the card
        var cx = btn.Left + btn.Width / 2;
        var cy = btn.Top + btn.Height / 2;
        var cur = btn.Parent;
        while (cur is not null && !ReferenceEquals(cur, card))
        {
            cx += cur.Left;
            cy += cur.Top;
            cur = cur.Parent;
        }

        map[btn.ViewModel] = new PointF(cardOriginX + cx, cardOriginY + cy);
    }

    private static IEnumerable<Views.SlotView> EnumerateSlotButtons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Views.SlotView sb) yield return sb;
            foreach (var nested in EnumerateSlotButtons(child))
                yield return nested;
        }
    }

    // ── Mouse events (panning, node dragging, links) ─────────────────────────────────────
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_session?.Tree.VirtualLink.IsVisible == true)
        {
            if (_session.Tree.ResetVirtualLinkCommand.CanExecute(null))
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            return;
        }

        // Child controls inside a card do not fire the canvas MouseDown; this is only reached
        // when clicking the blank area → canvas panning
        _isPanning = true;
        _panPressScreen = Cursor.Position;
        _panOffsetAtPress = _panOffset;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            var cur = Cursor.Position;
            _panOffset = new Point(
                _panOffsetAtPress.X + cur.X - _panPressScreen.X,
                _panOffsetAtPress.Y + cur.Y - _panPressScreen.Y);
            RelayoutAllCards();
            return;
        }

        // Mouse tracking in link mode is handled by WorkflowSlotConnectionBehavior; no need to repeat it here
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning)
        {
            _isPanning = false;
            Capture = false;
            return;
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            _isPanning = false;
            // Link state is managed separately by WorkflowSlotConnectionBehavior; not cleared here
        }
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────
    // The grid and rulers are drawn in OnPaintBackground (not OnPaint) as the bottom layer; cards
    // are child controls drawn on top, and links are drawn on top via the template LinkView geometry in OnPaint.
    // The floating translucent ruler bands, ticks, and labels are NOT drawn here anymore: they live in
    // the RulerOverlayForm (a WS_EX_LAYERED owned popup composited above the canvas at per-pixel alpha),
    // so cards scrolling under the band are genuinely dimmed instead of covering it — the one WinForms
    // mechanism that can match the other frameworks' floating translucent rulers.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        DrawGrid(g, origin);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        if (_session is null) return;

        // Compute a live world-coordinate snapshot of all slots and write back slot.Anchor (for link endpoints and hit-testing)
        var slotMap = BuildSlotWorldMap();
        foreach (var (slot, pt) in slotMap)
            slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);

        // Links: the template LinkView geometry is drawn uniformly on the canvas (using world
        // coordinates after TranslateTransform(origin)). Renderers are not added to the control
        // tree, avoiding WinForms overlapping full-size sibling windows being clipped by
        // WS_CLIPSIBLINGS; slots are drawn by the template SlotView inside the cards.
        var linkState = g.Save();
        g.TranslateTransform(origin.X, origin.Y);
        foreach (var lv in _linkRenderers)
        {
            lv.Render(g);
        }
        g.Restore(linkState);

        // Nodes without a corresponding card: draw a placeholder rectangle
        foreach (var node in _session.Tree.Nodes)
        {
            if (!_cards.ContainsKey(node))
                DrawNodeFallback(g, node, origin);
        }
    }

    private static void DrawNodeFallback(Graphics g, IWorkflowNodeViewModel node, PointF origin)
    {
        if (node.Size.Width <= 0 || node.Size.Height <= 0) return;
        var bounds = new RectangleF(
            (float)(node.Anchor.Horizontal + origin.X),
            (float)(node.Anchor.Vertical + origin.Y),
            (float)node.Size.Width,
            (float)node.Size.Height);

        using var body = new SolidBrush(Color.FromArgb(37, 37, 37));
        using var border = new Pen(Color.FromArgb(75, 85, 99), 1.5f);
        using var path = RoundRectF(bounds, 18f);
        g.FillPath(body, path);
        g.DrawPath(border, path);
    }

    // ── Grid drawing ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Draws the full-area grid (extending under the ruler bands), panning with the content.
    /// Consistent with the other solutions (WPF/Avalonia/MAUI/WinUI templates, Blazor): the grid
    /// belongs to the content and the rulers float. The floating translucent ruler bands live in
    /// the <see cref="RulerOverlayForm"/> (a WS_EX_LAYERED owned popup composited above the canvas),
    /// so grid lines and node cards scroll under the band and stay visibly dimmed.
    /// </summary>
    private void DrawGrid(Graphics g, PointF origin)
    {
        // Colors match the WinForms grid decorator template defaults (#2A2D2E / #3A3D40 / axis #4D4D4D).
        using var minor = new Pen(Color.FromArgb(42, 45, 46), 1f);
        using var major = new Pen(Color.FromArgb(58, 61, 64), 1f);
        using var axis = new Pen(Color.FromArgb(77, 77, 77), 1.2f);

        var cw = ClientSize.Width;
        var ch = ClientSize.Height;

        // Convert world coordinates to client and draw the grid full-area (no clip, no
        // content-area guard) so grid lines extend under the translucent ruler bands.
        var startX = Math.Floor((-origin.X) / GridSpacing) * GridSpacing;
        for (var x = startX; x <= -origin.X + cw + GridSpacing; x += GridSpacing)
        {
            var sx = (float)(x + origin.X);
            var pen = NearZero(x) ? axis : IsMajor(x) ? major : minor;
            g.DrawLine(pen, sx, 0, sx, ch);
        }

        var startY = Math.Floor((-origin.Y) / GridSpacing) * GridSpacing;
        for (var y = startY; y <= -origin.Y + ch + GridSpacing; y += GridSpacing)
        {
            var sy = (float)(y + origin.Y);
            var pen = NearZero(y) ? axis : IsMajor(y) ? major : minor;
            g.DrawLine(pen, 0, sy, cw, sy);
        }
    }

    // ── Hit testing ──────────────────────────────────────────────────────────────
    private IWorkflowSlotViewModel? HitTestSlot(Anchor worldAnchor, IWorkflowSlotViewModel? exclude = null)
    {
        if (_session is null) return null;
        const double r2 = 18.0 * 18.0;
        foreach (var slot in EnumerateAllSlots())
        {
            if (ReferenceEquals(slot, exclude)) continue;
            var dx = slot.Anchor.Horizontal - worldAnchor.Horizontal;
            var dy = slot.Anchor.Vertical - worldAnchor.Vertical;
            if (dx * dx + dy * dy <= r2) return slot;
        }

        return null;
    }

    private IEnumerable<IWorkflowSlotViewModel> EnumerateAllSlots()
    {
        if (_session is null) yield break;
        foreach (var node in _session.Tree.Nodes)
        foreach (var slot in EnumerateNodeSlots(node))
            yield return slot;
    }

    private static IEnumerable<IWorkflowSlotViewModel> EnumerateNodeSlots(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case BoolSelectorNodeViewModel b:
                if (b.InputSlot is not null) yield return b.InputSlot;
                if (b.TrueSlot is not null) yield return b.TrueSlot;
                if (b.FalseSlot is not null) yield return b.FalseSlot;
                break;
            case EnumSelectorNodeViewModel e:
                if (e.InputSlot is not null) yield return e.InputSlot;
                if (e.OutputSlots is not null)
                {
                    foreach (var s in e.OutputSlots.Cast<IWorkflowSlotViewModel>())
                        yield return s;
                }
                break;
            case NodeViewModel nv:
                if (nv.InputSlot is not null) yield return nv.InputSlot;
                if (nv.OutputSlot is not null) yield return nv.OutputSlot;
                break;
            case ControllerViewModel cv:
                if (cv.OutputSlot is not null) yield return cv.OutputSlot;
                break;
        }
    }

    // ── Coordinate conversion ──────────────────────────────────────────────────────────────
    private Anchor ClientToWorld(Point clientPt)
    {
        var scroll = AutoScrollPosition;
        return new Anchor(
            clientPt.X - _panOffset.X - scroll.X,
            clientPt.Y - _panOffset.Y - scroll.Y,
            0);
    }

    // ── Canvas size ──────────────────────────────────────────────────────────────
    private void UpdateCanvasMinSize()
    {
        if (_session is null || _session.Tree.Nodes.Count == 0)
        {
            AutoScrollMinSize = new System.Drawing.Size(1280, 760);
            return;
        }

        // Node Anchor/Size getters collapse toward the world origin by 1/scale, so the scroll
        // extent must come from the world bounds (collapsed × scale). Using the collapsed bounds
        // would shrink AutoScrollMinSize on zoom, re-clamp AutoScrollPosition, and move the axis.
        var scale = _session.Tree.Layout.Scale.Horizontal;
        var maxX = _session.Tree.Nodes.Max(n => (n.Anchor.Horizontal + n.Size.Width) * scale);
        var maxY = _session.Tree.Nodes.Max(n => (n.Anchor.Vertical + n.Size.Height) * scale);
        var w = (int)Math.Ceiling(maxX + _panOffset.X + 120);
        var h = (int)Math.Ceiling(maxY + _panOffset.Y + 120);
        AutoScrollMinSize = new System.Drawing.Size(Math.Max(1280, w), Math.Max(760, h));
    }

    // Prevent WinForms from scrolling child controls into view (would interfere with panning logic)
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // Link renderers are not part of the control tree; panning/scrolling is handled uniformly
        // by the canvas OnPaint origin transform.
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
        SyncRulerOverlay();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            DetachSession(_session);

            if (_rulerOverlay is not null)
            {
                _rulerOverlay.Dispose();
                _rulerOverlay = null;
            }
        }

        base.Dispose(disposing);
    }

    // ── Static helpers ──────────────────────────────────────────────────────────────
    private static bool IsMajor(double v)
    {
        var major = GridSpacing * MajorFreq;
        var norm = ((v % major) + major) % major;
        return norm < Eps || Math.Abs(norm - major) < Eps;
    }

    private static bool NearZero(double v) => Math.Abs(v) < Eps;

    /// <summary>Tick value format, consistent with the template FormatGridValue (K for thousands, M for millions).</summary>
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

    private static GraphicsPath RoundRectF(RectangleF r, float radius)
    {
        var d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Per-pixel alpha ruler-band overlay. WinForms children always paint above the parent's
    /// OnPaintBackground, so a translucent band drawn there can never dim the opaque node cards
    /// scrolling under it — cards cover the band. The one mechanism that CAN is a WS_EX_LAYERED
    /// window composited over the viewport: this is an owned, top-level, hit-transparent popup whose
    /// surface is rendered via UpdateLayeredWindow at per-pixel alpha, so the band genuinely
    /// composites ABOVE the node cards (cards are dimmed under the band, matching WPF/Avalonia/
    /// WinUI/MAUI/Razor). Creating a layered CHILD window fails with ERROR_NOT_SUPPORTED on some
    /// systems, hence the owned popup; owned popups also follow the form when it moves and stay above
    /// it for free. WS_EX_TOOLWINDOW keeps it out of the taskbar and alt-tab; WM_NCHITTEST →
    /// HTTRANSPARENT keeps panning and node drags working under the band.
    /// </summary>
    private sealed class RulerOverlayForm : Form
    {
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;
        private const int HtTransparent = -1;
        private const int WmNcHitTest = 0x0084;
        private const uint UlwAlpha = 2;

        private const int GridSpacing = 40;
        private const int MajorFreq = 5;
        private const double Eps = 0.001;

        // Same palette as the surface/grid. Alpha 0x70 (WinForms deviation): the band composites
        // over cards AND grid, so at the other frameworks' 0xC8 the grid under the band would read
        // only ~3.5 lum deep (imperceptible); 0x70 keeps the tint but lets the grid pass visibly
        // under the band.
        private readonly Color _rulerBackground = Color.FromArgb(112, 37, 37, 38);
        private readonly Color _labelColor = Color.FromArgb(136, 136, 136);
        private readonly Color _tickColor = Color.FromArgb(85, 85, 85);
        private readonly Color _axisColor = Color.FromArgb(77, 77, 77);
        private readonly Color _dividerColor = Color.FromArgb(58, 61, 64);
        private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);

        private Bitmap? _surface;

        public RulerOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExNoActivate | WsExToolWindow | WsExTransparent;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest)
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }

            base.WndProc(ref m);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double RulerThickness { get; set; } = 36;

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

        /// <summary>
        /// Repaints the layered surface. The window's own screen rect is the physical bitmap size;
        /// ticks/labels are drawn in logical (viewport client) space and scaled to fill, so a
        /// system-DPI-aware host (1:1 here) and a virtualized one both stay aligned with the grid
        /// drawn by the canvas.
        /// </summary>
        public void RefreshSurface()
        {
            if (IsDisposed || !IsHandleCreated || Width < 1 || Height < 1) return;

            GetWindowRect(Handle, out var rect);
            var pw = Math.Max(1, rect.Right - rect.Left);
            var ph = Math.Max(1, rect.Bottom - rect.Top);

            if (_surface is null || _surface.Width != pw || _surface.Height != ph)
            {
                _surface?.Dispose();
                _surface = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
            }

            using (var g = Graphics.FromImage(_surface))
            {
                g.Clear(Color.Transparent);
                g.ScaleTransform(
                    pw / (float)Math.Max(1, Width),
                    ph / (float)Math.Max(1, Height));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawBands(g, Width, Height);
            }

            UpdateLayeredSurface(rect, pw, ph);
        }

        private void DrawBands(Graphics g, int cw, int ch)
        {
            var ruler = (float)RulerThickness;
            using var rulerBrush = new SolidBrush(_rulerBackground);
            using var dividerPen = new Pen(_dividerColor, 1f);
            using var tickPen = new Pen(_tickColor, 1f);
            using var axisPen = new Pen(_axisColor, 1f);
            using var labelBrush = new SolidBrush(_labelColor);
            using var format = new StringFormat(StringFormat.GenericTypographic);

            // Translucent bands — one bitmap, per-pixel alpha over everything beneath
            // (the canvas grid AND the opaque node cards).
            g.FillRectangle(rulerBrush, 0, 0, cw, ruler);
            g.FillRectangle(rulerBrush, 0, 0, ruler, ch);

            // Dividers at full opacity.
            g.DrawLine(dividerPen, ruler, 0, ruler, ch);
            g.DrawLine(dividerPen, 0, ruler, cw, ruler);

            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorFreq);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
            var worldRight = worldLeft + cw;
            var worldBottom = worldTop + ch;

            // Top ruler. Ticks share the grid's x = value - worldLeft (the canvas draws the grid at
            // the same x), so ticks stay aligned with grid lines while the content scrolls under the
            // viewport-fixed band. Skip x < ruler so the corner junction and left band stay clean.
            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
                if (x < ruler)
                {
                    continue;
                }

                var isMajor = IsMajor(value);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = NearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, x, ruler, x, (float)(ruler - tickLength));

                if (isMajor)
                {
                    g.DrawString(FormatGridValue(value), _labelFont, labelBrush, x + 3, 2, format);
                }
            }

            // Left ruler.
            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
                if (y < ruler)
                {
                    continue;
                }

                var isMajor = IsMajor(value);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = NearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, ruler, y, (float)(ruler - tickLength), y);

                if (isMajor)
                {
                    g.DrawString(FormatGridValue(value), _labelFont, labelBrush, 3, y + 2, format);
                }
            }
        }

        private void UpdateLayeredSurface(RECT rect, int pw, int ph)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hbitmap = _surface!.GetHbitmap(Color.FromArgb(0));
            var old = SelectObject(memDc, hbitmap);
            try
            {
                var ptDst = new POINT { X = rect.Left, Y = rect.Top };
                var size = new SIZE { cx = pw, cy = ph };
                var ptSrc = new POINT();
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,               // AC_SRC_OVER
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1,           // AC_SRC_ALPHA — bitmap must be premultiplied
                };
                UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, UlwAlpha);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hbitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);
    }
}
