extern alias wf;

using System.Drawing;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using WfGrid = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowGridDecorator;

namespace VeloxDev.Adapters.Benchmarks;

// W8：WinForms 网格每次重绘 new 9 个 Pen/SolidBrush/StringFormat。直接调用 Render，隔离绘制本身。
[MemoryDiagnoser]
public class WinFormsGridRenderBenchmarks
{
    private const int Frames = 20;
    private static readonly MethodInfo Render =
        typeof(WfGrid).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private WfGrid _grid = null!;
    private Bitmap _bitmap = null!;
    private Graphics _graphics = null!;
    private readonly object[] _args = new object[1];

    [GlobalSetup]
    public void Setup()
    {
        _grid = new WfGrid { Width = 800, Height = 600 };
        _bitmap = new Bitmap(800, 600);
        _graphics = Graphics.FromImage(_bitmap);
        _args[0] = _graphics;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
        _grid.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Render20()
    {
        for (var i = 0; i < Frames; i++) Render.Invoke(_grid, _args);
    }
}
