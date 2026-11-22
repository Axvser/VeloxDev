using System.Drawing;
using System.Drawing.Text;

namespace VeloxDev.WinForms.Xaml.Visuals;

// GDI TextRenderer 直接写 HDC，不认 GDI+ 的 world transform；视觉树的平移全靠 Graphics.Transform，
// 所以这里的文字必须走 DrawString。测量也走同一个 Graphics，字符宽度才会和绘制一致（TextBox 的插入符依赖它）。
internal static class VisualText
{
    private static readonly Dictionary<int, Graphics> MeasureContexts = [];

    private static Graphics MeasureContext(int dpi)
    {
        if (MeasureContexts.TryGetValue(dpi, out var graphics))
        {
            return graphics;
        }

        var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(dpi, dpi);
        graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        MeasureContexts[dpi] = graphics;
        return graphics;
    }

    public static SizeF Measure(string text, Font font, float maxWidth, bool wrap, int dpi)
    {
        using var format = CreateFormat(wrap, StringAlignment.Near, StringAlignment.Near);
        var width = maxWidth > 0f ? maxWidth : 1_000_000f;
        var measured = MeasureContext(dpi).MeasureString(text, font, new SizeF(width, 1_000_000f), format);
        return new SizeF((float)Math.Ceiling(measured.Width), (float)Math.Ceiling(measured.Height));
    }

    public static float MeasureWidth(string text, Font font, int dpi)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.None,
        };
        return MeasureContext(dpi).MeasureString(text, font, PointF.Empty, format).Width;
    }

    public static void Draw(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        RectangleF bounds,
        bool wrap,
        StringAlignment horizontal,
        StringAlignment vertical)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        using var format = CreateFormat(wrap, horizontal, vertical);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static StringFormat CreateFormat(bool wrap, StringAlignment horizontal, StringAlignment vertical)
        => new()
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = wrap ? StringTrimming.None : StringTrimming.EllipsisCharacter,
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap,
        };
}
