using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlPen = VeloxDev.WinForms.Xaml.Pen;
using XamlLinearGradientBrush = VeloxDev.WinForms.Xaml.LinearGradientBrush;

namespace Demo.Views.Sections;

/// <summary>The lightweight visual-tree examples: composition and a connector batch.</summary>
public sealed partial class VisualsSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private readonly Random _random = new(17);
    private ContainerVisual? _sceneGroup;
    private ContainerVisual? _shadowCard;
    private bool _rotated;
    private bool _clipped;
    private int _curveCount = 400;

    /// <summary>Creates the section and builds both visual scenes.</summary>
    public VisualsSection()
    {
        InitializeComponent();
        RebuildScene();
        RebuildConnectors();
        Theme.Changed += OnThemeChanged;
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

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        RebuildScene();
        RebuildConnectors();
    }

    private void RebuildScene()
    {
        var palette = Theme.Current;
        var root = visualHost.Root;
        root.Children.Clear();
        root.Background = new SolidColorBrush(palette.ControlBackground);
        root.CornerRadius = 12f;

        var group = new ContainerVisual
        {
            Bounds = new RectangleF(26f, 20f, 620f, 270f),
            Opacity = opacitySlider.Value / 100d,
            Clip = _clipped ? new RectangleF(0f, 0f, 330f, 150f) : null,
            Transform = _rotated ? Rotation(8f, new PointF(310f, 135f)) : new Matrix(),
        };
        root.Add(group);
        _sceneGroup = group;

        var card = new ContainerVisual
        {
            Bounds = new RectangleF(0f, 0f, 560f, 246f),
            Background = Gradient(palette.Accent, Mix(palette.Accent, Color.Magenta, 0.35f)),
            CornerRadius = 18f,
            Effect = new DropShadowEffect
            {
                Offset = new PointF(6f, 10f),
                Blur = 12f,
                Color = Color.Black,
                Opacity = 0.5d,
            },
        };
        group.Add(card);
        _shadowCard = card;

        card.Add(new ShapeVisual
        {
            Bounds = new RectangleF(28f, 28f, 92f, 92f),
            Geometry = new EllipseGeometry(),
            Fill = new SolidColorBrush(Color.FromArgb(0xDC, 255, 255, 255)),
        });

        card.Add(new ShapeVisual
        {
            Bounds = new RectangleF(138f, 28f, 92f, 92f),
            Geometry = new RectangleGeometry { RadiusX = 20f, RadiusY = 20f },
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            Stroke = new XamlPen(Color.FromArgb(0xCC, 255, 255, 255), 1.5f),
        });

        card.Add(new ShapeVisual
        {
            Bounds = new RectangleF(248f, 22f, 104f, 104f),
            Geometry = Geometry.Parse(
                "M 52 4 L 64 38 L 100 38 L 71 59 L 82 94 L 52 73 L 22 94 L 33 59 L 4 38 L 40 38 Z"),
            Fill = Gradient(Color.FromArgb(0xFF, 0xF2, 0xB4, 0x4B), Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)),
        });

        card.Add(new TextVisual
        {
            Bounds = new RectangleF(28f, 142f, 500f, 40f),
            Text = "Lightweight visual tree",
            Font = GalleryTypography.SectionTitle,
            Foreground = new SolidColorBrush(Color.White),
        });

        card.Add(new TextVisual
        {
            Bounds = new RectangleF(28f, 184f, 500f, 44f),
            Text = "Transform, opacity, clip and a drop shadow on one painted surface.",
            Font = GalleryTypography.Body,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)),
        });

        visualStatus.Text = "1 pass · 0 HWNDs";
        visualHost.InvalidateVisual();
        connectorHost.InvalidateVisual();
    }

    private void RebuildConnectors()
    {
        var root = connectorHost.Root;
        root.Children.Clear();
        root.Background = new SolidColorBrush(Theme.Current.ControlBackground);
        root.CornerRadius = 12f;

        // 三个批次各用一种颜色，既保留单趟绘制的成本，又不会把所有曲线叠成一块纯色。
        var colors = new[]
        {
            Color.FromArgb(0x88, 0x5B, 0x8C, 0xFF),
            Color.FromArgb(0x88, 0x9B, 0x6B, 0xFF),
            Color.FromArgb(0x88, 0xF2, 0xB4, 0x4B),
        };
        var batches = new CurveBatchVisual[colors.Length];
        for (var index = 0; index < batches.Length; index++)
        {
            batches[index] = new CurveBatchVisual
            {
                Bounds = new RectangleF(20f, 12f, 900f, 220f),
                Stroke = new XamlPen(colors[index], 1.3f),
            };
            root.Add(batches[index]);
        }

        for (var index = 0; index < _curveCount; index++)
        {
            var start = new PointF(0f, 60f + (float)_random.NextDouble() * 100f);
            var end = new PointF(880f, 30f + (float)_random.NextDouble() * 170f);
            var lift = 30f + (float)_random.NextDouble() * 60f;
            batches[index % batches.Length].Add(
                start,
                new PointF(220f, start.Y - lift),
                new PointF(640f, end.Y - lift),
                end);
        }

        connectorStatus.Text = $"{_curveCount} curves · {batches.Length} paths · 0 HWNDs";
    }

    private static Matrix Rotation(float degrees, PointF center)
    {
        var matrix = new Matrix();
        matrix.RotateAt(degrees, center);
        return matrix;
    }

    private static Color Mix(Color first, Color second, float amount)
        => Color.FromArgb(
            0xFF,
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));

    private static XamlLinearGradientBrush Gradient(Color start, Color end)
    {
        var brush = new XamlLinearGradientBrush { StartPoint = new PointF(0f, 0f), EndPoint = new PointF(1f, 1f) };
        brush.GradientStops.Add(new GradientStop(start, 0d));
        brush.GradientStops.Add(new GradientStop(end, 1d));
        return brush;
    }

    private void OnOpacityChanged(object? sender, EventArgs e)
    {
        if (_sceneGroup is null)
        {
            return;
        }

        _sceneGroup.Opacity = opacitySlider.Value / 100d;
        visualHost.InvalidateVisual();
    }

    private void OnRotateClick(object? sender, EventArgs e)
    {
        _rotated = !_rotated;
        if (_sceneGroup is not null)
        {
            _sceneGroup.Transform = _rotated ? Rotation(8f, new PointF(310f, 135f)) : new Matrix();
            visualStatus.Text = _rotated ? "rotated · 0 HWNDs" : "1 pass · 0 HWNDs";
            visualHost.InvalidateVisual();
        }
    }

    private void OnClipClick(object? sender, EventArgs e)
    {
        _clipped = !_clipped;
        if (_sceneGroup is not null)
        {
            _sceneGroup.Clip = _clipped ? new RectangleF(0f, 0f, 330f, 150f) : null;
            visualStatus.Text = _clipped ? "clipped · 0 HWNDs" : "1 pass · 0 HWNDs";
            visualHost.InvalidateVisual();
        }
    }

    private void OnShadowClick(object? sender, EventArgs e)
    {
        if (_shadowCard is null)
        {
            return;
        }

        _shadowCard.Effect = _shadowCard.Effect is null
            ? new DropShadowEffect { Offset = new PointF(6f, 10f), Blur = 12f, Color = Color.Black, Opacity = 0.5d }
            : null;
        visualStatus.Text = _shadowCard.Effect is null ? "no shadow · 0 HWNDs" : "shadow · 0 HWNDs";
        visualHost.InvalidateVisual();
    }

    private void OnAddConnectorsClick(object? sender, EventArgs e)
    {
        _curveCount += 200;
        RebuildConnectors();
        connectorHost.InvalidateVisual();
    }
}
