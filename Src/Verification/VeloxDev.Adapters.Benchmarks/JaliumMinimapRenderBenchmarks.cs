extern alias jal;

using System.Reflection;
using BenchmarkDotNet.Attributes;
using Jalium.UI.Media;
using VeloxDev.AdapterTestKit;
using JalMinimap = jal::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay;

namespace VeloxDev.Adapters.Benchmarks;

// Jalium 小地图真实 OnRender：DrawingGroup.Open() 给出无需窗口的记录型 DrawingContext，反射调生产代码的 OnRender。
[MemoryDiagnoser]
public class JaliumMinimapRenderBenchmarks
{
    private const int Frames = 60;
    private static readonly MethodInfo OnRender =
        typeof(JalMinimap).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private JalMinimap _minimap = null!;
    private readonly object[] _args = new object[1];

    [GlobalSetup]
    public void Setup()
    {
        _minimap = new JalMinimap
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = TestGraph.Create().Tree,
            ViewportWidth = 800d,
            ViewportHeight = 600d,
        };
        _minimap.Measure(new Jalium.UI.Size(200d, 140d));
        _minimap.Arrange(new Jalium.UI.Rect(0d, 0d, 200d, 140d));
        Render();
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Render60()
    {
        for (var i = 0; i < Frames; i++) Render();
    }

    private void Render()
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        _args[0] = dc;
        OnRender.Invoke(_minimap, _args);
    }
}
