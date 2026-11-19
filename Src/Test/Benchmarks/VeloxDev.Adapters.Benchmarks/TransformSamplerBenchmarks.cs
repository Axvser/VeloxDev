extern alias wpf;
extern alias ava;
extern alias jal;

using BenchmarkDotNet.Attributes;
using VeloxDev.TransitionSystem;
using WpfMedia = System.Windows.Media;
using AvaMedia = Avalonia.Media;
using JalMedia = Jalium.UI.Media;

namespace VeloxDev.Adapters.Benchmarks;

// T1：起止变换类型不一致 / 含 TransformGroup 时走慢路径，每帧一次 InsertFrame。
[MemoryDiagnoser]
public class TransformSamplerBenchmarks
{
    private sealed class Sink : ITransitionProperty
    {
        public object? Last;
        public string Path => "RenderTransform";
        public Type PropertyType => typeof(object);
        public bool CanRead => true;
        public bool CanWrite => true;
        public object? GetValue(object? target) => Last;
        public bool SetValue(object target, object? value) { Last = value; return true; }
    }

    private readonly Sink _sink = new();
    private readonly object _target = new();
    private object? _working;

    private wpf::VeloxDev.Adapters.NativeSamplers.TransformSampler _wpf = null!;
    private object _wpfStart = null!, _wpfEnd = null!;
    private ava::VeloxDev.Adapters.NativeSamplers.TransformSampler _ava = null!;
    private object _avaStart = null!, _avaEnd = null!;
    private jal::VeloxDev.Adapters.NativeSamplers.TransformSampler _jal = null!;
    private object _jalStart = null!, _jalEnd = null!;

    [GlobalSetup]
    public void Setup()
    {
        _wpf = new();
        var wg = new WpfMedia.TransformGroup();
        wg.Children.Add(new WpfMedia.ScaleTransform(1, 1));
        wg.Children.Add(new WpfMedia.RotateTransform(0));
        wg.Children.Add(new WpfMedia.TranslateTransform(0, 0));
        _wpfStart = wg;
        _wpfEnd = new WpfMedia.TranslateTransform(100, 50);

        _ava = new();
        var ag = new AvaMedia.TransformGroup();
        ag.Children.Add(new AvaMedia.ScaleTransform(1, 1));
        ag.Children.Add(new AvaMedia.RotateTransform(0));
        ag.Children.Add(new AvaMedia.TranslateTransform(0, 0));
        _avaStart = ag;
        _avaEnd = new AvaMedia.TranslateTransform(100, 50);

        _jal = new();
        var jg = new JalMedia.TransformGroup();
        jg.Children.Add(new JalMedia.ScaleTransform { ScaleX = 1, ScaleY = 1 });
        jg.Children.Add(new JalMedia.RotateTransform { Angle = 0 });
        jg.Children.Add(new JalMedia.TranslateTransform { X = 0, Y = 0 });
        _jalStart = jg;
        _jalEnd = new JalMedia.TranslateTransform { X = 100, Y = 50 };
    }

    private const int Frames = 60;

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Wpf()
    {
        for (var i = 1; i <= Frames; i++)
            _wpf.InsertFrame(_target, _sink, ref _working, _wpfStart, _wpfEnd, null, i / (Frames + 1d));
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Avalonia()
    {
        for (var i = 1; i <= Frames; i++)
            _ava.InsertFrame(_target, _sink, ref _working, _avaStart, _avaEnd, null, i / (Frames + 1d));
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Jalium()
    {
        for (var i = 1; i <= Frames; i++)
            _jal.InsertFrame(_target, _sink, ref _working, _jalStart, _jalEnd, null, i / (Frames + 1d));
    }
}
