using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using Windows.Foundation;

namespace Demo.Views;

public sealed class WorkflowGridDecorator : Grid, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly SolidColorBrush SurfaceBackgroundBrush = CreateBrush("#141922");
    private static readonly SolidColorBrush RulerBackgroundBrush = CreateBrush("#1C2330");
    private static readonly SolidColorBrush LabelBrush = CreateBrush("#94A3B8");
    private static readonly SolidColorBrush MinorGridBrush = CreateBrush("#223043");
    private static readonly SolidColorBrush MajorGridBrush = CreateBrush("#31445C");
    private static readonly SolidColorBrush AxisBrush = CreateBrush("#38BDF8");
    private static readonly SolidColorBrush TickBrush = CreateBrush("#64748B");
    private static readonly SolidColorBrush DividerBrush = CreateBrush("#475569");

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
        var contentWidth = Math.Max(0, ActualWidth - ruler);
        var contentHeight = Math.Max(0, ActualHeight - ruler);
        var contentMargin = new Thickness(ruler, ruler, 0, 0);

        _contentBackground.Margin = contentMargin;
        _contentBackground.Width = contentWidth;
        _contentBackground.Height = contentHeight;

        _contentLayer.Margin = contentMargin;
        _contentLayer.Width = contentWidth;
        _contentLayer.Height = contentHeight;
        _contentLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, contentWidth, contentHeight) };

        _topRulerBackground.Height = ruler;
        _topRulerBackground.Width = ActualWidth;
        _leftRulerBackground.Width = ruler;
        _leftRulerBackground.Height = ActualHeight;

        _topRulerLayer.Margin = new Thickness(ruler, 0, 0, 0);
        _topRulerLayer.Width = contentWidth;
        _topRulerLayer.Height = ruler;
        _topRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, contentWidth, ruler) };

        _leftRulerLayer.Margin = new Thickness(0, ruler, 0, 0);
        _leftRulerLayer.Width = ruler;
        _leftRulerLayer.Height = contentHeight;
        _leftRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ruler, contentHeight) };
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };

        foreach (var child in Children)
        {
            if (!_internalElements.Contains(child) && child is FrameworkElement element)
            {
                element.Margin = contentMargin;
            }
        }
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
        var contentWidth = Math.Max(0, ActualWidth - ruler);
        var contentHeight = Math.Max(0, ActualHeight - ruler);
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        DrawGrid(contentWidth, contentHeight);
        DrawRulers(contentWidth, contentHeight, ruler);
    }

    private void DrawGrid(double contentWidth, double contentHeight)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + contentWidth;
        var worldBottom = worldTop + contentHeight;

        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = value - worldLeft;
            var brush = IsNearZero(value) ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            AddLine(_contentLayer, x, 0, x, contentHeight, brush, IsNearZero(value) ? 1.2 : 1);
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = value - worldTop;
            var brush = IsNearZero(value) ? AxisBrush : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;
            AddLine(_contentLayer, 0, y, contentWidth, y, brush, IsNearZero(value) ? 1.2 : 1);
        }
    }

    private void DrawRulers(double contentWidth, double contentHeight, double ruler)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + contentWidth;
        var worldBottom = worldTop + contentHeight;

        AddLine(_topRulerLayer, 0, ruler - 1, contentWidth, ruler - 1, DividerBrush, 1);
        AddLine(_leftRulerLayer, ruler - 1, 0, ruler - 1, contentHeight, DividerBrush, 1);

        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = value - worldLeft;
            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            AddLine(_topRulerLayer, x, ruler, x, ruler - tickLength, brush, IsNearZero(value) ? 1.2 : 1);

            if (isMajor)
            {
                AddLabel(_topRulerLayer, Math.Round(value).ToString(CultureInfo.InvariantCulture), x + 3, 2);
            }
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = value - worldTop;
            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            AddLine(_leftRulerLayer, ruler, y, ruler - tickLength, y, brush, IsNearZero(value) ? 1.2 : 1);

            if (isMajor)
            {
                AddLabel(_leftRulerLayer, Math.Round(value).ToString(CultureInfo.InvariantCulture), 3, y + 2);
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

    private static void AddLabel(Canvas canvas, string text, double left, double top)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = LabelBrush,
            FontSize = 10,
            FontWeight = FontWeights.Normal
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
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
