// VeloxDev customization: Customize line geometry, color, and thickness here.
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Globalization;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views.Workflow;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public sealed partial class LinkView : UserControl
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    private readonly Path _path;
    private readonly SolidColorBrush _strokeBrush = new(ParseColor("#DDFFFFFF"));
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly Point[] _points = new Point[4];
    private bool _updatePending;
    private bool _isLoaded;

    public LinkView()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);
        IsHitTestVisible = false;

        var container = new Grid();
        _path = new Path { Stroke = _strokeBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        container.Children.Add(_path);
        this.Content = container;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => ScheduleUpdate();
        ScheduleUpdate();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(LinkView), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(LinkView), new PropertyMetadata(ParseColor("#DDFFFFFF"), OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Synchronous redraw: the endpoint DPs are bound to measured slot anchors which are written
        // in-frame (synchronously on LayoutUpdated), so redraw now or the link lags a frame and
        // jitters while zooming.
        ((LinkView)d).UpdatePath();
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        EnsureGeometry();
        ScheduleUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _updatePending = false;
    }

    private void EnsureGeometry()
    {
        while (_pathFigure.Segments.Count < _points.Length - 1)
        {
            _pathFigure.Segments.Add(new LineSegment());
        }

        if (_pathGeometry.Figures.Count == 0)
        {
            _pathGeometry.Figures.Add(_pathFigure);
        }
    }

    private void ScheduleUpdate()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _updatePending = false;
            if (_isLoaded)
            {
                UpdatePath();
            }
        });
    }

    private void UpdatePath()
    {
        EnsureGeometry();

        if (!CanRender)
        {
            _path.Data = null;
            return;
        }

        BuildPoints();
        var color = LineColor;
        var thickness = 2;
        _strokeBrush.Color = color;
        _path.StrokeThickness = thickness;

        if (IsVirtualLink)
            _path.StrokeDashArray = VirtualStrokeDashArray;
        else
            _path.StrokeDashArray = null;

        _pathFigure.StartPoint = _points[0];
        for (int i = 1; i < _points.Length; i++)
        {
            ((LineSegment)_pathFigure.Segments[i - 1]).Point = _points[i];
        }

        _path.Data = _pathGeometry;
    }

    private void BuildPoints()
    {
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        _points[0] = new Point(StartLeft, StartTop);
        _points[1] = new Point(StartLeft + stub, StartTop);
        _points[2] = new Point(EndLeft - stub, EndTop);
        _points[3] = new Point(EndLeft, EndTop);
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 8
            ? Windows.UI.Color.FromArgb(
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value)
            : Windows.UI.Color.FromArgb(
                0xFF,
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value);
    }
}
