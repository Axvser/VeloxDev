using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A workflow slot's glyph: a filled circle tinted by the slot's state, placed on the card by its host.
/// </summary>
/// <remarks>
/// <para>
/// The slot comes from the <c>DataContext</c>; the host positions the element at the port centre, so the glyph
/// needs no geometry of its own. Derive from it for the size and the palette — the binding, the state tinting and
/// the repaint are the same in every host.
/// </para>
/// <para>
/// The three state colours are semantic — a port that can send, one that can receive, and one that can do both —
/// so they are fixed rather than part of a palette. <see cref="StandbyColor"/> covers the idle slot, which is a
/// style choice.
/// </para>
/// </remarks>
public class WorkflowSlotView : FrameworkElement
{
    private IWorkflowSlotViewModel? _slot;

    private static readonly Brush SenderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x63, 0x47));
    private static readonly Brush ReceiverBrush = new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32));
    private static readonly Brush BothBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0x82, 0xEE));

    private Color _standbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    private Color _borderColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    private double _radius = 7;
    private double _borderThickness;

    /// <summary>Creates the glyph.</summary>
    public WorkflowSlotView()
    {
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The glyph's radius, in the card's design coordinates.</summary>
    public double Radius
    {
        get => _radius;
        set
        {
            if (_radius == value) return;
            _radius = value;
            ResizeToGlyph();
            InvalidateVisual();
        }
    }

    /// <summary>The glyph's fill while the slot is idle.</summary>
    public Color StandbyColor
    {
        get => _standbyColor;
        set
        {
            if (_standbyColor == value) return;
            _standbyColor = value;
            InvalidateVisual();
        }
    }

    /// <summary>The glyph's outline colour. Only drawn when <see cref="BorderThickness"/> is above zero.</summary>
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            if (_borderColor == value) return;
            _borderColor = value;
            InvalidateVisual();
        }
    }

    /// <summary>The glyph's outline width. Zero (the default) draws no outline.</summary>
    public double BorderThickness
    {
        get => _borderThickness;
        set
        {
            if (_borderThickness == value) return;
            _borderThickness = value;
            InvalidateVisual();
        }
    }

    /// <summary>The slot this glyph is showing, taken from the <c>DataContext</c>.</summary>
    protected IWorkflowSlotViewModel? Slot => _slot;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var pen = _borderThickness > 0 ? new Pen(new SolidColorBrush(_borderColor), _borderThickness) : null;
        dc.DrawEllipse(ResolveBrush(), pen, new Point(_radius, _radius), _radius, _radius);
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_slot is INotifyPropertyChanged old) old.PropertyChanged -= OnSlotChanged;

        _slot = DataContext as IWorkflowSlotViewModel;

        if (_slot is INotifyPropertyChanged notify) notify.PropertyChanged += OnSlotChanged;

        ResizeToGlyph();
        InvalidateVisual();
    }

    // 端口的状态变了（Core 的 UpdateState 在连线建立/删除时改它）就要重画，颜色才跟得上。
    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State))
        {
            InvalidateVisual();
        }
    }

    private void ResizeToGlyph()
    {
        Width = _radius * 2;
        Height = _radius * 2;
    }

    private Brush ResolveBrush()
    {
        if (_slot is null) return new SolidColorBrush(_standbyColor);

        var state = _slot.State;
        bool sender = state.HasFlag(SlotState.Sender);
        bool receiver = state.HasFlag(SlotState.Receiver);
        if (sender && receiver) return BothBrush;
        if (sender) return SenderBrush;
        if (receiver) return ReceiverBrush;
        return new SolidColorBrush(_standbyColor);
    }
}
