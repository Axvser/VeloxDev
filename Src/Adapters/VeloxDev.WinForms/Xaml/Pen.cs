using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// How a shape is outlined: a brush, a width, an optional dash pattern and the GDI+ cap and join styles.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Draw"/> creates the matching <see cref="System.Drawing.Pen"/> for the duration of one stroke. The
/// implicit conversion to <see cref="System.Drawing.Pen"/> exposes the same cached GDI+ pen so the primitive can be
/// handed to an existing <see cref="Graphics"/> call without a cast.
/// </para>
/// <para>
/// Outline gradients are flattened to <see cref="Brush.ApproximateColor"/>: GDI+ pens accept a solid colour here.
/// </para>
/// </remarks>
public sealed class Pen : IDisposable
{
    private System.Drawing.Pen? _native;
    private Brush? _brush;
    private float _thickness = 1f;
    private float[]? _dashPattern;
    private LineCap _startLineCap = LineCap.Flat;
    private LineCap _endLineCap = LineCap.Flat;
    private LineJoin _lineJoin = LineJoin.Miter;

    /// <summary>Creates a transparent, one-pixel pen.</summary>
    public Pen()
    {
    }

    /// <summary>Creates a one-pixel pen of <paramref name="color"/>.</summary>
    /// <param name="color">The outline colour.</param>
    public Pen(Color color)
        : this(color, 1f)
    {
    }

    /// <summary>Creates a pen of <paramref name="color"/> and <paramref name="thickness"/>.</summary>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline width in pixels.</param>
    public Pen(Color color, float thickness)
    {
        _brush = new SolidColorBrush(color);
        Thickness = thickness;
    }

    /// <summary>Creates a pen that strokes with <paramref name="brush"/>.</summary>
    /// <param name="brush">The outline brush.</param>
    /// <param name="thickness">The outline width in pixels.</param>
    public Pen(Brush brush, float thickness)
    {
        _brush = brush;
        Thickness = thickness;
    }

    /// <summary>The brush the outline is painted with; a null brush draws nothing.</summary>
    public Brush? Brush
    {
        get => _brush;
        set
        {
            _brush = value;
            InvalidateNative();
        }
    }

    /// <summary>The outline width in pixels; one by default.</summary>
    public float Thickness
    {
        get => _thickness;
        set
        {
            _thickness = value;
            InvalidateNative();
        }
    }

    /// <summary>The dash pattern in units of <see cref="Thickness"/>; a null pattern draws a solid line.</summary>
    public float[]? DashPattern
    {
        get => _dashPattern;
        set
        {
            _dashPattern = value;
            InvalidateNative();
        }
    }

    /// <summary>The cap drawn at the start of an open figure.</summary>
    public LineCap StartLineCap
    {
        get => _startLineCap;
        set
        {
            _startLineCap = value;
            InvalidateNative();
        }
    }

    /// <summary>The cap drawn at the end of an open figure.</summary>
    public LineCap EndLineCap
    {
        get => _endLineCap;
        set
        {
            _endLineCap = value;
            InvalidateNative();
        }
    }

    /// <summary>The GDI+ name for <see cref="StartLineCap"/>; both write the same value.</summary>
    public LineCap StartCap
    {
        get => StartLineCap;
        set => StartLineCap = value;
    }

    /// <summary>The GDI+ name for <see cref="EndLineCap"/>; both write the same value.</summary>
    public LineCap EndCap
    {
        get => EndLineCap;
        set => EndLineCap = value;
    }

    /// <summary>How adjacent segments are joined.</summary>
    public LineJoin LineJoin
    {
        get => _lineJoin;
        set
        {
            _lineJoin = value;
            InvalidateNative();
        }
    }

    /// <summary>Strokes <paramref name="path"/> with this pen.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="path">The geometry to outline.</param>
    public void Draw(Graphics graphics, GraphicsPath path)
    {
        if (graphics is null || path is null || path.PointCount == 0)
        {
            return;
        }

        var brush = Brush;
        if (brush is null || brush.ApproximateColor.A <= 0 || Thickness <= 0f)
        {
            return;
        }

        graphics.DrawPath(Native, path);
    }

    /// <summary>Returns the cached GDI+ pen this outline drives.</summary>
    /// <param name="pen">The primitive pen.</param>
    public static implicit operator System.Drawing.Pen(Pen pen) => pen.Native;

    /// <inheritdoc />
    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
        GC.SuppressFinalize(this);
    }

    private System.Drawing.Pen Native
    {
        get
        {
            if (_native is not null)
            {
                return _native;
            }

            var color = Brush?.ApproximateColor ?? Color.Transparent;
            var native = new System.Drawing.Pen(color, Math.Max(0f, Thickness))
            {
                StartCap = _startLineCap,
                EndCap = _endLineCap,
                LineJoin = _lineJoin,
            };
            var dashPattern = DashPattern;
            if (dashPattern is { Length: > 0 })
            {
                try
                {
                    native.DashPattern = dashPattern;
                }
                catch (ArgumentException)
                {
                    // 非法 dash（0、负数或长度不足）：保留实线而不是抛给绘制阶段。
                }
            }

            _native = native;
            return native;
        }
    }

    private void InvalidateNative()
    {
        _native?.Dispose();
        _native = null;
    }
}
