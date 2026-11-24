using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using VeloxDev.AdapterTestKit;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Avalonia.Headless.Tests.Infrastructure;

// Avalonia 无头窗口里的工作流表面：真实布局、真实输入路由，没有窗口系统。
// 可视树按 trimmed 模板的形状搭，够表面解析所有它需要的名字。
internal sealed class AvaloniaHeadlessSurface : IDisposable
{
    private const double HostWidth = 800d;
    private const double HostHeight = 600d;
    private const double CanvasWidth = 8000d;
    private const double CanvasHeight = 6000d;

    public AvaloniaHeadlessSurface(TestGraph graph, bool enable = true)
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
            Background = Brushes.Transparent,
        };

        Canvas.Bind(global::Avalonia.Layout.Layoutable.WidthProperty,
            new global::Avalonia.Data.Binding("ActualSize.Width") { Source = Tree.Layout });
        Canvas.Bind(global::Avalonia.Layout.Layoutable.HeightProperty,
            new global::Avalonia.Data.Binding("ActualSize.Height") { Source = Tree.Layout });

        Decorator = new TestGridDecorator();

        Scroll.Content = Canvas;
        Decorator.Children.Add(Scroll);
        Border.Child = Decorator;
        Host.Content = Border;

        // FindControl is a namescope lookup; a code-built tree has to register the names explicitly.
        var scope = new NameScope();
        NameScope.SetNameScope(Host, scope);
        scope.Register("PART_ScrollViewer", Scroll);
        scope.Register("PART_Canvas", Canvas);
        scope.Register("PART_GridDecorator", Decorator);
        scope.Register("PART_SurfaceBorder", Border);
        scope.Complete();

        Behaviors.WorkflowSurfaceBehavior.SetScrollViewerName(Host, "PART_ScrollViewer");
        Behaviors.WorkflowSurfaceBehavior.SetCanvasName(Host, "PART_Canvas");
        Behaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(Host, "PART_GridDecorator");
        Behaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(Host, "PART_SurfaceBorder");
        Behaviors.WorkflowSurfaceBehavior.SetZoomEnabled(Host, true);

        Window = new Window
        {
            Width = HostWidth,
            Height = HostHeight,
            Content = Host,
        };

        Window.Show();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(Host, enable);
        Behaviors.WorkflowSurfaceBehavior.Refresh(Host);
        Dispatcher.UIThread.RunJobs();
    }

    public TestGraph Graph { get; }

    public VeloxDev.WorkflowSystem.TreeDefaultViewModel Tree { get; }

    public Window Window { get; }

    public UserControl Host { get; }

    public Border Border { get; }

    public ScrollViewer Scroll { get; }

    public Canvas Canvas { get; }

    public TestGridDecorator Decorator { get; }

    /// <summary>Moves the scroll viewer and lets the ScrollChanged → Refresh path run.</summary>
    public void ScrollTo(double horizontal, double vertical)
    {
        Scroll.Offset = new Vector(horizontal, vertical);
        Scroll.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(Host, false);
        Window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
