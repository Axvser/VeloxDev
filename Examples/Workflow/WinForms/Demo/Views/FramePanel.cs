using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Demo.Views;

/// <summary>
/// A panel that draws a rounded, hairline-framed surface — the shape WinForms has no control for.
///
/// The reference's card is built out of rounded, 1px-bordered boxes everywhere: the input fields, the script
/// editor and the header strip inside it, the status capsule, and the 2px type accent bar. A WinForms
/// <see cref="Panel"/> can only be outlined in a system colour, and a <see cref="TextBox"/>/<see cref="ComboBox"/>
/// cannot be outlined at all, so every one of those is this control with different metrics. Its own corners are
/// painted with <see cref="Ground"/> so the shape blends into whatever it is sitting on; the children it hosts
/// (the field's borderless text box, the capsule's label) are inset far enough that their square corners never
/// poke past the curve.
/// </summary>
internal sealed class FramePanel : Panel
{
    private Color _fill = CardTheme.Surface;
    private Color _frame = CardTheme.Border;
    private Color _ground = CardTheme.Surface;
    private float _frameWidth = 1f;
    private float _radius = CardTheme.FieldRadius;
    private CardCorners _corners = CardCorners.All;

    public FramePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = _ground;
    }

    /// <summary>The rounded surface's colour.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    /// <summary>The hairline's colour. Ignored when <see cref="FrameWidth"/> is zero.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Frame
    {
        get => _frame;
        set { _frame = value; Invalidate(); }
    }

    /// <summary>The colour outside the rounded shape — normally the surface this panel sits on.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Ground
    {
        get => _ground;
        set { _ground = value; BackColor = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float FrameWidth
    {
        get => _frameWidth;
        set { _frameWidth = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Radius
    {
        get => _radius;
        set { _radius = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CardCorners Corners
    {
        get => _corners;
        set { _corners = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The stroke is centred on the geometry, so the path is inset by half its width and the frame lands
        // exactly on this control's bounds.
        var inset = _frameWidth > 0 ? _frameWidth / 2f : 0f;
        var rect = new RectangleF(inset, inset, Width - (inset * 2), Height - (inset * 2));
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var path = CardTheme.RoundedPath(rect, _radius, _corners);
        using var fill = new SolidBrush(_fill);
        g.FillPath(fill, path);

        if (_frameWidth > 0 && _frame.A > 0)
        {
            using var pen = new Pen(_frame, _frameWidth);
            g.DrawPath(pen, path);
        }
    }
}
