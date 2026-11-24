using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks;

// Core surface pipeline: the arithmetic every adapter used to inline (wheel step, single-axis write,
// clamp/edge expansion, centering). A fake host isolates it from any platform scroll container.
[MemoryDiagnoser]
public class SurfaceScrollerBenchmarks
{
    private sealed class Host : ISurfaceScrollHost
    {
        private readonly CanvasLayout layout = new()
        {
            OriginSize = new VeloxDev.WorkflowSystem.Size(8000d, 6000d),
            NegativeOffset = new Offset(0d, 0d),
            PositiveOffset = new Offset(0d, 0d),
        };

        public CanvasLayout? Layout => layout;
        public double ViewportWidth => 1200d;
        public double ViewportHeight => 800d;
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public double HorizontalMaximum => 100_000d;
        public double VerticalMaximum => 100_000d;

        public SurfaceWheelProfile WheelProfile => SurfaceWheelProfile.FromLines(
            SurfaceScrollerCore.DefaultWheelDelta,
            3d,
            SurfaceScrollerCore.DefaultLineHeight);

        public void Reset()
        {
            HorizontalOffset = 1000d;
            VerticalOffset = 1000d;
        }

        public void ApplyOffset(double? horizontalOffset, double? verticalOffset)
        {
            if (horizontalOffset is { } x) HorizontalOffset = x;
            if (verticalOffset is { } y) VerticalOffset = y;
        }

        public void ExpandCanvas()
        {
        }
    }

    private Host host = null!;
    private SurfaceScrollerCore scroller = null!;

    [GlobalSetup]
    public void Setup()
    {
        host = new Host();
        scroller = new SurfaceScrollerCore(host);
    }

    [IterationSetup]
    public void ResetOffsets() => host.Reset();

    [Benchmark]
    public void WheelNotch() => scroller.ScrollBy(120d, -120d);

    [Benchmark]
    public void CenterOn() => scroller.CenterOn(4000d, 3000d);
}
