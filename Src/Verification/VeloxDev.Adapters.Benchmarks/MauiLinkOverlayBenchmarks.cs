using BenchmarkDotNet.Attributes;
using Microsoft.Maui.Graphics;
using Color = Microsoft.Maui.Graphics.Color;
using PointF = Microsoft.Maui.Graphics.PointF;

namespace VeloxDev.Adapters.Benchmarks;

// MAUI 连线层：每帧每条链接 new PathF + 4 次 Color.WithAlpha。A = 原写法；B = 复用 PathF 原地 SetPoint + 按源色缓存淡色。
[MemoryDiagnoser]
public class MauiLinkOverlayBenchmarks
{
    private const int Links = 20;
    private const int Samples = 64;
    private static readonly double[] Factors = [0.10, 0.16, 0.75, 0.55];

    private readonly PointF[] _samples = new PointF[Samples];
    private readonly Color _color = Color.FromArgb("#FF38BDF8");
    private PathF? _body;
    private Color? _fadeSource;
    private readonly Color[] _fades = new Color[4];

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < Samples; i++) _samples[i] = new PointF(i * 3f, i * 2f);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Links)]
    public object? Original()
    {
        object? last = null;
        for (var l = 0; l < Links; l++)
        {
            var body = new PathF();
            body.MoveTo(_samples[0]);
            for (var i = 1; i < Samples; i++) body.LineTo(_samples[i]);
            _body = body;
            for (var f = 0; f < Factors.Length; f++) last = _color.WithAlpha((float)(_color.Alpha * Factors[f]));
        }

        return last;
    }

    [Benchmark(OperationsPerInvoke = Links)]
    public object? Reused()
    {
        object? last = null;
        for (var l = 0; l < Links; l++)
        {
            if (_body is { } body && body.Count == Samples)
            {
                for (var i = 0; i < Samples; i++) body.SetPoint(i, _samples[i].X, _samples[i].Y);
            }
            else
            {
                body = new PathF();
                body.MoveTo(_samples[0]);
                for (var i = 1; i < Samples; i++) body.LineTo(_samples[i]);
                _body = body;
            }

            if (!ReferenceEquals(_fadeSource, _color))
            {
                _fadeSource = _color;
                for (var f = 0; f < Factors.Length; f++) _fades[f] = _color.WithAlpha((float)(_color.Alpha * Factors[f]));
            }

            for (var f = 0; f < Factors.Length; f++) last = _fades[f];
        }

        return last;
    }
}
