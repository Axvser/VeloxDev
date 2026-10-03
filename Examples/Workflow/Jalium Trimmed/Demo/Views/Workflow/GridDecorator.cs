// VeloxDev customization: The workflow grid. The adapter's WorkflowGridDecorator owns the world-coordinate maths,
// the tick layout and the label formatting; this file supplies the palette and the spacing.
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow grid: the adapter's <see cref="WorkflowGridDecorator"/> with this project's palette.
/// </summary>
public sealed class GridDecorator : WorkflowGridDecorator
{
    public GridDecorator()
    {
        MinorGridColor = Color.FromRgb(0x2A, 0x2D, 0x2E);
        MajorGridColor = Color.FromRgb(0x3A, 0x3D, 0x40);
        AxisColor = Color.FromRgb(0x4D, 0x4D, 0x4D);
        RulerBackground = Color.FromArgb(0xC8, 0x2D, 0x2D, 0x30);
        RulerLabelColor = Color.FromRgb(0xC8, 0xC8, 0xC8);
        RulerTickColor = Color.FromRgb(0x6E, 0x6E, 0x6E);
        RulerDividerColor = Color.FromRgb(0x4D, 0x4D, 0x4D);
        GridStep = 40d;
        MajorLineEvery = 5;
    }
}
