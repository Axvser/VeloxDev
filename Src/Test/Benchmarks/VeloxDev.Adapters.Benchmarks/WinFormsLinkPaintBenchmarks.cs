extern alias wf;

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BenchmarkDotNet.Attributes;
using WfLink = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowLinkAttachment;

namespace VeloxDev.Adapters.Benchmarks;

// W5：WinForms 连线 Paint 每次 new Pen + new GraphicsPath（均持有 GDI+ 本机句柄）。
[MemoryDiagnoser]
public class WinFormsLinkPaintBenchmarks
{
    private const int Frames = 60;

    private Control _control = null!;
    private WfLink _link = null!;
    private Bitmap _bitmap = null!;
    private Graphics _graphics = null!;

    [GlobalSetup]
    public void Setup()
    {
        _control = new Control();
        _link = WfLink.Attach(_control);
        // 直接写入窗口局部曲线，隔离 Paint 本身的开销（几何重建另有路径）。
        typeof(WfLink).GetField("windowCurve", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_link, new PointF[] { new(2, 2), new(60, 2), new(140, 98), new(198, 98) });
        _bitmap = new Bitmap(200, 100);
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
        for (var i = 0; i < Frames; i++) _link.Paint(_graphics);
    }
}
