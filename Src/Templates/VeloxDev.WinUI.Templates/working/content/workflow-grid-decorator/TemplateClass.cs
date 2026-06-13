using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using Windows.Foundation;

namespace TemplateNamespace;

public sealed class TemplateClass : Grid, IWorkflowGridDecorator
{
    private static readonly SolidColorBrush BackgroundBrush = CreateBrush("TemplateGridBackground");
    private static readonly SolidColorBrush MinorGridBrush = CreateBrush("TemplateMinorGridColor");
    private static readonly SolidColorBrush MajorGridBrush = CreateBrush("TemplateMajorGridColor");
    private static readonly SolidColorBrush AxisBrush = CreateBrush("TemplateAxisColor");
    private readonly Canvas _gridLayer = new() { IsHitTestVisible = false };

    public static readonly DependencyProperty GridSpacingProperty = RegisterProperty(nameof(GridSpacing), TemplateGridSpacing);
    public static readonly DependencyProperty MajorLineEveryProperty = RegisterProperty(nameof(MajorLineEvery), TemplateMajorLineEvery);
    public static readonly DependencyProperty ScrollOffsetXProperty = RegisterProperty(nameof(ScrollOffsetX), 0d);
    public static readonly DependencyProperty ScrollOffsetYProperty = RegisterProperty(nameof(ScrollOffsetY), 0d);
    public static readonly DependencyProperty ContentOffsetXProperty = RegisterProperty(nameof(ContentOffsetX), 0d);
    public static readonly DependencyProperty ContentOffsetYProperty = RegisterProperty(nameof(ContentOffsetY), 0d);

    public TemplateClass()
    {
        Background = BackgroundBrush;
        Children.Add(_gridLayer);
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
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

    private void Redraw()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _gridLayer.Children.Clear();
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;

        for (var value = Math.Floor(worldLeft / spacing) * spacing;
             value <= worldLeft + ActualWidth + spacing;
             value += spacing)
        {
            var x = value - worldLeft;
            AddLine(x, 0, x, ActualHeight, SelectBrush(value, majorStep));
        }

        for (var value = Math.Floor(worldTop / spacing) * spacing;
             value <= worldTop + ActualHeight + spacing;
             value += spacing)
        {
            var y = value - worldTop;
            AddLine(0, y, ActualWidth, y, SelectBrush(value, majorStep));
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke)
        => _gridLayer.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = ReferenceEquals(stroke, AxisBrush) ? 1.2 : 1
        });

    private static DependencyProperty RegisterProperty(string name, object defaultValue)
        => DependencyProperty.Register(
            name,
            defaultValue.GetType(),
            typeof(TemplateClass),
            new PropertyMetadata(defaultValue, OnVisualPropertyChanged));

    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TemplateClass)sender).Redraw();

    private static Brush SelectBrush(double value, double majorStep)
        => Math.Abs(value) < 0.001
            ? AxisBrush
            : Math.Abs(value % majorStep) < 0.001
                ? MajorGridBrush
                : MinorGridBrush;

    private static SolidColorBrush CreateBrush(string hex)
    {
        hex = hex.TrimStart('#');
        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(hex.Length == 8
            ? Windows.UI.Color.FromArgb(
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value)
            : Windows.UI.Color.FromArgb(
                0xFF,
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value));
    }
}
