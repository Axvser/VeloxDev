extern alias wf;

using System.Reflection;
using System.Windows.Forms;
using BenchmarkDotNet.Attributes;
using VeloxDev.AdapterTestKit;
using WfLink = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowLinkAttachment;

namespace VeloxDev.Adapters.Benchmarks;

// W5：连线几何重建每次 new 加宽画笔、路径、平移矩阵。拖拽时每帧都会走这里。
[MemoryDiagnoser]
public class WinFormsLinkRebuildBenchmarks
{
    private const int Frames = 60;
    private static readonly MethodInfo Rebuild =
        typeof(WfLink).GetMethod("RebuildGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private Control _control = null!;
    private WfLink _link = null!;

    [GlobalSetup]
    public void Setup()
    {
        _control = new Control();
        _link = WfLink.Attach(_control);
        _link.Bind(TestGraph.Create().Link);
    }

    [GlobalCleanup]
    public void Cleanup() => _control.Dispose();

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Rebuild60()
    {
        for (var i = 0; i < Frames; i++) Rebuild.Invoke(_link, null);
    }
}
