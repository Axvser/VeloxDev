extern alias wf;

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BenchmarkDotNet.Attributes;
using WfTree = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowTreeView;

namespace VeloxDev.Adapters.Benchmarks;

// W8：树视图浮动标尺 DrawBands 每次 new 5 个 Pen/Brush + StringFormat。
[MemoryDiagnoser]
public class WinFormsTreeBandsBenchmarks
{
    private const int Frames = 20;

    private Form _form = null!;
    private MethodInfo _drawBands = null!;
    private Bitmap _bitmap = null!;
    private Graphics _graphics = null!;
    private object[] _args = null!;

    [GlobalSetup]
    public void Setup()
    {
        var type = typeof(WfTree).GetNestedType("RulerOverlayForm", BindingFlags.NonPublic)!;
        _form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        _drawBands = type.GetMethod("DrawBands", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Bands20()
    {
        for (var i = 0; i < Frames; i++) _drawBands.Invoke(_form, _args);
    }
}
