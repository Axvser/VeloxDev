// VeloxDev customization: A workflow surface that composes the GridDecorator,
// ScrollViewer, Canvas, Minimap, and ViewPool into a single control. Bind
// IWorkflowTreeViewModel via the ViewModel property (or Tag/DataContext) to
// start rendering. Generate the NodeView/SlotView/LinkView templates and wire
// their factories below when you rename the generated types.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>Absolute-positioned canvas that hosts pooled node views.</summary>
    public Panel PART_Canvas { get; }

    /// <summary>Layer below the canvas hosting pooled link views.</summary>
    public Panel PART_LinksHost { get; }

    /// <summary>Grid + ruler decorator that implements <see cref="IWorkflowGridDecorator"/>.</summary>
    public Control PART_GridDecorator { get; }

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

    // Keeps the links pool in sync with tree.Links + VirtualLink without mutating
    // the tree's own collections.
    private ObservableCollection<IWorkflowViewModel>? _linkItems;
    private NotifyCollectionChangedEventHandler? _linksChangedHandler;
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

        // Grid decorator: the base layer, fully fills the surface.
        PART_GridDecorator = new GridDecorator
        {
            Dock = DockStyle.Fill,
            Name = "PART_GridDecorator",
        };

        // Scroll viewer: pans via AutoScroll; canvas + links host are virtualized inside.
        PART_ScrollViewer = new ScrollableControl
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Name = "PART_ScrollViewer",
        };

        // Links host: sits below the canvas in z-order so links paint behind nodes.
        PART_LinksHost = new Panel
        {
            Location = Point.Empty,
            BackColor = Color.Transparent,
            Name = "PART_LinksHost",
            Size = new Size(1920, 1080),
        };

        // Canvas: absolute-positioned host for pooled node views.
        PART_Canvas = new Panel
        {
            Location = Point.Empty,
            BackColor = Color.Transparent,
            Name = "PART_Canvas",
            Size = new Size(1920, 1080),
        };

        // Links host first (bottom), canvas second (top). Both are children of the
        // scroll viewer so they pan together; the canvas is above the links host.
        PART_ScrollViewer.Controls.Add(PART_LinksHost);
        PART_ScrollViewer.Controls.Add(PART_Canvas);
        PART_GridDecorator.Controls.Add(PART_ScrollViewer);
        Controls.Add(PART_GridDecorator);

        // View pool: nodes go into the canvas, links into the links host. This keeps
        // links behind nodes regardless of the pool's BringToFront behavior.
        _selector = new TemplateSelector();
        _selector.NodeViewFactory = CreateNodeView;
        _selector.LinkViewFactory = CreateLinkView;

        WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_GridDecorator");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(this, "PART_GridDecorator");

        HandleCreated += OnHandleCreated;
        Resize += OnSurfaceResize;
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
                Controls.Remove(PART_MinimapOverlay);
                PART_MinimapOverlay = null;
            }

            if (value is null) return;

            PART_MinimapOverlay = value;
            value.Visible = true;
            value.Name = "PART_MinimapOverlay";
            Controls.Add(value);
            value.BringToFront();
            WorkflowSurfaceBehavior.SetMinimapOverlayName(this, "PART_MinimapOverlay");
        }
    }

    private void AttachTree()
    {
        WorkflowSurfaceBehavior.SetWorkflowTree(this, _tree);

        // Build a reactive links collection: VirtualLink (first) + all real links.
        _linkItems = _tree is null ? null : CreateLinkItems(_tree);

        // Reconfigure the pools (detaches previous managers, then re-attaches).
        ViewPool.SetItemsSource(PART_Canvas, _tree?.Nodes);
        ViewPool.SetTemplateSelector(PART_Canvas, _selector);
        ViewPool.SetItemsSource(PART_LinksHost, _linkItems);
        ViewPool.SetTemplateSelector(PART_LinksHost, _selector);

        if (_tree is not null && PART_MinimapOverlay is not null)
        {
            WorkflowSurfaceBehavior.Refresh(this);
        }
    }

    private ObservableCollection<IWorkflowViewModel> CreateLinkItems(IWorkflowTreeViewModel tree)
    {
        // Unsubscribe any previous tree's handler.
        if (_linksSubscribedTree is not null && _linksChangedHandler is not null)
        {
            _linksSubscribedTree.Links.CollectionChanged -= _linksChangedHandler;
        }

        _linkItems = new ObservableCollection<IWorkflowViewModel> { tree.VirtualLink };
        foreach (var link in tree.Links)
        {
            _linkItems.Add(link);
        }

        _linksSubscribedTree = tree;
        _linksChangedHandler = OnLinksCollectionChanged;
        tree.Links.CollectionChanged += _linksChangedHandler;
        return _linkItems;
    }

    private void OnLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new NotifyCollectionChangedEventHandler(OnLinksCollectionChanged), sender, e);
            return;
        }

        if (_linkItems is null) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (var link in e.NewItems)
                {
                    _linkItems.Add((IWorkflowViewModel)link);
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (var link in e.OldItems)
                {
                    _linkItems.Remove((IWorkflowViewModel)link);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                _linkItems.Clear();
                if (_linksSubscribedTree is not null)
                {
                    _linkItems.Add(_linksSubscribedTree.VirtualLink);
                    foreach (var link in _linksSubscribedTree.Links)
                    {
                        _linkItems.Add(link);
                    }
                }
                break;
        }
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
        var tree = _tree;
        if (tree is null) return;

        var layout = tree.Layout;
        if (layout is null) return;

        var size = layout.ActualSize;
        var width = Math.Max(1, (int)Math.Ceiling(size.Width));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height));
        PART_Canvas.Width = width;
        PART_Canvas.Height = height;
        PART_LinksHost.Width = width;
        PART_LinksHost.Height = height;
        PART_ScrollViewer.AutoScrollMinSize = new Size(width, height);
    }

    // ── View factories (NodeView/SlotView/LinkView templates) ─────────────────

    private Control CreateNodeView(IWorkflowNodeViewModel node)
    {
        var view = new NodeView { ViewModel = node };
        PopulateSlotHosts(view, node);
        return view;
    }

    private static Control CreateLinkView(IWorkflowLinkViewModel link)
    {
        var view = new LinkView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        view.Bind(link);
        return view;
    }

    /// <summary>
    /// Populates the node view's PART_InputSlot / PART_OutputSlots hosts from
    /// <see cref="IWorkflowNodeViewModel.Slots"/>, distinguishing inputs from
    /// outputs by <see cref="IWorkflowSlotViewModel.Channel"/>.
    /// </summary>
    private static void PopulateSlotHosts(NodeView view, IWorkflowNodeViewModel node)
    {
        foreach (var slot in node.Slots)
        {
            var isInput = slot.Channel.HasFlag(SlotChannel.OneSource)
                          || slot.Channel.HasFlag(SlotChannel.MultipleSources);
            var slotView = new SlotView { ViewModel = slot };
            if (isInput)
            {
                slotView.Location = Point.Empty;
                view.PART_InputSlot.Controls.Add(slotView);
                slotView.BringToFront();
            }
            else
            {
                view.PART_OutputSlots.Controls.Add(slotView);
                slotView.BringToFront();
            }
        }
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
            ViewPool.SetItemsSource(PART_LinksHost, null);
            ViewPool.SetTemplateSelector(PART_LinksHost, null);
            if (_linksSubscribedTree is not null && _linksChangedHandler is not null)
            {
                _linksSubscribedTree.Links.CollectionChanged -= _linksChangedHandler;
                _linksSubscribedTree = null;
                _linksChangedHandler = null;
            }

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnTreeChanged;
                _notifier = null;
            }
        }

        base.Dispose(disposing);
    }
}
