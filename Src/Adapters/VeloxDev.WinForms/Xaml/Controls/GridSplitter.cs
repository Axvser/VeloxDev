using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>Which dimension a <see cref="GridSplitter"/> changes while it is dragged.</summary>
public enum GridResizeDirection
{
    /// <summary>The drag changes the width of the neighbouring columns.</summary>
    Columns = 0,

    /// <summary>The drag changes the height of the neighbouring rows.</summary>
    Rows = 1,
}

/// <summary>Which neighbours of a <see cref="GridSplitter"/> follow its drag.</summary>
public enum GridResizeBehavior
{
    /// <summary>Both the previous and next sibling resize, in opposite directions.</summary>
    PreviousAndNext = 0,

    /// <summary>Only the previous sibling resizes.</summary>
    Previous = 1,

    /// <summary>Only the next sibling resizes.</summary>
    Next = 2,
}

/// <summary>
/// A draggable divider that resizes its neighbouring controls or an explicit <see cref="Target"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the common case, not a full WPF grid redistribution: the splitter changes the
/// <see cref="System.Windows.Forms.Control.Width"/> or <see cref="System.Windows.Forms.Control.Height"/> of its
/// previous and/or next sibling, so it works predictably inside a plain panel or a stack panel whose children keep
/// their own size. A star-sized <see cref="Grid"/> does not redistribute its columns from these writes.
/// </para>
/// <para>
/// <see cref="Orientation"/> controls the painted shape; <see cref="ResizeDirection"/> controls the drag axis. The
/// grip surface and colours come from <see cref="Theme.Current"/> unless a colour is set explicitly.
/// </para>
/// </remarks>
public class GridSplitter : Control
{
    /// <summary>The painted axis of the divider; vertical by default.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(GridSplitter),
        new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>Whether a drag changes column widths or row heights; columns by default.</summary>
    public static readonly DependencyProperty ResizeDirectionProperty = DependencyProperty.Register(
        nameof(ResizeDirection),
        typeof(GridResizeDirection),
        typeof(GridSplitter),
        new PropertyMetadata(GridResizeDirection.Columns, OnResizeDirectionPropertyChanged));

    /// <summary>Which neighbours follow the drag; both by default.</summary>
    public static readonly DependencyProperty ResizeBehaviorProperty = DependencyProperty.Register(
        nameof(ResizeBehavior),
        typeof(GridResizeBehavior),
        typeof(GridSplitter),
        new PropertyMetadata(GridResizeBehavior.PreviousAndNext));

