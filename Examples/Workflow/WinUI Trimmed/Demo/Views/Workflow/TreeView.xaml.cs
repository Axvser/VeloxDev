using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Linq;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views.Workflow;

/// <summary>
/// TEMP diagnostic probe (VeloxDev deep-zoom dangle investigation). Passive: samples, on LayoutUpdated
/// and a 250 ms timer, every realized node/link and logs to %TEMP%\veloxdev_winui_drift.log lines where
/// the drawn link endpoint, the stored slot.Anchor, and the port slot view's live canvas-local center
/// disagree, plus node-placement freshness (realized Canvas.Left/ActualWidth vs collapsed Anchor/Size).
/// Does not mutate model or visuals.
/// </summary>
public sealed partial class TreeView : UserControl
{
    private static readonly string s_logPath = Path.Combine(Path.GetTempPath(), "veloxdev_winui_drift.log");

    private DispatcherTimer? _timer;
    private DateTime _lastLogUtc = DateTime.MinValue;
    private DateTime _lastSizesUtc = DateTime.MinValue;

    public TreeView()
    {
        InitializeComponent();
        Loaded += OnProbeLoaded;
        Unloaded += OnProbeUnloaded;
    }

    private void OnProbeLoaded(object sender, RoutedEventArgs e)
    {
        LogLine("=== probe attached ===");
        PART_Canvas.LayoutUpdated += OnProbeLayout;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        Sample();
    }

