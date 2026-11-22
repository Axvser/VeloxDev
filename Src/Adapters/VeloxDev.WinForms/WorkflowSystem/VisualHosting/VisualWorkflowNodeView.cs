using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualStackPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel;
using VisualTextBlock = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The default visual card for one workflow node: a rounded surface with a title, a subtitle and its slot glyphs.
/// </summary>
/// <remarks>
/// A card drags its node through <see cref="INodeComponent.MoveCommand"/>; the delta is converted from screen pixels
/// to world units with the layout's scale, so the card stays under the pointer at every zoom. <see cref="BuildContent"/>
/// is the extension point a host overrides to keep its own card design.
/// </remarks>
public class VisualWorkflowNodeView : VisualElement
{
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font SubtitleFont = new("Segoe UI", 8.5f);
    private static readonly Dictionary<Type, PropertyInfo?> TitleProperties = [];

    private readonly List<VisualWorkflowSlotView> _slotViews = [];
    internal Dictionary<ISlotComponent, Wf.Anchor> AnchorCache { get; } = [];
    private VisualElement? _content;
    private INodeComponent? _node;
    private INotifyPropertyChanged? _nodeNotifier;
    private bool _dragging;
    private DrawingPointF _lastPointer;

    /// <summary>Creates an unbound node card.</summary>
    public VisualWorkflowNodeView()
    {
        IsHitTestVisible = true;
        Focusable = false;
    }

    /// <summary>The node this card renders, or <see langword="null"/>.</summary>
    public INodeComponent? Node => _node;

    /// <summary>The slot glyphs owned by this card.</summary>
    public IReadOnlyList<VisualWorkflowSlotView> SlotViews => _slotViews;

    /// <summary>Binds this card to <paramref name="node"/>; passing <see langword="null"/> clears it.</summary>
    /// <param name="node">The node to render.</param>
    public void Bind(INodeComponent? node)
    {
        if (ReferenceEquals(_node, node))
        {
            return;
        }

        Unbind();
        _node = node;
        if (node is null)
        {
            return;
        }

        DataContext = node;
        if (node is INotifyPropertyChanged notifier)
        {
            _nodeNotifier = notifier;
            _nodeNotifier.PropertyChanged += OnNodePropertyChanged;
        }

        node.Slots.CollectionChanged += OnSlotsChanged;
        _content = BuildContent(node);
        if (_content is not null)
        {
            Add(_content);
            _content.IsHitTestVisible = false;
        }

        RebuildSlots();
        RefreshPosition();
    }

    /// <summary>Re-applies the model's current anchor and size to this card's bounds.</summary>
    public void RefreshPosition()
        => (ParentElement as VisualWorkflowCanvas)?.SyncViewGeometry(this);

