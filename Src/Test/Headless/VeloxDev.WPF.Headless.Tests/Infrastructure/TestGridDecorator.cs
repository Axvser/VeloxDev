using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WPF.Headless.Tests.Infrastructure;

// 满足 IWorkflowGridDecorator 的最小对象：只记录表面推进来的值。适配器只需要接口，
// 不需要真的网格/标尺，就能验证表面的数据流。
internal sealed class TestGridDecorator : Panel, IWorkflowGridDecorator
{
    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public double ContentOffsetX { get; set; }

    public double ContentOffsetY { get; set; }

    public double RulerBand => 0d;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }
}
