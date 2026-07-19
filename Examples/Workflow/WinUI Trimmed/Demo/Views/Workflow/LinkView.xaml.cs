// VeloxDev customization: Adjust drawing and hit testing here while preserving the bindable endpoint properties.
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Globalization;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views.Workflow;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove.
/// </summary>
public sealed partial class LinkView : UserControl
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    private readonly Path _path;
    private readonly Path _arrowPath;
    private readonly SolidColorBrush _strokeBrush = new(ParseColor("#DDFFFFFF"));
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly PathGeometry _arrowGeometry = new();
    private readonly PathFigure _arrowFigure = new() { IsClosed = true };
    private readonly LineSegment _arrowLeftSegment = new();
    private readonly LineSegment _arrowRightSegment = new();
    private readonly Point[] _points = new Point[4];
    private bool _updatePending;
    private bool _isLoaded;

    public LinkView()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);

        var container = new Grid();
        _path = new Path { Stroke = _strokeBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        _arrowPath = new Path { Fill = _strokeBrush, IsHitTestVisible = false };
        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
        DataContextChanged += (_, _) =>
        {
            UpdateInteractivity();
            ScheduleUpdate();
        };
        UpdateInteractivity();
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
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.UpdateInteractivity();
        control.ScheduleUpdate();
    }

    private void UpdateInteractivity()
    {
        IsHitTestVisible = !IsVirtualLink;
        IsTabStop = !IsVirtualLink;
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

        if (_arrowFigure.Segments.Count == 0)
        {
            _arrowFigure.Segments.Add(_arrowLeftSegment);
            _arrowFigure.Segments.Add(_arrowRightSegment);
            _arrowGeometry.Figures.Add(_arrowFigure);
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
            _arrowPath.Data = null;
            return;
        }

        BuildPoints();
        var color = IsHighlighted ? Microsoft.UI.Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2;
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

        // Arrowhead
        if (!IsVirtualLink)
        {
            var from = _points[^2];
            var tip = _points[^1];
            double tx = tip.X - from.X, ty = tip.Y - from.Y;
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (len <= 0.001)
            {
                _arrowPath.Data = null;
                return;
            }

            tx /= len;
            ty /= len;
            double nx = -ty, ny = tx;
            double al = 12, aw = 8;
            var baseP = new Point(tip.X - tx * al, tip.Y - ty * al);
            _arrowFigure.StartPoint = tip;
            _arrowLeftSegment.Point = new Point(baseP.X + nx * (aw / 2), baseP.Y + ny * (aw / 2));
            _arrowRightSegment.Point = new Point(baseP.X - nx * (aw / 2), baseP.Y - ny * (aw / 2));
            _arrowPath.Data = _arrowGeometry;
        }
        else
        {
            _arrowPath.Data = null;
        }
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

    #region Interaction

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(FocusState.Pointer); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && IsHighlighted)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;
        BuildPoints();
        for (int i = 0; i < _points.Length - 1; i++)
            if (DistSeg(pt, _points[i], _points[i + 1]) <= hitRadius) return true;
        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        double px = a.X + t * abx - p.X, py = a.Y + t * aby - p.Y;
        return Math.Sqrt(px * px + py * py);
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

    #endregion
}
