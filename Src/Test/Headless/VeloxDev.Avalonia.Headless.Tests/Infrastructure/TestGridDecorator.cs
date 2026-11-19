using Avalonia.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Avalonia.Headless.Tests.Infrastructure;

// 满足 IWorkflowGridDecorator 的最小对象：只记录表面推进来的值。适配器只需要接口，
// 不需要真的网格/标尺，就能验证表面的数据流。
internal sealed class TestGridDecorator : Panel, IWorkflowGridDecorator
{
    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public double ContentOffsetX { get; set; }

    public double ContentOffsetY { get; set; }

    public double RulerBand => 0d;
}
