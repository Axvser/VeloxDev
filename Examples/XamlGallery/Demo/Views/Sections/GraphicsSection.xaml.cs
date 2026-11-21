using System.Drawing;
using System.Drawing.Drawing2D;

namespace Demo.Views.Sections;

/// <summary>Demonstrates the vector shapes and gradient brushes.</summary>
public sealed partial class GraphicsSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section and runs the generated initializer.</summary>
    public GraphicsSection()
    {
        InitializeComponent();
    }
}

/// <summary>Point lists shared by the polygon and polyline examples.</summary>
public static class ShapePoints
{
    /// <summary>A small upward triangle.</summary>
    public static PointF[] Triangle { get; } = [new(45f, 4f), new(86f, 76f), new(4f, 76f)];

    /// <summary>A six-point zig-zag line.</summary>
    public static PointF[] ZigZag { get; } =
        [new(4f, 70f), new(24f, 30f), new(44f, 58f), new(64f, 18f), new(84f, 50f), new(96f, 20f)];

    /// <summary>A tall triangle used for the stroked outline.</summary>
    public static PointF[] TallTriangle { get; } = [new(60f, 4f), new(116f, 90f), new(4f, 90f)];
}

/// <summary>A generated bitmap shown through <c>Image</c>.</summary>
public static class GalleryArt
{
    /// <summary>A small gradient orb with a highlight and a ring.</summary>
    public static Bitmap Orb { get; } = CreateOrb();

    private static Bitmap CreateOrb()
    {
        var bitmap = new Bitmap(160, 160);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(16f, 16f, 128f, 128f);
        using (var body = new LinearGradientBrush(bounds, Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF), Color.FromArgb(0xFF, 0x9B, 0x6B, 0xFF), 45f))
        {
            graphics.FillEllipse(body, bounds);
        }

        using (var highlight = new SolidBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)))
        {
            graphics.FillEllipse(highlight, 42f, 34f, 44f, 30f);
        }

        using (var ring = new Pen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 3f))
        {
            graphics.DrawEllipse(ring, bounds);
        }

        return bitmap;
    }
}
