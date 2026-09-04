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

namespace TemplateNamespace;

/// <summary>
/// A workflow surface decorator that draws the world grid and floating translucent rulers.
/// Content (the scroll viewer + canvas) fills the whole viewport; the world canvas is
/// translated by <see cref="RulerThickness"/> so the world origin stays at the ruler-band
/// edge while content can still scroll under the translucent bands (the Jalium floating
/// ruler model). The decorator layers, bottom to top: surface + grid, the content child,
/// and the ruler overlay (topmost, hit-test transparent).
///
/// Grid/ruler elements are POOLED and updated in place on scroll rather than rebuilt:
/// a drag frame only rewrites coordinates on existing elements instead of re-creating
/// ~100 XAML Line/TextBlock objects each frame (the pre-pooling cause of drag jank).
/// </summary>
public sealed class TemplateClass : Grid, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly SolidColorBrush SurfaceBackgroundBrush = CreateBrush("TemplateGridBackground");
    private static readonly SolidColorBrush RulerBackgroundBrush = CreateBrush("TemplateRulerBackground");
    private static readonly SolidColorBrush LabelBrush = CreateBrush("TemplateRulerLabelColor");
    private static readonly SolidColorBrush MinorGridBrush = CreateBrush("TemplateMinorGridColor");
    private static readonly SolidColorBrush MajorGridBrush = CreateBrush("TemplateMajorGridColor");
    private static readonly SolidColorBrush AxisBrush = CreateBrush("TemplateAxisColor");
    private static readonly SolidColorBrush TickBrush = CreateBrush("TemplateRulerTickColor");
    private static readonly SolidColorBrush DividerBrush = CreateBrush("TemplateRulerDividerColor");

    private readonly Canvas _contentLayer;
    private readonly Canvas _topRulerLayer;
    private readonly Canvas _leftRulerLayer;
    private readonly Border _contentBackground;
    private readonly Border _topRulerBackground;
    private readonly Border _leftRulerBackground;

    // Pooled visuals: created once, coordinates rewritten in place during pan.
    private readonly List<Line> _gridVertical = [];
    private readonly List<Line> _gridHorizontal = [];
    private readonly List<Line> _topTicks = [];
    private readonly List<Line> _leftTicks = [];
    private readonly List<TextBlock> _topLabels = [];
    private readonly List<TextBlock> _leftLabels = [];
    private readonly List<string> _topLabelText = [];
    private readonly List<string> _leftLabelText = [];

    // Static band dividers (structural only, positioned in ApplyChildLayout).
    private readonly Line _topDivider = new() { Stroke = DividerBrush, StrokeThickness = 1 };
    private readonly Line _leftDivider = new() { Stroke = DividerBrush, StrokeThickness = 1 };

    // Last applied structural values so ApplyChildLayout is a no-op between changes.
    private double _lastWidth = -1;
    private double _lastHeight = -1;
    private double _lastRuler = -1;

    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(28d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(TemplateGridSpacing, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery),
        typeof(int),
        typeof(TemplateClass),
        new PropertyMetadata(TemplateMajorLineEvery, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY),
        typeof(double),
        typeof(TemplateClass),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public TemplateClass()
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

        // Static dividers sit under the pooled ticks/labels (same add-order as before).
        _topRulerLayer.Children.Add(_topDivider);
        _leftRulerLayer.Children.Add(_leftDivider);

        Loaded += (_, _) => RebuildSurface();
        SizeChanged += (_, _) => RebuildSurface();
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
        var decorator = (TemplateClass)d;
        if (e.Property == ScrollOffsetXProperty || e.Property == ScrollOffsetYProperty
            || e.Property == ContentOffsetXProperty || e.Property == ContentOffsetYProperty)
        {
            decorator.UpdateGridAndRulers();
        }
        else
        {
            decorator.RebuildSurface();
        }
    }

    private void ApplyChildLayout()
    {
        var ruler = Math.Max(0, RulerThickness);
        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Guard: only re-apply when a structural value actually changed, so the
        // per-layout geometry churn (and the LayoutUpdated feedback loop) is gone.
        if (Math.Abs(width - _lastWidth) < 0.5
            && Math.Abs(height - _lastHeight) < 0.5
            && Math.Abs(ruler - _lastRuler) < 0.5)
        {
            return;
        }

        _lastWidth = width;
        _lastHeight = height;
        _lastRuler = ruler;

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

        // Static band dividers.
        _topDivider.X1 = ruler; _topDivider.Y1 = ruler - 1;
        _topDivider.X2 = width; _topDivider.Y2 = ruler - 1;
        _leftDivider.X1 = ruler - 1; _leftDivider.Y1 = ruler;
        _leftDivider.X2 = ruler - 1; _leftDivider.Y2 = height;

        Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
    }

    private void RebuildSurface()
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyChildLayout();

        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var spacing = Math.Max(8, GridSpacing);
        var major = Math.Max(1, MajorLineEvery);

        // Reserve enough pooled elements for the current viewport + headroom.
        const int margin = 8;
        EnsurePool(_gridVertical, _contentLayer, (int)Math.Ceiling(width / spacing) + margin);
        EnsurePool(_gridHorizontal, _contentLayer, (int)Math.Ceiling(height / spacing) + margin);
        EnsurePool(_topTicks, _topRulerLayer, (int)Math.Ceiling(width / spacing) + margin);
        EnsurePool(_leftTicks, _leftRulerLayer, (int)Math.Ceiling(height / spacing) + margin);
        EnsureLabelPool(_topLabels, _topLabelText, _topRulerLayer, (int)Math.Ceiling(width / spacing) / major + margin);
        EnsureLabelPool(_leftLabels, _leftLabelText, _leftRulerLayer, (int)Math.Ceiling(height / spacing) / major + margin);

        UpdateGridAndRulers();
    }

    private void UpdateGridAndRulers()
    {
        if (!IsLoaded || _gridVertical.Count == 0)
        {
            return;
        }

        var ruler = Math.Max(0, RulerThickness);
        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
        var worldRight = worldLeft + width;
        var worldBottom = worldTop + height;

        // Vertical grid lines.
        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        var vIndex = 0;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
            var line = _gridVertical[vIndex];
            line.X1 = x; line.Y1 = 0; line.X2 = x; line.Y2 = height;
            var nearZero = IsNearZero(value);
            line.Stroke = nearZero ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            line.StrokeThickness = nearZero ? 1.2 : 1;
            line.Visibility = Visibility.Visible;
            vIndex++;
        }
        HideExcess(_gridVertical, vIndex);

        // Horizontal grid lines.
        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        var hIndex = 0;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = WorkflowSurfaceMath.GridY(value, worldTop, ruler);
            var line = _gridHorizontal[hIndex];
            line.X1 = 0; line.Y1 = y; line.X2 = width; line.Y2 = y;
            var nearZero = IsNearZero(value);
            line.Stroke = nearZero ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            line.StrokeThickness = nearZero ? 1.2 : 1;
            line.Visibility = Visibility.Visible;
            hIndex++;
        }
        HideExcess(_gridHorizontal, hIndex);

        // Top ruler ticks + labels.
        var tIndex = 0;
        var lIndex = 0;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
            if (x < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var nearZero = IsNearZero(value);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var tick = _topTicks[tIndex];
            tick.X1 = x; tick.Y1 = ruler; tick.X2 = x; tick.Y2 = ruler - tickLength;
            tick.Stroke = nearZero ? AxisBrush : TickBrush;
            tick.StrokeThickness = nearZero ? 1.2 : 1;
            tick.Visibility = Visibility.Visible;
            tIndex++;

            if (isMajor)
            {
                var text = FormatGridValue(value);
                if (_topLabelText[lIndex] != text)
                {
                    _topLabelText[lIndex] = text;
                    _topLabels[lIndex].Text = text;
                }
                Canvas.SetLeft(_topLabels[lIndex], x + 3);
                Canvas.SetTop(_topLabels[lIndex], 2);
                _topLabels[lIndex].Visibility = Visibility.Visible;
                lIndex++;
            }
        }
        HideExcess(_topTicks, tIndex);
        HideExcess(_topLabels, lIndex);

        // Left ruler ticks + labels.
        tIndex = 0;
        lIndex = 0;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = WorkflowSurfaceMath.GridY(value, worldTop, ruler);
            if (y < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var nearZero = IsNearZero(value);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var tick = _leftTicks[tIndex];
            tick.X1 = ruler; tick.Y1 = y; tick.X2 = ruler - tickLength; tick.Y2 = y;
            tick.Stroke = nearZero ? AxisBrush : TickBrush;
            tick.StrokeThickness = nearZero ? 1.2 : 1;
            tick.Visibility = Visibility.Visible;
            tIndex++;

            if (isMajor)
            {
                var text = FormatGridValue(value);
                if (_leftLabelText[lIndex] != text)
                {
                    _leftLabelText[lIndex] = text;
                    _leftLabels[lIndex].Text = text;
                }
                Canvas.SetLeft(_leftLabels[lIndex], 3);
                Canvas.SetTop(_leftLabels[lIndex], y + 2);
                _leftLabels[lIndex].Visibility = Visibility.Visible;
                lIndex++;
            }
        }
        HideExcess(_leftTicks, tIndex);
        HideExcess(_leftLabels, lIndex);
    }

    private static void EnsurePool(List<Line> pool, Canvas host, int count)
    {
        while (pool.Count < count)
        {
            var line = new Line { Visibility = Visibility.Collapsed };
            pool.Add(line);
            host.Children.Add(line);
        }
    }

    private static void EnsureLabelPool(List<TextBlock> pool, List<string> cache, Canvas host, int count)
    {
        while (pool.Count < count)
        {
            var label = new TextBlock
            {
                Text = string.Empty,
                Foreground = LabelBrush,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                Visibility = Visibility.Collapsed
            };
            pool.Add(label);
            cache.Add(string.Empty);
            host.Children.Add(label);
        }
    }

    private static void HideExcess<T>(List<T> pool, int activeCount) where T : UIElement
    {
        for (var i = activeCount; i < pool.Count; i++)
        {
            pool[i].Visibility = Visibility.Collapsed;
        }
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
