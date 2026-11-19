using BenchmarkDotNet.Attributes;
using AvaBrush = Avalonia.Media.SolidColorBrush;
using AvaColor = Avalonia.Media.Color;
using AvaPen = Avalonia.Media.Pen;
using JalBrush = Jalium.UI.Media.SolidColorBrush;
using JalColor = Jalium.UI.Media.Color;
using JalPen = Jalium.UI.Media.Pen;

namespace VeloxDev.Adapters.Benchmarks;

// W4：Avalonia/Jalium 小地图每帧 new 两支 Pen。平台 DrawingContext 无法无头构造，这里隔离「取画笔」这一步：
// A = 原写法（每帧 new），B = 改后的按画刷引用+线宽缓存。
[MemoryDiagnoser]
public class MinimapPenBenchmarks
{
    private const int Frames = 60;

    private readonly AvaBrush _avaBrush = new(AvaColor.FromArgb(220, 148, 163, 184));
    private readonly JalBrush _jalBrush = new(JalColor.FromArgb(0xDC, 0x94, 0xA3, 0xB8));
    private AvaPen? _avaBorder, _avaViewport;
    private JalPen? _jalBorder, _jalViewport;

    [Benchmark(OperationsPerInvoke = Frames)]
    public object? Avalonia_NewPen()
    {
        object? last = null;
        for (var i = 0; i < Frames; i++)
        {
            last = new AvaPen(_avaBrush, 1);
            last = new AvaPen(_avaBrush, 1.5);
        }

        return last;
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public object? Avalonia_CachedPen()
    {
        object? last = null;
        for (var i = 0; i < Frames; i++)
        {
            last = Get(ref _avaBorder, _avaBrush, 1);
            last = Get(ref _avaViewport, _avaBrush, 1.5);
        }

        return last;
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public object? Jalium_NewPen()
    {
        object? last = null;
        for (var i = 0; i < Frames; i++)
        {
            last = new JalPen(_jalBrush, 1);
            last = new JalPen(_jalBrush, 1.5);
        }

        return last;
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public object? Jalium_CachedPen()
    {
        object? last = null;
        for (var i = 0; i < Frames; i++)
        {
            last = Get(ref _jalBorder, _jalBrush, 1);
            last = Get(ref _jalViewport, _jalBrush, 1.5);
        }

        return last;
    }

    // 与适配器里 GetPen 同一判断。
    private static AvaPen Get(ref AvaPen? cache, AvaBrush brush, double thickness)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush) || cache.Thickness != thickness)
        {
            cache = new AvaPen(brush, thickness);
        }

        return cache;
    }

    private static JalPen Get(ref JalPen? cache, JalBrush brush, double thickness)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush) || cache.Thickness != thickness)
        {
            cache = new JalPen(brush, thickness);
        }

        return cache;
    }
}
