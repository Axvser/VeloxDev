using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks;

// 小地图管线的热路径：树订阅之后的映射、放置，以及拖拽时的坐标换算与滚动写回。
// 图层是假的，测的是 Core 状态机本身，不含任何平台的 DrawingContext 成本。
[MemoryDiagnoser]
public class MinimapPipelineBenchmarks
{
    private const int Frames = 60;

    private TreeDefaultViewModel _tree = null!;
    private MinimapPipelineCore<object, NullRenderer, BenchmarkViewport, BenchmarkInput> _pipeline = null!;
    private BenchmarkInput _input = null!;
    private readonly object _surface = new();

    [GlobalSetup]
    public void Setup()
    {
        _tree = new TreeDefaultViewModel();
        for (var i = 0; i < 200; i++)
        {
            _tree.Nodes.Add(new NodeDefaultViewModel
            {
                Anchor = new Anchor(i % 20 * 220d, i / 20 * 160d, 0),
                Size = new Size(160d, 90d),
            });
        }

        _input = new BenchmarkInput();
        _pipeline = new MinimapPipelineCore<object, NullRenderer, BenchmarkViewport, BenchmarkInput>(
            new NullRenderer(),
            new BenchmarkViewport(),
            _input,
            MinimapMetrics.Default with { AvailableRight = 1920d, AvailableBottom = 1080d })
        {
            Tree = _tree,
        };
    }

    [GlobalCleanup]
    public void Cleanup() => _pipeline.Dispose();

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Refresh60()
    {
        for (var i = 0; i < Frames; i++)
        {
            _pipeline.Invalidate();
            _pipeline.Refresh();
        }
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Render60()
    {
        for (var i = 0; i < Frames; i++)
        {
            _pipeline.Render(_surface);
        }
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Drag60()
    {
        var rect = _pipeline.MinimapRect;
        var centerX = rect.Left + rect.Width * 0.6d;
        var centerY = rect.Top + rect.Height * 0.6d;

        _input.PointerPosition = (centerX, centerY);
        _input.IsPressed = true;
        _pipeline.PointerPressed();

        for (var i = 0; i < Frames; i++)
        {
            _input.PointerPosition = (centerX + i % 20 - 10, centerY + i % 20 - 10);
            _pipeline.PointerMoved();
        }

        _pipeline.PointerReleased();
    }

    private sealed class NullRenderer : IMinimapRenderer<object>
    {
        public int Calls { get; private set; }

        public void Render(object surface, MinimapDrawContext context) => Calls++;
    }

    private sealed class BenchmarkViewport : IMinimapViewportHost
    {
        public double ScrollOffsetX => 0d;

        public double ScrollOffsetY => 0d;

        public double ContentOffsetX => 0d;

        public double ContentOffsetY => 0d;

        public double ViewportWidth => 800d;

        public double ViewportHeight => 600d;

        public double ScrollableWidth => 1_000_000d;

        public double ScrollableHeight => 1_000_000d;

        public void ApplyScroll(double x, double y)
        {
        }
    }

    private sealed class BenchmarkInput : IMinimapInputHost
    {
        public (double X, double Y) PointerPosition { get; set; }

        public bool IsPressed { get; set; }

        public bool IsCaptured { get; private set; }

        public WorkflowBounds ClampRect => default;

        public bool Capture()
        {
            IsCaptured = true;
            return true;
        }

        public void Release() => IsCaptured = false;
    }
}
