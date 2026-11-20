using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WinForms.Xaml.Controls;

// 自绘控件共用的小工具：圆角路径与「底色 + 描边」的单一入口，避免每个控件各写一份 GDI+ 逻辑。
internal static class XamlDrawing
{
    // 原生子窗口不认 alpha：透明落到 fallback，半透明取不透明部分。
    public static Color ToOpaque(Color color, Color fallback)
    {
        if (color.A == 0)
        {
            return fallback;
        }

        return color.A == 255 ? color : Color.FromArgb(255, color.R, color.G, color.B);
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        => RoundedRectangle(bounds, radius, radius);

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radiusX, float radiusY)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return path;
        }

        var rx = Math.Min(Math.Max(0f, radiusX), bounds.Width / 2f);
        var ry = Math.Min(Math.Max(0f, radiusY), bounds.Height / 2f);
        if (rx <= 0f || ry <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameterX = 2f * rx;
        var diameterY = 2f * ry;
        path.AddArc(bounds.X, bounds.Y, diameterX, diameterY, 180f, 90f);
        path.AddArc(bounds.Right - diameterX, bounds.Y, diameterX, diameterY, 270f, 90f);
        path.AddArc(bounds.Right - diameterX, bounds.Bottom - diameterY, diameterX, diameterY, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameterY, diameterX, diameterY, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    public static void PaintChrome(
        Graphics graphics,
        Rectangle bounds,
        Color background,
        Color borderBrush,
        Thickness borderThickness,
        float cornerRadius)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new RectangleF(0.5f, 0.5f, Math.Max(0, bounds.Width - 1f), Math.Max(0, bounds.Height - 1f));
        if (body.Width <= 0f || body.Height <= 0f)
        {
            return;
        }

        using var path = RoundedRectangle(body, cornerRadius);
        if (background.A > 0)
        {
            using var brush = new SolidBrush(background);
            graphics.FillPath(brush, path);
        }

        var thickness = Math.Min(
            Math.Max(Math.Max(borderThickness.Left, borderThickness.Right), Math.Max(borderThickness.Top, borderThickness.Bottom)),
            Math.Min(body.Width, body.Height) / 2f);
        if (borderBrush.A > 0 && thickness > 0f)
        {
            using var pen = new Pen(borderBrush, thickness);
            graphics.DrawPath(pen, path);
        }
    }
}