    /// <summary>An explicit control to resize; when null the neighbouring siblings are used.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target),
        typeof(System.Windows.Forms.Control),
        typeof(GridSplitter),
        new PropertyMetadata(null));

    private bool _dragging;
    private bool _hasCapture;
    private Point _lastPoint;

    /// <summary>Creates the splitter with the double-buffered, self-painted style it needs.</summary>
    public GridSplitter()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        UpdateCursor();
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets the painted axis of the divider.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets whether a drag changes column widths or row heights.</summary>
    public GridResizeDirection ResizeDirection
    {
        get => (GridResizeDirection)GetValue(ResizeDirectionProperty)!;
        set => SetValue(ResizeDirectionProperty, value);
    }

    /// <summary>Gets or sets which neighbours follow the drag.</summary>
    public GridResizeBehavior ResizeBehavior
    {
        get => (GridResizeBehavior)GetValue(ResizeBehaviorProperty)!;
        set => SetValue(ResizeBehaviorProperty, value);
    }

    /// <summary>Gets or sets an explicit control to resize instead of the neighbouring siblings.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Target
    {
        get => (System.Windows.Forms.Control?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Whether the splitter is currently being dragged.</summary>
    public bool IsDragging => _dragging;

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var thickness = 6;
        var length = Math.Max(16, Theme.Current.ControlHeight);
        return Orientation == Orientation.Vertical
            ? new Size(thickness, length)
            : new Size(length, thickness);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = Theme.Current;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var background = _dragging
            ? theme.ControlPressed
            : IsPointerOver
                ? theme.ControlHover
                : Color.Transparent;
        if (Background is { A: > 0 } authoredBrush && HasXamlValue(BackColorProperty))
        {
            background = authoredBrush.ApproximateColor;
        }

        if (background.A > 0)
        {
            using var path = XamlDrawing.RoundedRectangle(new RectangleF(0, 0, Width, Height), theme.CornerRadius);
            using var fill = new SolidBrush(background);
            graphics.FillPath(fill, path);
        }

        var grip = _dragging
            ? theme.Accent
            : IsPointerOver
                ? theme.Foreground
                : theme.MutedForeground;
        PaintGrip(graphics, grip);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled)
        {
            return;
        }

        Focus();
        _dragging = true;
        _lastPoint = e.Location;
        _hasCapture = CapturePointer();
        Invalidate();
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
        ResizeBy(horizontal, vertical);
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
    }

    /// <inheritdoc />
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_dragging && _hasCapture && !Capture)
        {
            _dragging = false;
            _hasCapture = false;
            Invalidate();
        }
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

    private void PaintGrip(Graphics graphics, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        var vertical = Orientation == Orientation.Vertical;
        var thickness = 2;
        var length = Math.Max(8, (vertical ? ClientSize.Height : ClientSize.Width) * 2 / 5);
        var bounds = vertical
            ? new Rectangle((ClientSize.Width - thickness) / 2, (ClientSize.Height - length) / 2, thickness, length)
            : new Rectangle((ClientSize.Width - length) / 2, (ClientSize.Height - thickness) / 2, length, thickness);
        using var brush = new SolidBrush(color);
        using var path = XamlDrawing.RoundedRectangle(bounds, 1f);
        graphics.FillPath(brush, path);
    }

    private void ResizeBy(int horizontalChange, int verticalChange)
    {
        var horizontal = ResizeDirection == GridResizeDirection.Columns;
        var delta = horizontal ? horizontalChange : verticalChange;
        if (delta == 0)
        {
            return;
        }

        if (Target is { } target)
        {
            ResizeControl(target, delta, horizontal);
            return;
        }

        if (Parent is not { } parent)
        {
            return;
        }

        var index = parent.Controls.GetChildIndex(this);
        if (index < 0)
        {
            return;
        }

        var before = FindSibling(parent, index, -1);
        var after = FindSibling(parent, index, 1);
        var useBefore = ResizeBehavior != GridResizeBehavior.Next;
        var useAfter = ResizeBehavior != GridResizeBehavior.Previous;

        if (useBefore && useAfter && before is not null && after is not null)
        {
            delta = ClampDelta(delta, before, after, horizontal);
            ResizeControl(before, delta, horizontal);
            ResizeControl(after, -delta, horizontal);
            return;
        }

        if (useBefore && before is not null)
        {
            if (delta < 0)
            {
                delta = Math.Max(delta, -Available(before, horizontal));
            }

            ResizeControl(before, delta, horizontal);
            return;
        }

        if (useAfter && after is not null)
        {
            if (delta > 0)
            {
                delta = Math.Min(delta, Available(after, horizontal));
            }

            ResizeControl(after, -delta, horizontal);
        }
    }

    private static System.Windows.Forms.Control? FindSibling(System.Windows.Forms.Control parent, int index, int direction)
    {
        for (var current = index + direction; current >= 0 && current < parent.Controls.Count; current += direction)
        {
            var child = parent.Controls[current];
            if (child is not null && !child.IsDisposed && XamlLayout.OccupiesSpace(child))
            {
                return child;
            }
        }

        return null;
    }

    // delta 正方向是「前一个变大、后一个变小」；两边的余量都要满足，实际位移取交集。
    private static int ClampDelta(int delta, System.Windows.Forms.Control grow, System.Windows.Forms.Control shrink, bool horizontal)
    {
        if (delta > 0)
        {
            return Math.Min(delta, Available(shrink, horizontal));
        }

        if (delta < 0)
        {
            return Math.Max(delta, -Available(grow, horizontal));
        }

        return 0;
    }

    private static int Available(System.Windows.Forms.Control control, bool horizontal)
        => Math.Max(0, Extent(control, horizontal) - MinimumExtent(control, horizontal));

    private static void ResizeControl(System.Windows.Forms.Control control, int amount, bool horizontal)
    {
        if (control.IsDisposed)
        {
            return;
        }

        var next = Math.Max(MinimumExtent(control, horizontal), Extent(control, horizontal) + amount);
        if (control is Control xaml)
        {
            if (horizontal)
            {
                xaml.Width = next;
            }
            else
            {
                xaml.Height = next;
            }

            return;
        }

        if (horizontal)
        {
            control.Width = next;
        }
        else
        {
            control.Height = next;
        }
    }

    private static int Extent(System.Windows.Forms.Control control, bool horizontal)
        => control is Control xaml
            ? horizontal ? xaml.Width : xaml.Height
            : horizontal ? control.Width : control.Height;

    private static int MinimumExtent(System.Windows.Forms.Control control, bool horizontal)
        => control is Control xaml
            ? horizontal ? Math.Max(0, xaml.MinWidth) : Math.Max(0, xaml.MinHeight)
            : horizontal ? Math.Max(0, control.MinimumSize.Width) : Math.Max(0, control.MinimumSize.Height);

    private void UpdateCursor()
        => Cursor = ResizeDirection == GridResizeDirection.Columns ? Cursors.SizeWE : Cursors.SizeNS;

    private static void OnResizeDirectionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is GridSplitter splitter && !splitter.IsDisposed)
        {
            splitter.UpdateCursor();
            splitter.Invalidate();
        }
    }

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is GridSplitter splitter && !splitter.IsDisposed)
        {
            splitter.PerformLayout();
            splitter.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
