extern alias wf;

using System.Drawing;
using System.Windows.Forms;
using BenchmarkDotNet.Attributes;
using VeloxDev.AdapterTestKit;
using WfSlot = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotAttachment;

namespace VeloxDev.Adapters.Benchmarks;

// W8：插槽 Paint 每次 new SolidBrush + Pen。
[MemoryDiagnoser]
public class WinFormsSlotPaintBenchmarks
{
    private const int Frames = 60;

    private Control _control = null!;
    private WfSlot _slot = null!;
    private Bitmap _bitmap = null!;
    private Graphics _graphics = null!;

    [GlobalSetup]
    public void Setup()
    {
        _control = new Control { Width = 16, Height = 16 };
        _slot = WfSlot.Attach(_control);
        _slot.SlotPath = "M0 0 L1024 0 L1024 1024 L0 1024 Z";
        _slot.Slot = TestGraph.Create().LeftOutput;
        _bitmap = new Bitmap(16, 16);
        _graphics = Graphics.FromImage(_bitmap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
        _control.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Paint60()
    {
        for (var i = 0; i < Frames; i++) _slot.Paint(_graphics);
    }
}