    private void OnProbeUnloaded(object sender, RoutedEventArgs e)
    {
        PART_Canvas.LayoutUpdated -= OnProbeLayout;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private void OnProbeLayout(object? sender, object e) => Sample();

    private static void LogLine(string line)
    {
        try
        {
            File.AppendAllText(s_logPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void Sample()
    {
        try
        {
            if (PART_Canvas.DataContext is not IWorkflowTreeViewModel tree)
            {
                return;
            }

            var layout = tree.Layout;
            double scale = layout.Scale.Horizontal;

            // Throttle: at most one row per 150 ms unless a drift is found (drift rows bypass).
            var now = DateTime.UtcNow;
            var allow = (now - _lastLogUtc).TotalMilliseconds >= 150;

            foreach (var child in PART_Canvas.Children.OfType<FrameworkElement>())
            {
                if (child.DataContext is IWorkflowLinkViewModel link)
                {
                    var row = SampleLink(child, link, scale, layout);
                    if (row is not null && (allow || row.Contains("DRIFT")))
                    {
                        LogLine(row);
                        _lastLogUtc = now;
                    }
                }
                else if (child.DataContext is IWorkflowNodeViewModel node)
                {
                    var row = SampleNode(child, node, scale);
                    if (row is not null && (allow || row.Contains("DRIFT")))
                    {
                        LogLine(row);
                        _lastLogUtc = now;
                    }
                }
            }
        SampleSizes(layout);
        }
        catch (Exception ex)
        {
            LogLine($"!probe-ex {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string? SampleLink(FrameworkElement linkView, IWorkflowLinkViewModel link, double scale, CanvasLayout layout)
    {
        var lv = linkView as LinkView;
        if (lv is null)
        {
            return null;
        }

        double sx = lv.StartLeft, sy = lv.StartTop, ex = lv.EndLeft, ey = lv.EndTop;
        var parts = new[]
        {
            ("start", link.Sender, sx, sy),
            ("end", link.Receiver, ex, ey),
        };

        foreach (var (tag, slot, drawnX, drawnY) in parts)
        {
            if (slot?.Parent is not IWorkflowNodeViewModel node)
            {
                continue;
            }

            var nodeView = PART_Canvas.Children
                .OfType<FrameworkElement>()
                .FirstOrDefault(c => ReferenceEquals(c.DataContext, node));
            if (nodeView is null)
            {
                continue;
            }

            var slotView = FindSlotView(nodeView, slot);
            if (slotView is null || slotView.ActualWidth <= 0)
            {
                continue;
            }

            var center = CenterToCanvas(slotView, PART_Canvas);
            double ddx = center.X - drawnX;
            double ddy = center.Y - drawnY;
            double dStorage = Math.Abs(center.X - slot.Anchor.Horizontal) + Math.Abs(center.Y - slot.Anchor.Vertical);
            var nodeId = $"n({node.Anchor.Horizontal:0},{node.Anchor.Vertical:0})";
            if (Math.Abs(ddx) + Math.Abs(ddy) > 1.0)
            {
                return $"DRIFT scale={scale:F3} link@{nodeId}[{tag}] drawn=({drawnX:F1},{drawnY:F1}) livePort=({center.X:F1},{center.Y:F1}) d=({ddx:F1},{ddy:F1}) storedD={dStorage:F1} anchorStored=({slot.Anchor.Horizontal:F1},{slot.Anchor.Vertical:F1}) off=({layout.ActualOffset.Horizontal:F0},{layout.ActualOffset.Vertical:F0})";
            }

            if (Math.Abs(dStorage) > 1.0)
            {
                    return $"STOREDDRIFT scale={scale:F3} {nodeId}[{tag}] livePort=({center.X:F1},{center.Y:F1}) anchorStored=({slot.Anchor.Horizontal:F1},{slot.Anchor.Vertical:F1}) d={dStorage:F1}";
            }
        }

        return null;
    }

    /// <summary>Size/shape audit: does each realized link's polyline geometry stay inside ITS OWN element
    /// box? Since the offset-frame fix, LinkView places itself at −ActualOffset and bakes +ActualOffset
    /// into the geometry, so the clip-relevant quantity is the LOCAL geometry (raw DP + frame offset)
    /// against the [0..ActualWidth] element box — collapse-zoom pushes raw left/top-quadrant ports to
    /// NEGATIVE canvas-local, which used to sit outside the old (0,0)-anchored box by construction and
    /// is exactly where a WinUI element-bounds clip cut the line. Emits an OUT flag whenever a link's
    /// local geometry leaves its box, plus a throttled frame-shape row so a clean run is visible too.</summary>
    private void SampleSizes(CanvasLayout layout)
    {
        double scale = layout.Scale.Horizontal;
        double cwSet = PART_Canvas.Width, chSet = PART_Canvas.Height;
        double cw = PART_Canvas.ActualWidth, ch = PART_Canvas.ActualHeight;

        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        string? worst = null;

        foreach (var child in PART_Canvas.Children.OfType<FrameworkElement>())
        {
            if (child is not LinkView lv)
            {
                continue;
            }

            // Local frame = what the clip actually sees: raw geometry shifted by the element's
            // −(OffsetFrameX/Y) placement.
            double x0 = Math.Min(lv.StartLeft, lv.EndLeft) + lv.OffsetFrameX;
            double x1 = Math.Max(lv.StartLeft, lv.EndLeft) + lv.OffsetFrameX;
            double y0 = Math.Min(lv.StartTop, lv.EndTop) + lv.OffsetFrameY;
            double y1 = Math.Max(lv.StartTop, lv.EndTop) + lv.OffsetFrameY;
            xMin = Math.Min(xMin, x0);
            xMax = Math.Max(xMax, x1);
            yMin = Math.Min(yMin, y0);
            yMax = Math.Max(yMax, y1);

            var flags = "";
            if (x0 < 0) flags += "L"; // local geometry left of the element box origin
            if (y0 < 0) flags += "T";
            if (x1 > lv.ActualWidth) flags += "R";
            if (y1 > lv.ActualHeight) flags += "B";
            if (flags.Length > 0)
            {
                worst ??= $"localGeom=({x0:F0},{y0:F0})-({x1:F0},{y1:F0}) lvBox=({lv.ActualWidth:F0}x{lv.ActualHeight:F0}) placed=({Canvas.GetLeft(lv):F0},{Canvas.GetTop(lv):F0}) OUT[{flags}]";
            }
        }

        if (double.IsInfinity(xMin))
        {
            return; // no realized links yet
        }

        var now = DateTime.UtcNow;
        bool dirty = worst is not null;
        bool allowed = dirty
            ? (now - _lastSizesUtc).TotalMilliseconds >= 100
            : (now - _lastSizesUtc).TotalMilliseconds >= 400;
        if (!allowed)
        {
            return;
        }

        var sv = PART_ScrollViewer;
        string scroll = sv is null
            ? "sv=n/a"
            : $"scroll=({sv.HorizontalOffset:F0},{sv.VerticalOffset:F0}) ext=({sv.ExtentWidth:F0}x{sv.ExtentHeight:F0}) vp=({sv.ViewportWidth:F0}x{sv.ViewportHeight:F0})";
        LogLine($"SIZE scale={scale:F3} canvasSet=({cwSet:F0}x{chSet:F0}) canvasA=({cw:F0}x{ch:F0}) asize=({layout.ActualSize.Width:F0}x{layout.ActualSize.Height:F0}) neg=({layout.NegativeOffset.Horizontal:F0},{layout.NegativeOffset.Vertical:F0}) linkGeom=({xMin:F0},{yMin:F0})-({xMax:F0},{yMax:F0}) off=({layout.ActualOffset.Horizontal:F0},{layout.ActualOffset.Vertical:F0}) {scroll}{(worst is null ? "" : " " + worst)}");
        _lastSizesUtc = now;
    }

    private string? SampleNode(FrameworkElement nodeView, IWorkflowNodeViewModel node, double scale)
    {
        double left = Canvas.GetLeft(nodeView);
        double top = Canvas.GetTop(nodeView);
        double w = nodeView.ActualWidth;
        var anchor = node.Anchor;
        var size = node.Size;
        double placeD = Math.Abs(left - anchor.Horizontal) + Math.Abs(top - anchor.Vertical);
        double sizeD = Math.Abs(w - size.Width);
        if (placeD > 1.5 || sizeD > 1.5)
        {
            return $"NODEDRIFT scale={scale:F3} n({anchor.Horizontal:0},{anchor.Vertical:0}) realizedLeft={left:F1} anchorGet={anchor.Horizontal:F1} realizedW={w:F1} sizeGet={size.Width:F1} dPlace={placeD:F1} dSize={sizeD:F1}";
        }

        return null;
    }

    private Point CenterToCanvas(FrameworkElement slotView, FrameworkElement canvas)
    {
        var t = slotView.TransformToVisual(canvas);
        return t.TransformPoint(new Point(slotView.ActualWidth / 2.0, slotView.ActualHeight / 2.0));
    }

    private static FrameworkElement? FindSlotView(DependencyObject root, IWorkflowSlotViewModel target)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is FrameworkElement fe && ReferenceEquals(fe.DataContext, target))
            {
                return fe;
            }

            var hit = FindSlotView(c, target);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }
}
