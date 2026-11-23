using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using VisualGrid = VeloxDev.WinForms.Xaml.Visuals.Controls.Grid;
using VisualPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.Panel;
using VisualScrollViewer = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer;
using VisualUserControl = VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl;
using XamlBinding = VeloxDev.WinForms.Xaml.Binding;
using DrawingPen = System.Drawing.Pen;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WinForms.Headless.Tests.Infrastructure;

// The surface is a single VisualHost: one HWND paints a visual tree whose named elements are the parts. The view
// pool materializes Helper.VisibleItems into node/link views; every visual is a plain element, no part is an HWND.
internal sealed class TestWorkflowSurface : VisualHost
{
    public TestWorkflowSurface()
    {
        Size = new DrawingSize(800, 600);
        // Keep the camera math in logical units so a scaled monitor cannot change the expectations.
        DpiScaleOverride = 1f;

        SurfaceView = new TestWorkflowSurfaceView();
        RootContainer = new VisualGrid();
        RootContainer.Add(SurfaceView);
        Root = RootContainer;

        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_GridDecorator");
        WorkflowSurfaceBehavior.SetZoomEnabled(this, true);

        // The headless selector owns every view: node cards bind their geometry, links bind their endpoints.
        ViewPool.SetTemplateSelector(SurfaceView.Canvas, new TestVisualTemplateSelector());
    }

    public TestWorkflowSurfaceView SurfaceView { get; }

    public VisualGrid RootContainer { get; }

    public VisualCanvas Canvas => SurfaceView.Canvas;

    public VisualScrollViewer ScrollViewer => SurfaceView.ScrollViewer;

    public TestVisualGridDecorator GridDecorator => SurfaceView.GridDecorator;

    /// <summary>Runs one real measure/arrange/render pass into an off-screen bitmap.</summary>
    /// <remarks>
    /// The visual tree lays out lazily, on paint or hit test. Nothing is painted into a window here, so the
    /// harness drives the same layout path through <see cref="VisualHost.RenderTo"/>. A pass can add pooled
    /// views during virtualisation, so callers render twice when they depend on the first pass's measurement.
    /// </remarks>
    public void RenderSurface()
    {
        var size = LogicalClientSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            size = new DrawingSize(800, 600);
        }

        using var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        RenderTo(graphics, size);
    }

    /// <summary>The visual the pool created for <paramref name="item"/>, or <see langword="null"/>.</summary>
    public VisualElement? ViewFor(object item)
    {
        foreach (var child in Canvas.Children)
        {
            if (child is VisualElement element && ReferenceEquals(element.DataContext, item))
            {
                return element;
            }
        }

        return null;
    }
}

// The named parts, assembled exactly like a generated surface view: decorator > scroll viewer > canvas.
internal sealed class TestWorkflowSurfaceView : VisualUserControl
{
    public TestWorkflowSurfaceView()
    {
        GridDecorator = new TestVisualGridDecorator { Name = "PART_GridDecorator" };
        ScrollViewer = new VisualScrollViewer
        {
            Name = "PART_ScrollViewer",
            IsScrollInputEnabled = false,
        };
        Canvas = new VisualCanvas
        {
            Name = "PART_Canvas",
            UnboundedHitTest = true,
            ClipToBounds = false,
        };

        ScrollViewer.Content = Canvas;
        GridDecorator.Content = ScrollViewer;
        Content = GridDecorator;
    }

    public TestVisualGridDecorator GridDecorator { get; }

    public VisualScrollViewer ScrollViewer { get; }

    public VisualCanvas Canvas { get; }
}

// A template-owned decorator: the adapter only defines the contract and pushes the offsets into it.
internal sealed class TestVisualGridDecorator : VisualElement, IWorkflowGridDecorator
{
    private VisualElement? _content;

    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public double ContentOffsetX { get; set; }

    public double ContentOffsetY { get; set; }

    // No ruler band, so the camera content offset is exactly the layout's ActualOffset.
    public double RulerBand => 0d;

    public VisualElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (_content is not null)
            {
                RemoveChild(_content);
            }

            _content = value;
            if (value is not null)
            {
                Add(value);
            }

            InvalidateMeasure();
        }
    }

    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
    {
        _content?.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeOverride(DrawingSize finalSize)
    {
        _content?.Arrange(new RectangleF(0f, 0f, finalSize.Width, finalSize.Height));
    }
}

// The pool hands one view per visible item; the view is chosen here rather than by a XAML resource key.
internal sealed class TestVisualTemplateSelector : VisualTemplateSelector
{
    public override VisualElement? SelectTemplate(object? item) => item switch
    {
        INodeComponent node => new TestNodeView(node),
        ILinkComponent => new TestLinkView(),
        _ => null,
    };
}

// A node card with two named slot parts and a drag band. Geometry is bound exactly like a generated template.
internal sealed class TestNodeView : VisualUserControl
{
    private const double SlotSize = 18d;
    private const double DragBandHeight = 24d;

