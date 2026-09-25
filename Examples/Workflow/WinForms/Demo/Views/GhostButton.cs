using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Demo.Views;

/// <summary>
/// The card's action button: a same-shaped "ghost" — transparent ground, hairline border, rounded corner —
/// whose meaning is carried by its text colour alone.
///
/// A WinForms <see cref="Button"/> cannot do this: <c>FlatStyle.Flat</c> outlines in
/// <c>FlatAppearance.BorderColor</c> but paints square corners and an opaque background, and the disabled state
/// is the theme's solid grey slab, which stands out badly in a row of ghosts. The whole face is therefore
/// painted here: hover lifts the ground and the border, and disabling only dims the border and the text —
/// the shape never changes.
/// </summary>
internal sealed class GhostButton : Button
{
    private Color _textColor = CardTheme.Value;
    private float _radius = CardTheme.FieldRadius;
    private float _frameWidth = 1f;
    private bool _hover;

    public GhostButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = CardTheme.Surface;
        Font = CardTheme.Font(CardTheme.ButtonSize);
        ForeColor = CardTheme.Value;
    }

    /// <summary>The button's semantic colour. Hover and disabled never change the hue, only what is done to it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
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
    public float FrameWidth
    {
        get => _frameWidth;
        set { _frameWidth = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // AllPaintingInWmPaint means nothing has erased this control yet, and the ghost's ground is the card's
        // surface: filling with it is exactly what "transparent" would have looked like here.
        using (var ground = new SolidBrush(CardTheme.Surface))
        {
            g.FillRectangle(ground, ClientRectangle);
        }

        var inset = _frameWidth > 0 ? _frameWidth / 2f : 0f;
        var rect = new RectangleF(inset, inset, Width - (_frameWidth > 0 ? _frameWidth : 0), Height - (_frameWidth > 0 ? _frameWidth : 0));
        using var path = CardTheme.RoundedPath(rect, _radius);

        var fill = Enabled && _hover ? CardTheme.Hover : CardTheme.Surface;
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        var frame = !Enabled ? CardTheme.DisabledBorder : _hover ? CardTheme.HoverBorder : CardTheme.Border;
        if (_frameWidth > 0)
        {
            using var pen = new Pen(frame, _frameWidth);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            Enabled ? _textColor : CardTheme.DisabledText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
