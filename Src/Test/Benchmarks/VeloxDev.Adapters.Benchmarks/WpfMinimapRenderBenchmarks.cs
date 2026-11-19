extern alias wpf;

using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BenchmarkDotNet.Attributes;
using VeloxDev.AdapterTestKit;
using WpfMinimap = wpf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay;

namespace VeloxDev.Adapters.Benchmarks;

// W4：小地图 OnRender 每帧 new Pen / RectangleGeometry。直接调用 OnRender 写入 DrawingVisual，隔离绘制本身的分配。
[MemoryDiagnoser]
public class WpfMinimapRenderBenchmarks
{
    private const int Frames = 60;
    private static readonly MethodInfo OnRender =
        typeof(WpfMinimap).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private Dispatcher _ui = null!;
    private WpfMinimap _minimap = null!;
    private DrawingVisual _visual = null!;
    private object[] _args = new object[1];

    [GlobalSetup]
    public void Setup()
    {
        // 进程内工具链不在 STA 上运行，单独起一个 STA 线程承载 WPF Dispatcher。
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _ui = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        _ui.Invoke(() =>
        {
            _minimap = new WpfMinimap
            {
                Width = 200d,
                Height = 140d,
                WorkflowTree = TestGraph.Create().Tree,
                ViewportWidth = 800d,
                ViewportHeight = 600d,
                IsMinimapVisible = true,
            };
            _minimap.Measure(new System.Windows.Size(200d, 140d));
            _minimap.Arrange(new Rect(0d, 0d, 200d, 140d));
            _visual = new DrawingVisual();
            Render();
        });
    }

    [GlobalCleanup]
    public void Cleanup() => _ui.InvokeShutdown();

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Render60() => _ui.Invoke(() =>
    {
        for (var i = 0; i < Frames; i++) Render();
    });

    private void Render()
    {
        using var dc = _visual.RenderOpen();
        _args[0] = dc;
        OnRender.Invoke(_minimap, _args);
    }
}
