using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using Windows.Foundation;

namespace TemplateNamespace;

public sealed class TemplateClass : Grid, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly SolidColorBrush BackgroundBrush = CreateBrush("TemplateGridBackground");
    private static readonly SolidColorBrush RulerBackgroundBrush = CreateBrush("TemplateRulerBackground");
    private static readonly SolidColorBrush LabelBrush = CreateBrush("TemplateRulerLabelColor");
    private static readonly SolidColorBrush MinorGridBrush = CreateBrush("TemplateMinorGridColor");
    private static readonly SolidColorBrush MajorGridBrush = CreateBrush("TemplateMajorGridColor");
    private static readonly SolidColorBrush AxisBrush = CreateBrush("TemplateAxisColor");
    private static readonly SolidColorBrush TickBrush = CreateBrush("TemplateRulerTickColor");
    private static readonly SolidColorBrush DividerBrush = CreateBrush("TemplateRulerDividerColor");

    private readonly Canvas _contentLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _topRulerLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _leftRulerLayer = new() { IsHitTestVisible = false };
    private readonly Border _contentBackground = new() { IsHitTestVisible = false };
    private readonly Border _topRulerBackground = new() { IsHitTestVisible = false };
    private readonly Border _leftRulerBackground = new() { IsHitTestVisible = false };

    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(28d, OnVisualPropertyChanged));

    public static readonly DependencyProperty GridSpacingProperty = RegisterProperty(
        nameof(GridSpacing), TemplateGridSpacing);

    public static readonly DependencyProperty MajorLineEveryProperty = RegisterProperty(
        nameof(MajorLineEvery), TemplateMajorLineEvery);

    public static readonly DependencyProperty ScrollOffsetXProperty = RegisterProperty(nameof(ScrollOffsetX), 0d);
    public static readonly DependencyProperty ScrollOffsetYProperty = RegisterProperty(nameof(ScrollOffsetY), 0d);
    public static readonly DependencyProperty ContentOffsetXProperty = RegisterProperty(nameof(ContentOffsetX), 0d);
    public static readonly DependencyProperty ContentOffsetYProperty = RegisterProperty(nameof(ContentOffsetY), 0d);

    public TemplateClass()
    {
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) };
        Background = BackgroundBrush;

        _contentBackground.Background = BackgroundBrush;
        _topRulerBackground.Background = RulerBackgroundBrush;
        _topRulerBackground.VerticalAlignment = VerticalAlignment.Top;
        _leftRulerBackground.Background = RulerBackgroundBrush;
        _leftRulerBackground.HorizontalAlignment = HorizontalAlignment.Left;

        Children.Add(_contentBackground);
        Children.Add(_contentLayer);
        Children.Add(_topRulerBackground);
        Children.Add(_leftRulerBackground);
        Children.Add(_topRulerLayer);
        Children.Add(_leftRulerLayer);

        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
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

        _topRulerBackground.Height = ruler;
        _topRulerLayer.Height = ruler;
        _topRulerLayer.Margin = new Thickness(ruler, 0, 0, 0);
        _topRulerLayer.Width = contentWidth;
        _topRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, contentWidth, ruler) };

        _leftRulerBackground.Width = ruler;
        _leftRulerLayer.Width = ruler;
        _leftRulerLayer.Margin = new Thickness(0, ruler, 0, 0);
        _leftRulerLayer.Height = contentHeight;
        _leftRulerLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ruler, contentHeight) };
    }

    private void Redraw()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        ApplyChildLayout();

        var ruler = Math.Max(0, RulerThickness);
        var contentWidth = Math.Max(0, ActualWidth - ruler);
        var contentHeight = Math.Max(0, ActualHeight - ruler);

        _contentLayer.Children.Clear();
        _topRulerLayer.Children.Clear();
        _leftRulerLayer.Children.Clear();

        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + contentWidth;
        var worldBottom = worldTop + contentHeight;

        // Grid lines (clipped to content area)
        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = value - worldLeft;
            AddLine(_contentLayer, x, 0, x, contentHeight, SelectBrush(value, majorStep),
                IsNearZero(value) ? 1.2 : 1);
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = value - worldTop;
            AddLine(_contentLayer, 0, y, contentWidth, y, SelectBrush(value, majorStep),
                IsNearZero(value) ? 1.2 : 1);
        }

        // Divider lines
        AddLine(this, ruler, 0, ruler, ActualHeight, DividerBrush, 1);
        AddLine(this, 0, ruler, ActualWidth, ruler, DividerBrush, 1);

        // Top ruler ticks and labels
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = value - worldLeft;
            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            var thickness = IsNearZero(value) ? 1.2 : 1;
            AddLine(_topRulerLayer, x, ruler, x, ruler - tickLength, brush, thickness);

            if (isMajor)
            {
                AddLabel(_topRulerLayer, value, x + 3, 2);
            }
        }

        // Left ruler ticks and labels
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = value - worldTop;
            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
            var brush = IsNearZero(value) ? AxisBrush : TickBrush;
            var thickness = IsNearZero(value) ? 1.2 : 1;
            AddLine(_leftRulerLayer, ruler, y, ruler - tickLength, y, brush, thickness);

            if (isMajor)
            {
                AddLabel(_leftRulerLayer, value, 3, y + 2);
            }
        }
    }

    private static void AddLine(Panel parent, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
        => parent.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness
        });

    private static void AddLabel(Panel parent, double value, double x, double y)
    {
        parent.Children.Add(new TextBlock
        {
            Text = FormatGridValue(value),
            FontSize = 10,
            Foreground = LabelBrush,
            Margin = new Thickness(x, y, 0, 0)
        });
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

    private static DependencyProperty RegisterProperty(string name, object defaultValue)
        => DependencyProperty.Register(
            name, defaultValue.GetType(), typeof(TemplateClass),
            new PropertyMetadata(defaultValue, OnVisualPropertyChanged));

    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TemplateClass)sender).Redraw();

    private static Brush SelectBrush(double value, double majorStep)
        => IsNearZero(value)
            ? AxisBrush
            : IsMajorLine(value, majorStep) ? MajorGridBrush : MinorGridBrush;

    private static bool IsMajorLine(double value, double majorStep)
        => majorStep > 0 && (Math.Abs(value % majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep - majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep + majorStep) < MajorLineEpsilon);

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;

    private static SolidColorBrush CreateBrush(string hex)
    {
        hex = hex.TrimStart('#');
        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(hex.Length == 8
            ? Windows.UI.Color.FromArgb(
                (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Windows.UI.Color.FromArgb(
                0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    }
}
