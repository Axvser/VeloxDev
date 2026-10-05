// VeloxDev customization: The workflow grid. The adapter's WorkflowGridDecorator owns the world-coordinate maths,
// the tick layout and the label formatting; this file supplies the palette and the spacing.
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The workflow grid: the adapter's <see cref="WorkflowGridDecorator"/> with this project's palette.
/// </summary>
public sealed class TemplateClass : WorkflowGridDecorator
{
    public TemplateClass()
    {
        MinorGridColor = (Color)ColorConverter.ConvertFromString("TemplateMinorGridColor");
        MajorGridColor = (Color)ColorConverter.ConvertFromString("TemplateMajorGridColor");
        AxisColor = (Color)ColorConverter.ConvertFromString("TemplateAxisColor");
        RulerBackground = (Color)ColorConverter.ConvertFromString("TemplateRulerBackground");
        RulerLabelColor = (Color)ColorConverter.ConvertFromString("TemplateRulerLabelColor");
        RulerTickColor = (Color)ColorConverter.ConvertFromString("TemplateRulerTickColor");
        RulerDividerColor = (Color)ColorConverter.ConvertFromString("TemplateRulerDividerColor");
        SurfaceBackground = (Color)ColorConverter.ConvertFromString("TemplateGridBackground");
        GridStep = TemplateGridSpacing;
        MajorLineEvery = TemplateMajorLineEvery;
    }
}
