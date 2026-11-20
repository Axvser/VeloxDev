extern alias wf;

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BenchmarkDotNet.Attributes;
using WfAttached = wf::VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks;

// W8：浮动标尺 DrawBands 每次 new 5 个 Pen/Brush + StringFormat。
// 标尺浮层已从 WorkflowTreeView 的嵌套类型拆成独立的 WorkflowRulerOverlayForm，类型是 internal，按名反射。
[MemoryDiagnoser]
public class WinFormsTreeBandsBenchmarks
{
    private const int Frames = 20;

    private static readonly Type OverlayType = typeof(WfAttached.WorkflowSurfaceBehavior)
        .Assembly.GetType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowRulerOverlayForm", throwOnError: true)!;

    private Form _form = null!;
    private Control _host = null!;
    private MethodInfo _drawBands = null!;
    private Bitmap _bitmap = null!;
    private Graphics _graphics = null!;
    private object[] _args = null!;

    [GlobalSetup]
    public void Setup()
    {
        _host = new Panel { Width = 800, Height = 600 };
        _form = (Form)Activator.CreateInstance(
            OverlayType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [_host, 36d],
            culture: null)!;
        _drawBands = OverlayType.GetMethod("DrawBands", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _bitmap = new Bitmap(800, 600);
        _graphics = Graphics.FromImage(_bitmap);
        _args = [_graphics, 800, 600];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
        _form.Dispose();
        _host.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Bands20()
    {
        for (var i = 0; i < Frames; i++) _drawBands.Invoke(_form, _args);
    }
}