    public TestNodeView(INodeComponent node)
    {
        ClipToBounds = false;

        VisualBindingOperations.Bind(this, WidthProperty, new XamlBinding("Size.Width"));
        VisualBindingOperations.Bind(this, HeightProperty, new XamlBinding("Size.Height"));
        VisualBindingOperations.Bind(this, VisualCanvas.LeftProperty, new XamlBinding("Anchor.Horizontal"));
        VisualBindingOperations.Bind(this, VisualCanvas.TopProperty, new XamlBinding("Anchor.Vertical"));
        VisualBindingOperations.Bind(this, VisualPanel.ZIndexProperty, new XamlBinding("Anchor.Layer"));

        // The adapter measures the named parts and writes their world-space centres back to the model.
        WorkflowSlotLayoutBehavior.SetIsEnabled(this, true);
        WorkflowSlotLayoutBehavior.SetSlotNames(this, "PART_InputSlot,PART_OutputSlot");
        WorkflowSlotLayoutBehavior.SetCoordinateHostName(this, "PART_Canvas");

        var content = new VisualCanvas { Name = "PART_NodeCanvas", ClipToBounds = false };

        var dragBand = new TestDragHandle
        {
            Width = node.Size.Width,
            Height = DragBandHeight,
        };
        WorkflowNodeDragBehavior.SetIsEnabled(dragBand, true);
        WorkflowNodeDragBehavior.SetCoordinateHostName(dragBand, "PART_Canvas");
        VisualCanvas.SetLeft(dragBand, 0d);
        VisualCanvas.SetTop(dragBand, 0d);
        content.AddChild(dragBand);

        foreach (var slot in node.Slots)
        {
            var receiver = IsReceiver(slot);
            var slotView = new TestSlotView
            {
                Name = receiver ? "PART_InputSlot" : "PART_OutputSlot",
                Width = SlotSize,
                Height = SlotSize,
                DataContext = slot,
            };
            WorkflowSlotConnectionBehavior.SetIsEnabled(slotView, true);

            VisualCanvas.SetLeft(slotView, receiver ? 0d : node.Size.Width - SlotSize);
            VisualCanvas.SetTop(slotView, (node.Size.Height - SlotSize) / 2d);
            content.AddChild(slotView);
        }

        Content = content;
    }

    // A port is placed on the edge its channel (or its existing connection) says it belongs to.
    private static bool IsReceiver(ISlotComponent slot)
    {
        if (slot.Sources.Count > 0)
        {
            return true;
        }

        if (slot.Targets.Count > 0)
        {
            return false;
        }

        return slot.Channel.HasFlag(SlotChannel.OneSource)
               && !slot.Channel.HasFlag(SlotChannel.OneTarget);
    }
}

internal sealed class TestSlotView : VisualElement
{
}

internal sealed class TestDragHandle : VisualElement
{
}

// Cubic Bézier that publishes the curve Core hit-tests and paints it in world coordinates.
internal sealed class TestLinkView : VisualElement
{
    private const double MinimumPull = 40d;

    public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
        nameof(StartLeft), typeof(double), typeof(TestLinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
        nameof(StartTop), typeof(double), typeof(TestLinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
        nameof(EndLeft), typeof(double), typeof(TestLinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
        nameof(EndTop), typeof(double), typeof(TestLinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty CanRenderProperty = DependencyProperty.Register(
        nameof(CanRender), typeof(bool), typeof(TestLinkView),
        new PropertyMetadata(true) { AffectsRender = true });

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(TestLinkView),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { AffectsRender = true });

    private ILinkComponent? _publishedLink;

    public TestLinkView()
    {
        // The pointer must fall through to the cards and the empty canvas; the curve answers through Core.
        IsHitTestVisible = false;
        VisualPanel.SetZIndex(this, -1);

        // The template binds the link to the canvas, not to a fixed million-pixel box; an ElementName source is
        // resolved after the pooled view is parented.
        VisualBindingOperations.Bind(this, WidthProperty, new XamlBinding("Width") { ElementName = "PART_Canvas" });
        VisualBindingOperations.Bind(this, HeightProperty, new XamlBinding("Height") { ElementName = "PART_Canvas" });
        VisualBindingOperations.Bind(this, StartLeftProperty, new XamlBinding("Sender.Anchor.Horizontal"));
        VisualBindingOperations.Bind(this, StartTopProperty, new XamlBinding("Sender.Anchor.Vertical"));
        VisualBindingOperations.Bind(this, EndLeftProperty, new XamlBinding("Receiver.Anchor.Horizontal"));
        VisualBindingOperations.Bind(this, EndTopProperty, new XamlBinding("Receiver.Anchor.Vertical"));
        VisualBindingOperations.Bind(this, CanRenderProperty, new XamlBinding("IsVisible"));
    }

    public double StartLeft
    {
        get => (double)GetValue(StartLeftProperty)!;
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => (double)GetValue(StartTopProperty)!;
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => (double)GetValue(EndLeftProperty)!;
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => (double)GetValue(EndTopProperty)!;
        set => SetValue(EndTopProperty, value);
    }

    public bool CanRender
    {
        get => (bool)GetValue(CanRenderProperty)!;
        set => SetValue(CanRenderProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty)!;
        set => SetValue(LineColorProperty, value);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
            InvalidateVisual();
        }
    }

    protected override void OnRender(Graphics graphics)
    {
        if (!CanRender || DataContext is not ILinkComponent link || !link.IsRenderReady())
        {
            return;
        }

        var curve = LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);

        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        link.PublishCurve(curve, this);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new DrawingPen(LineColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var index = 1; index < curve.Count; index++)
        {
            graphics.DrawLine(
                pen,
                (float)curve.XAt(index - 1),
                (float)curve.YAt(index - 1),
                (float)curve.XAt(index),
                (float)curve.YAt(index));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
        }

        base.Dispose(disposing);
    }
}
