using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>A vector shape painted from a <see cref="Geometry"/>, with an optional fill and stroke.</summary>
public class ShapeVisual : Visual
{
    /// <summary>The geometry painted in local coordinates.</summary>
    public Geometry Geometry { get; set; } = Geometry.Empty;

    /// <summary>The fill brush, or <see langword="null"/> for an unfilled shape.</summary>
    public Brush? Fill { get; set; }

    /// <summary>The stroke pen, or <see langword="null"/> for an unstroked shape.</summary>
    public Pen? Stroke { get; set; }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        if (Geometry is null)
        {
            return;
        }

        using var path = Geometry.ToPath(new RectangleF(0f, 0f, Bounds.Width, Bounds.Height));
        if (path.PointCount == 0)
        {
            return;
        }

        if (Fill is { } fill && fill.ApproximateColor.A > 0)
        {
            fill.FillPath(graphics, path);
        }

        if (Stroke is { } stroke)
        {
            stroke.Draw(graphics, path);
        }
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
    {
        if (Geometry is null)
        {
            return false;
        }

        using var path = Geometry.ToPath(new RectangleF(0f, 0f, Bounds.Width, Bounds.Height));
        return path.PointCount > 0 && path.IsVisible(point);
    }
}

/// <summary>The horizontal alignment of text inside a <see cref="TextVisual"/>.</summary>
public enum VisualTextAlignment
{
    /// <summary>Align to the left edge.</summary>
    Left = 0,

    /// <summary>Centre the line.</summary>
    Center = 1,

    /// <summary>Align to the right edge.</summary>
    Right = 2,
}

/// <summary>A lightweight text visual drawn through GDI+ so transforms and opacity apply to the glyphs.</summary>
/// <remarks>
/// Unlike the HWND-backed <see cref="Controls.TextBlock"/>, this visual uses <see cref="Graphics.DrawString(string, Font, Brush, RectangleF, StringFormat)"/>.
/// That makes it transform-aware and composable, at the cost of GDI+ text metrics instead of GDI text.
/// </remarks>
public class TextVisual : Visual
{
    /// <summary>The text to draw.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The font; the control font by default.</summary>
    public Font Font { get; set; } = System.Windows.Forms.Control.DefaultFont;

    /// <summary>The text brush; white by default.</summary>
    public Brush? Foreground { get; set; } = new SolidColorBrush(Color.White);

    /// <summary>The horizontal line alignment.</summary>
    public VisualTextAlignment TextAlignment { get; set; } = VisualTextAlignment.Left;

    /// <summary>Whether lines wrap inside the visual's bounds.</summary>
    public bool Wrap { get; set; } = true;

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        if (string.IsNullOrEmpty(Text) || Font is null)
        {
            return;
        }

        using var brush = new SolidBrush(Foreground?.ApproximateColor ?? Color.White);
        using var format = new StringFormat
        {
            Alignment = TextAlignment switch
            {
                VisualTextAlignment.Center => StringAlignment.Center,
                VisualTextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = Wrap ? 0 : StringFormatFlags.NoWrap,
        };
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        // 表面按 DPI 做世界缩放，点位字体要先折回 96 DPI，否则会被放大两次；像素字体保持原样。
        graphics.DrawString(
            Text,
            ResolveDrawFont(Font, graphics),
            brush,
            new RectangleF(0f, 0f, Bounds.Width, Bounds.Height),
            format);
    }
}

/// <summary>One cubic segment used by <see cref="CurveBatchVisual"/>.</summary>
public readonly struct VisualCurve
{
    /// <summary>Creates a cubic segment.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    public VisualCurve(PointF start, PointF control1, PointF control2, PointF end)
    {
        Start = start;
        Control1 = control1;
        Control2 = control2;
        End = end;
    }

    /// <summary>The start point.</summary>
    public PointF Start { get; }

    /// <summary>The first control point.</summary>
    public PointF Control1 { get; }

    /// <summary>The second control point.</summary>
    public PointF Control2 { get; }

    /// <summary>The end point.</summary>
    public PointF End { get; }
}

/// <summary>Draws many cubic curves with one <see cref="Pen"/> and one painted pass, without a control per curve.</summary>
public class CurveBatchVisual : Visual
{
    /// <summary>The pen shared by every curve.</summary>
    public Pen Stroke { get; set; } = new(Color.FromArgb(0x88, 0x5B, 0x8C, 0xFF), 1.5f);

    /// <summary>The curves in local coordinates.</summary>
    public IList<VisualCurve> Curves { get; } = new List<VisualCurve>();

    /// <summary>Adds a cubic segment.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    public void Add(PointF start, PointF control1, PointF control2, PointF end)
        => Curves.Add(new VisualCurve(start, control1, control2, end));

    /// <summary>Removes every curve.</summary>
    public void Clear() => Curves.Clear();

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        if (Curves.Count == 0 || Stroke is null)
        {
            return;
        }

        using var path = new GraphicsPath();
        foreach (var curve in Curves)
        {
            path.AddBezier(curve.Start, curve.Control1, curve.Control2, curve.End);
        }

        Stroke.Draw(graphics, path);
    }
}
