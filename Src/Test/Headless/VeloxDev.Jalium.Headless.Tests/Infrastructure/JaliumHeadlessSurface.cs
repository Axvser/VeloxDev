using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Jalium.Headless.Tests.Infrastructure;

// Jalium 的控件可以脱离窗口 Measure/Arrange（它自己的测试就是这么跑的）；
// 表面行为按名字认领部件，所以这里用 NameScope 注册，不依赖 .jalxaml。
internal sealed class JaliumHeadlessSurface : IDisposable
{
    public JaliumHeadlessSurface(TestGraph graph, bool bind = true)
    {
        Graph = graph;

        Host = new UserControl { Width = 800d, Height = 600d };
        Border = new Border();
        Scroll = new ScrollViewer
        {
            Width = 800d,
            Height = 600d,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Canvas = new Canvas { Width = 8000d, Height = 6000d };
        Decorator = new TestGridDecorator();

        Scroll.Content = Canvas;
        Decorator.Children.Add(Scroll);
        Border.Child = Decorator;
        Host.Content = Border;

        var scope = new NameScope();
        NameScope.SetNameScope(Host, scope);
        Host.RegisterName("PART_ScrollViewer", Scroll);
        Host.RegisterName("PART_Canvas", Canvas);
        Host.RegisterName("PART_GridDecorator", Decorator);
        Host.RegisterName("PART_SurfaceBorder", Border);

        WorkflowSurfaceBehavior.SetScrollViewerName(Host, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(Host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetGridDecoratorName(Host, "PART_GridDecorator");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(Host, "PART_SurfaceBorder");
        WorkflowSurfaceBehavior.SetZoomEnabled(Host, true);

        // BindTree 从宿主资源里取这个键；不给选择器，ViewPool 只会挂一半。
        Host.Resources[WorkflowSurfaceBehavior.TemplateSelectorKey] = new AnyTemplateSelector();

        Host.DataContext = bind ? graph.Tree : null;
        WorkflowSurfaceBehavior.SetIsEnabled(Host, true);

        Host.Measure(new Size(800d, 600d));
        Host.Arrange(new Rect(0d, 0d, 800d, 600d));

        // 没有窗口，Loaded 不会自己来。Jalium 的 SetLoadedState 同时置 IsLoaded 并抬 CLR Loaded
        // （只 RaiseEvent 不够：表面订的是 CLR 事件）—— 这正是 Window.Show 内部做的那一步。
        SetLoaded(Host);
        WorkflowSurfaceBehavior.Refresh(Host);
        Pump();

        // 视口恢复是在 Loaded 队列里调 ScrollTo 的；Jalium 的滚动偏移要等下一次布局才落到
        // HorizontalOffset/Viewport 上，所以恢复后必须再排一次版。
        Layout();
        WorkflowSurfaceBehavior.Refresh(Host);
        Pump();
    }

    private void Layout()
    {
        Host.Measure(new Size(800d, 600d));
        Host.Arrange(new Rect(0d, 0d, 800d, 600d));
    }

    public TestGraph Graph { get; }

    public UserControl Host { get; }

    public Border Border { get; }

    public ScrollViewer Scroll { get; }

    public Canvas Canvas { get; }

    public TestGridDecorator Decorator { get; }

    public void ScrollTo(double horizontal, double vertical)
    {
        Scroll.ScrollToHorizontalOffset(horizontal);
        Scroll.ScrollToVerticalOffset(vertical);
        Settle();
    }

    /// <summary>Re-runs measure/arrange and the refresh that publishes the new scroll offset.</summary>
    public void Settle()
    {
        // 偏移先被记成 requested，下一次 Measure/Arrange 才成为 HorizontalOffset/Viewport。
        Layout();
        WorkflowSurfaceBehavior.Refresh(Host);
        Pump();
    }

    private static readonly MethodInfo SetLoadedState =
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "Jalium's FrameworkElement.SetLoadedState moved; update the headless harness.");

    internal static void SetLoaded(FrameworkElement element)
        => SetLoadedState.Invoke(element, [true]);

    public static void Pump(DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        WorkflowSurfaceBehavior.SetIsEnabled(Host, false);
        Host.Content = null;
        Pump();
    }
}