    /// <summary>Builds the passive content of a card; a host overrides this to keep its own design.</summary>
    /// <param name="node">The node the card is bound to.</param>
    /// <returns>The visual that fills the card behind its slots.</returns>
    protected virtual VisualElement BuildContent(INodeComponent node)
    {
        var stack = new VisualStackPanel { Spacing = 3 };
        stack.Add(new VisualTextBlock
        {
            Text = TitleOf(node),
            Font = TitleFont,
            Foreground = Theme.Current.Foreground,
        });
        stack.Add(new VisualTextBlock
        {
            Text = node.GetType().Name,
            Font = SubtitleFont,
            Foreground = Theme.Current.MutedForeground,
        });

        return new VisualBorder
        {
            Background = Theme.Current.Surface,
            BorderBrush = Theme.Current.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 8f,
            Padding = new XamlThickness(12f, 9f),
            Child = stack,
        };
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
    {
        _content?.Measure(availableSize);
        foreach (var slot in _slotViews)
        {
            slot.Measure(DrawingSize.Empty);
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(DrawingSize finalSize)
    {
        _content?.Arrange(new DrawingRectangleF(0f, 0f, finalSize.Width, finalSize.Height));
        ArrangeSlots(finalSize);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left || HitSlot(args.Position))
        {
            return;
        }

        _dragging = true;
        _lastPointer = args.Position;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        if (!_dragging || (args.Buttons & MouseButtons.Left) == 0)
        {
            _dragging = false;
            return;
        }

        if (_node is not { } node)
        {
            return;
        }

        var deltaX = args.Position.X - _lastPointer.X;
        var deltaY = args.Position.Y - _lastPointer.Y;
        if (Math.Abs(deltaX) < 0.01f && Math.Abs(deltaY) < 0.01f)
        {
            return;
        }

        var scale = node.Parent?.Layout?.Scale.Horizontal ?? 1d;
        if (double.IsNaN(scale) || scale <= 0d)
        {
            scale = 1d;
        }

        node.MoveCommand.Execute(new Wf.Offset(deltaX * scale, deltaY * scale));
        _lastPointer = args.Position;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _dragging = false;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
        }

        base.Dispose(disposing);
    }

    private void Unbind()
    {
        if (_node is not null)
        {
            _node.Slots.CollectionChanged -= OnSlotsChanged;
        }

        if (_nodeNotifier is not null)
        {
            _nodeNotifier.PropertyChanged -= OnNodePropertyChanged;
            _nodeNotifier = null;
        }

        _dragging = false;
        ClearSlots();

        if (_content is not null)
        {
            if (Children.Contains(_content))
            {
                RemoveChild(_content);
            }

            _content.Dispose();
            _content = null;
        }

        _node = null;
        DataContext = null;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
        {
            RefreshPosition();
            (ParentElement as VisualWorkflowCanvas)?.NotifyNodeGeometryChanged(this);
        }
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSlots();

    private void RebuildSlots()
    {
        ClearSlots();
        if (_node is not { } node)
        {
            return;
        }

        foreach (var slot in node.Slots)
        {
            var view = new VisualWorkflowSlotView { Slot = slot };
            Add(view);
            _slotViews.Add(view);
        }

        InvalidateMeasure();
    }

    private void ClearSlots()
    {
        foreach (var view in _slotViews)
        {
            if (Children.Contains(view))
            {
                RemoveChild(view);
            }

            view.Dispose();
        }

        _slotViews.Clear();
    }

    private void ArrangeSlots(DrawingSize finalSize)
    {
        if (_slotViews.Count == 0)
        {
            return;
        }

        var left = new List<VisualWorkflowSlotView>();
        var right = new List<VisualWorkflowSlotView>();
        foreach (var view in _slotViews)
        {
            if (view.Slot is { } slot && CanSend(slot.Channel))
            {
                right.Add(view);
            }
            else
            {
                left.Add(view);
            }
        }

        PlaceColumn(left, finalSize, rightSide: false);
        PlaceColumn(right, finalSize, rightSide: true);

        static void PlaceColumn(List<VisualWorkflowSlotView> views, DrawingSize size, bool rightSide)
        {
            if (views.Count == 0)
            {
                return;
            }

            const float glyph = VisualWorkflowSlotView.DefaultGlyphSize;
            var usable = Math.Max(0f, size.Height - glyph);
            var step = views.Count == 1 ? usable : usable / (views.Count - 1);
            var x = rightSide ? size.Width - glyph : 0f;
            for (var index = 0; index < views.Count; index++)
            {
                var y = views.Count == 1 ? usable / 2f : step * index;
                views[index].Arrange(new DrawingRectangleF(x, y, glyph, glyph));
            }
        }
    }

    private bool HitSlot(DrawingPointF surfacePoint)
    {
        var local = PointFromSurface(surfacePoint);
        foreach (var view in _slotViews)
        {
            if (view.Bounds.Contains(local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanSend(SlotChannel channel)
        => (channel & SlotChannel.OneTarget) != 0 || (channel & SlotChannel.MultipleTargets) != 0;

    private static string TitleOf(INodeComponent node)
    {
        var type = node.GetType();
        if (!TitleProperties.TryGetValue(type, out var property))
        {
            property = type.GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
            TitleProperties[type] = property;
        }

        return property?.GetValue(node) as string ?? type.Name;
    }
}
