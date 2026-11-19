using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.AdapterTestKit;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WPF.Headless.Tests.Infrastructure;

// 完全用代码搭出来的工作流表面：没有 XAML、没有窗口、没有测试宿主自己的消息循环。
// 可视树按 trimmed 模板的形状搭，够表面解析所有它需要的名字。
// WPF 只需要 STA 线程和一个被泵的 Dispatcher；测试类用 [STATestClass]，凡是按优先级排队的工作
// （布局、视图池批次、视口恢复）之后都要调 Pump。
internal sealed class WpfHeadlessSurface : IDisposable
{
    private const double HostWidth = 800d;
    private const double HostHeight = 600d;
    private const double CanvasWidth = 8000d;
    private const double CanvasHeight = 6000d;

    public WpfHeadlessSurface(TestGraph graph, bool enable = true)
    {
        Graph = graph;
        Tree = graph.Tree;

        // The template binds the canvas to Layout.ActualSize; the hand-built fixture must do the same or an
        // overscroll-grown canvas has no extent to scroll into.
        Tree.Layout.OriginSize = new VeloxDev.WorkflowSystem.Size(CanvasWidth, CanvasHeight);

        Host = new UserControl
        {
            Width = HostWidth,
            Height = HostHeight,
            DataContext = Tree,
        };

        Border = new Border
        {
            Background = Brushes.Transparent,
        };

        Scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Canvas = new Canvas
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Background = Brushes.Transparent,
        };

        // The template binds the canvas to Layout.ActualSize; the hand-built fixture must do the same or an
        // overscroll-grown canvas has no extent to scroll into.
        Canvas.SetBinding(FrameworkElement.WidthProperty,
            new System.Windows.Data.Binding("ActualSize.Width") { Source = Tree.Layout });
        Canvas.SetBinding(FrameworkElement.HeightProperty,
            new System.Windows.Data.Binding("ActualSize.Height") { Source = Tree.Layout });

        Decorator = new TestGridDecorator();

        Scroll.Content = Canvas;
        Decorator.Children.Add(Scroll);
        Border.Child = Decorator;
        Host.Content = Border;

        // FindName is a namescope lookup; code-built trees have to register the names explicitly.
        var scope = new NameScope();
        NameScope.SetNameScope(Host, scope);
        scope.RegisterName("PART_ScrollViewer", Scroll);
        scope.RegisterName("PART_Canvas", Canvas);
        scope.RegisterName("PART_GridDecorator", Decorator);
        scope.RegisterName("PART_SurfaceBorder", Border);

        Behaviors.WorkflowSurfaceBehavior.SetScrollViewerName(Host, "PART_ScrollViewer");
        Behaviors.WorkflowSurfaceBehavior.SetCanvasName(Host, "PART_Canvas");
        Behaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(Host, "PART_GridDecorator");
        Behaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(Host, "PART_SurfaceBorder");
        Behaviors.WorkflowSurfaceBehavior.SetZoomEnabled(Host, true);

        Layout();
        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(Host, enable);
        Behaviors.WorkflowSurfaceBehavior.Refresh(Host);
        Pump();
    }

    public TestGraph Graph { get; }

    public VeloxDev.WorkflowSystem.TreeDefaultViewModel Tree { get; }

    public UserControl Host { get; }

    public Border Border { get; }

    public ScrollViewer Scroll { get; }

    public Canvas Canvas { get; }

    public TestGridDecorator Decorator { get; }

    /// <summary>Lays out the host at its fixed size; WPF does not lay out a window-less control on its own.</summary>
    public void Layout()
    {
        Host.Measure(new System.Windows.Size(HostWidth, HostHeight));
        Host.Arrange(new Rect(0d, 0d, HostWidth, HostHeight));
        Host.UpdateLayout();
    }

    /// <summary>Scrolls both axes and lets the ScrollChanged → Refresh path run.</summary>
    public void ScrollTo(double horizontal, double vertical)
    {
        Scroll.ScrollToHorizontalOffset(horizontal);
        Scroll.ScrollToVerticalOffset(vertical);
        Scroll.UpdateLayout();
        Pump();
    }

    /// <summary>Drains dispatcher work down to the given priority (idle by default, so Loaded work is included).</summary>
    public static void Pump(DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(Host, false);
        Host.Content = null;
        Pump();
    }
}
