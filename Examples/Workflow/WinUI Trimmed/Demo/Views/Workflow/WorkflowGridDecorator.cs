using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using Windows.Foundation;

namespace Demo.Views.Workflow;

/// <summary>
/// A workflow surface decorator that draws the world grid and floating translucent rulers.
/// Content (the scroll viewer + canvas) fills the whole viewport; the world canvas is
/// translated by <see cref="RulerThickness"/> so the world origin stays at the ruler-band
/// edge while content can still scroll under the translucent bands (the Jalium floating
/// ruler model). The decorator layers, bottom to top: surface + grid, the content child,
/// and the ruler overlay (topmost, hit-test transparent).
/// </summary>
public sealed class WorkflowGridDecorator : Grid, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly SolidColorBrush SurfaceBackgroundBrush = CreateBrush("#1E1E1E");
    private static readonly SolidColorBrush RulerBackgroundBrush = CreateBrush("#C8252526");
    private static readonly SolidColorBrush LabelBrush = CreateBrush("#888888");
    private static readonly SolidColorBrush MinorGridBrush = CreateBrush("#2A2D2E");
    private static readonly SolidColorBrush MajorGridBrush = CreateBrush("#3A3D40");
    private static readonly SolidColorBrush AxisBrush = CreateBrush("#4D4D4D");
    private static readonly SolidColorBrush TickBrush = CreateBrush("#555555");
    private static readonly SolidColorBrush DividerBrush = CreateBrush("#3A3D40");

    private readonly Canvas _contentLayer;
    private readonly Canvas _topRulerLayer;
    private readonly Canvas _leftRulerLayer;
    private readonly Border _contentBackground;
    private readonly Border _topRulerBackground;
    private readonly Border _leftRulerBackground;
    private readonly HashSet<UIElement> _internalElements;

    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(28d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(40d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery),
        typeof(int),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(5, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY),
        typeof(double),
        typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public WorkflowGridDecorator()
    {
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) };
        Background = SurfaceBackgroundBrush;

        _contentBackground = new Border
        {
            Background = SurfaceBackgroundBrush,
            IsHitTestVisible = false
        };
        _contentLayer = new Canvas
        {
            IsHitTestVisible = false
        };
        _topRulerBackground = new Border
        {
            Background = RulerBackgroundBrush,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top
        };
        _leftRulerBackground = new Border
        {
            Background = RulerBackgroundBrush,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _topRulerLayer = new Canvas
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top
        };
        _leftRulerLayer = new Canvas
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _internalElements =
        [
            _contentBackground,
            _contentLayer,
            _topRulerBackground,
            _leftRulerBackground,
            _topRulerLayer,
            _leftRulerLayer
        ];

        Children.Add(_contentBackground);
        Children.Add(_contentLayer);
        Children.Add(_topRulerBackground);
        Children.Add(_leftRulerBackground);
        Children.Add(_topRulerLayer);
        Children.Add(_leftRulerLayer);

        // Ruler overlays render above the content child (scroll viewer), so content
        // scrolling under the translucent bands stays visibly dimmed.
        Canvas.SetZIndex(_topRulerBackground, 100);
        Canvas.SetZIndex(_leftRulerBackground, 100);
        Canvas.SetZIndex(_topRulerLayer, 100);
        Canvas.SetZIndex(_leftRulerLayer, 100);

        Loaded += (_, _) => RefreshVisuals();
        SizeChanged += (_, _) => RefreshVisuals();
        LayoutUpdated += (_, _) => ApplyChildLayout();
    }

    public double RulerThickness
    {
        get => (double)GetValue(RulerThicknessProperty);
        set => SetValue(RulerThicknessProperty, value);
    }

    public double GridSpacing
    {
        get => (double)GetValue(GridSpacingProperty);
        set => SetValue(GridSpacingProperty, value);
    }

    public int MajorLineEvery
    {
        get => (int)GetValue(MajorLineEveryProperty);
        set => SetValue(MajorLineEveryProperty, value);
    }

    public double ScrollOffsetX
    {
        get => (double)GetValue(ScrollOffsetXProperty);
        set => SetValue(ScrollOffsetXProperty, value);
    }

    public double ScrollOffsetY
    {
        get => (double)GetValue(ScrollOffsetYProperty);
        set => SetValue(ScrollOffsetYProperty, value);
    }

    public double ContentOffsetX
    {
        get => (double)GetValue(ContentOffsetXProperty);
        set => SetValue(ContentOffsetXProperty, value);
    }

    public double ContentOffsetY
    {
        get => (double)GetValue(ContentOffsetYProperty);
        set => SetValue(ContentOffsetYProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WorkflowGridDecorator)d).RefreshVisuals();
    }

    private void ApplyChildLayout()
    {
        var ruler = Math.Max(0, RulerThickness);
        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);

        // Grid and surface span the full viewport so the grid extends under the ruler bands.
        _contentBackground.Width = width;
        _contentBackground.Height = height;
        _contentBackground.Margin = new Thickness(0);

        _contentLayer.Width = width;
        _contentLayer.Height = height;
        _contentLayer.Margin = new Thickness(0);
        _contentLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };

        _topRulerBackground.Height = ruler;
        _topRulerBackground.Width = width;
        _leftRulerBackground.Width = ruler;
        _leftRulerBackground.Height = height;

        _topRulerLayer.Margin = new Thickness(0);
        _topRulerLayer.Width = width;
        _topRulerLayer.Height = ruler;
        _topRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, ruler) };

        _leftRulerLayer.Margin = new Thickness(0);
        _leftRulerLayer.Width = ruler;
        _leftRulerLayer.Height = height;
        _leftRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ruler, height) };

        Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
    }

    private void RefreshVisuals()
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyChildLayout();

        _contentLayer.Children.Clear();
        _topRulerLayer.Children.Clear();
        _leftRulerLayer.Children.Clear();

        var ruler = Math.Max(0, RulerThickness);
        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawGrid(width, height, ruler);
        DrawRulers(width, height, ruler);
    }

    private void DrawGrid(double width, double height, double ruler)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + width;
        var worldBottom = worldTop + height;

        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = ruler + (value - worldLeft);
            var brush = IsNearZero(value) ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            AddLine(_contentLayer, x, 0, x, height, brush, IsNearZero(value) ? 1.2 : 1);
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = ruler + (value - worldTop);
            var brush = IsNearZero(value) ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            AddLine(_contentLayer, 0, y, width, y, brush, IsNearZero(value) ? 1.2 : 1);
        }
    }

    private void DrawRulers(double width, double height, double ruler)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + width;
        var worldBottom = worldTop + height;

        // Dividers stop at the perpendicular band (they never cross the corner junction).
        AddLine(_topRulerLayer, ruler, ruler - 1, width, ruler - 1, DividerBrush, 1);
        AddLine(_leftRulerLayer, ruler - 1, ruler, ruler - 1, height, DividerBrush, 1);

        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = ruler + (value - worldLeft);
            if (x < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            AddLine(_topRulerLayer, x, ruler, x, ruler - tickLength, brush, IsNearZero(value) ? 1.2 : 1);

            if (isMajor)
            {
                AddLabel(_topRulerLayer, value, x + 3, 2);
            }
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = ruler + (value - worldTop);
            if (y < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            AddLine(_leftRulerLayer, ruler, y, ruler - tickLength, y, brush, IsNearZero(value) ? 1.2 : 1);

            if (isMajor)
            {
                AddLabel(_leftRulerLayer, value, 3, y + 2);
            }
        }
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness
        });
    }

    private static void AddLabel(Canvas canvas, double value, double left, double top)
    {
        var label = new TextBlock
        {
            Text = FormatGridValue(value),
            Foreground = LabelBrush,
            FontSize = 10,
            FontWeight = FontWeights.Normal
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        byte a = 0xFF;
        int index = 0;
        if (hex.Length == 8)
        {
            a = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            index = 2;
        }

        var r = byte.Parse(hex.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.Substring(index + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.Substring(index + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private static bool IsMajorLine(double value, double majorStep)
    {
        if (majorStep <= 0)
        {
            return false;
        }

        var remainder = value % majorStep;
        return Math.Abs(remainder) < MajorLineEpsilon
            || Math.Abs(remainder - majorStep) < MajorLineEpsilon
            || Math.Abs(remainder + majorStep) < MajorLineEpsilon;
    }

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;
}
