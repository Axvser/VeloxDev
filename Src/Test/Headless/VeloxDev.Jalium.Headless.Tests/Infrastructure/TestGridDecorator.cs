using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Jalium.Headless.Tests.Infrastructure;

// 满足 IWorkflowGridDecorator 的最小对象：只记录表面推进来的值。
internal sealed class TestGridDecorator : Panel, IWorkflowGridDecorator
{
    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public double ContentOffsetX { get; set; }

    public double ContentOffsetY { get; set; }

    public double RulerBand => 0d;

    protected override global::Jalium.UI.Size MeasureOverride(global::Jalium.UI.Size availableSize)
    {
        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    protected override global::Jalium.UI.Size ArrangeOverride(global::Jalium.UI.Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            child.Arrange(new global::Jalium.UI.Rect(finalSize));
        }

        return finalSize;
    }
}
