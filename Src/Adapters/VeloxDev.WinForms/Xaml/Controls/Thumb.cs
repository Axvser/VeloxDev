using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>Carries the starting offset of a thumb drag.</summary>
public class DragStartedEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="horizontalOffset">The horizontal position where the drag started.</param>
    /// <param name="verticalOffset">The vertical position where the drag started.</param>
    public DragStartedEventArgs(int horizontalOffset, int verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }

    /// <summary>The horizontal position where the drag started.</summary>
    public int HorizontalOffset { get; }

    /// <summary>The vertical position where the drag started.</summary>
    public int VerticalOffset { get; }
}

/// <summary>Carries the distance moved since the previous thumb drag event.</summary>
public class DragDeltaEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="horizontalChange">The horizontal movement since the previous event.</param>
    /// <param name="verticalChange">The vertical movement since the previous event.</param>
    public DragDeltaEventArgs(int horizontalChange, int verticalChange)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
    }

    /// <summary>The horizontal movement since the previous event, in pixels.</summary>
    public int HorizontalChange { get; }

    /// <summary>The vertical movement since the previous event, in pixels.</summary>
    public int VerticalChange { get; }
}

/// <summary>Carries the total movement of a finished thumb drag.</summary>
public class DragCompletedEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="horizontalChange">The total horizontal movement.</param>
    /// <param name="verticalChange">The total vertical movement.</param>
    /// <param name="canceled">Whether the drag ended without a normal mouse release.</param>
    public DragCompletedEventArgs(int horizontalChange, int verticalChange, bool canceled)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        Canceled = canceled;
    }

    /// <summary>The total horizontal movement, in pixels.</summary>
    public int HorizontalChange { get; }

    /// <summary>The total vertical movement, in pixels.</summary>
    public int VerticalChange { get; }

    /// <summary>Whether the drag was cancelled, for example by a lost mouse capture.</summary>
    public bool Canceled { get; }
}

/// <summary>
/// A self-drawn drag grip that reports incremental movement through <see cref="DragStarted"/>,
/// <see cref="DragDelta"/> and <see cref="DragCompleted"/>.
/// </summary>
/// <remarks>
/// <para>
/// The thumb does not move itself; a host reacts to the events. Movement is reported in the thumb's client
/// coordinates, so a host that resizes a sibling keeps receiving steady deltas.
/// </para>
/// <para>The grip surface and colours come from <see cref="Theme.Current"/> unless a colour is set explicitly.</para>
/// </remarks>
public class Thumb : Control
{
    private bool _dragEnabled = true;
    private bool _dragging;
    private bool _hasCapture;
    private Point _lastPoint;
    private int _totalHorizontal;
    private int _totalVertical;

    /// <summary>Creates the thumb with the double-buffered, self-painted style it needs.</summary>
    public Thumb()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Cursor = Cursors.SizeAll;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Raised when a drag starts.</summary>
    public event EventHandler<DragStartedEventArgs>? DragStarted;

    /// <summary>Raised for every movement while a drag is active.</summary>
    public event EventHandler<DragDeltaEventArgs>? DragDelta;

    /// <summary>Raised when a drag ends, normally or because it was cancelled.</summary>
    public event EventHandler<DragCompletedEventArgs>? DragCompleted;

    /// <summary>Whether a drag is currently in progress.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Allows the thumb to start new drags; drags are enabled by default.</summary>
    public void Enable() => _dragEnabled = true;

    /// <summary>Stops the thumb from starting new drags and cancels the active one, if any.</summary>
    public void Disable()
    {
        _dragEnabled = false;
        EndDrag(canceled: true);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var height = Math.Max(16, Theme.Current.ControlHeight);
        return new Size(10, height);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = Theme.Current;
        var bounds = ClientRectangle;
        var background = HasXamlValue(BackgroundProperty) && Background is { } custom
            ? custom.ApproximateColor
            : _dragging || IsPointerOver
                ? theme.ControlHover
                : theme.ControlBackground;

        XamlDrawing.PaintChrome(
            e.Graphics,
            bounds,
            background,
            theme.ControlBorder,
            new Thickness(1f),
            theme.CornerRadius);
        PaintGrip(e.Graphics, _dragging ? theme.Accent : theme.MutedForeground);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled || !_dragEnabled || _dragging)
        {
            return;
        }

        Focus();
        _dragging = true;
        _lastPoint = e.Location;
        _totalHorizontal = 0;
        _totalVertical = 0;
        _hasCapture = CapturePointer();
        Invalidate();
        DragStarted?.Invoke(this, new DragStartedEventArgs(0, 0));
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var horizontal = e.X - _lastPoint.X;
        var vertical = e.Y - _lastPoint.Y;
        if (horizontal == 0 && vertical == 0)
        {
            return;
        }

        _lastPoint = e.Location;
        _totalHorizontal += horizontal;
        _totalVertical += vertical;
        DragDelta?.Invoke(this, new DragDeltaEventArgs(horizontal, vertical));
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging)
        {
            base.OnMouseUp(e);
            return;
        }

        _dragging = false;
        _hasCapture = false;
        base.OnMouseUp(e);
        Invalidate();
        DragCompleted?.Invoke(this, new DragCompletedEventArgs(_totalHorizontal, _totalVertical, canceled: false));
    }

    /// <inheritdoc />
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_dragging && _hasCapture && !Capture)
        {
            EndDrag(canceled: true);
        }
    }

    /// <inheritdoc />
    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            EndDrag(canceled: true);
        }

        base.OnEnabledChanged(e);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void EndDrag(bool canceled)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _hasCapture = false;
        ReleasePointerCapture();
        Invalidate();
        DragCompleted?.Invoke(this, new DragCompletedEventArgs(_totalHorizontal, _totalVertical, canceled));
    }

    // 三个圆点沿长边排开；竖长与横长的抓握方向自然区分。
    private void PaintGrip(Graphics graphics, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = ClientSize.Width / 2f;
        var centerY = ClientSize.Height / 2f;
        var vertical = ClientSize.Height >= ClientSize.Width;
        using var brush = new SolidBrush(color);
        for (var index = -1; index <= 1; index++)
        {
            var x = vertical ? centerX - 1f : centerX + (index * 5f) - 1f;
            var y = vertical ? centerY + (index * 5f) - 1f : centerY - 1f;
            graphics.FillEllipse(brush, x, y, 2f, 2f);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
