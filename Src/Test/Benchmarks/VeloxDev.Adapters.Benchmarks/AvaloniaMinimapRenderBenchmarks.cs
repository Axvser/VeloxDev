extern alias ava;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using BenchmarkDotNet.Attributes;
using VeloxDev.AdapterTestKit;
using AvaMinimap = ava::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay;

namespace VeloxDev.Adapters.Benchmarks;

// Avalonia 小地图真实 Render：无头平台 + RenderTargetBitmap 的 DrawingContext，直接调生产代码的 Render。
[MemoryDiagnoser]
public class AvaloniaMinimapRenderBenchmarks
{
    private const int Frames = 60;
    private static bool _platformReady;

    private AvaMinimap _minimap = null!;
    private RenderTargetBitmap _bitmap = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (!_platformReady)
        {
            AppBuilder.Configure<Avalonia.Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();
            _platformReady = true;
        }

        _minimap = new AvaMinimap
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = TestGraph.Create().Tree,
            ViewportWidth = 800d,
            ViewportHeight = 600d,
            IsMinimapVisible = true,
        };
        _minimap.Measure(new Avalonia.Size(200d, 140d));
        _minimap.Arrange(new Avalonia.Rect(0d, 0d, 200d, 140d));
        _bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(200, 140), new Avalonia.Vector(96d, 96d));
        Render();
    }

    [GlobalCleanup]
    public void Cleanup() => _bitmap.Dispose();

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Render60()
    {
        for (var i = 0; i < Frames; i++) Render();
    }

    private void Render()
    {
        using var dc = _bitmap.CreateDrawingContext();
        _minimap.Render(dc);
    }
}
