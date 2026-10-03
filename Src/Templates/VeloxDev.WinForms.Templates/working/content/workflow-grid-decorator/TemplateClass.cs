// VeloxDev customization: The workflow grid. The adapter's WorkflowGridDecorator owns the world-coordinate maths,
// the tick layout, the label formatting and the clipping; this file supplies the palette and the spacing.
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The workflow grid: the adapter's <see cref="WorkflowGridDecorator"/> with this project's palette.
/// </summary>
public sealed class TemplateClass : WorkflowGridDecorator
{
    public TemplateClass()
    {
        GridBackground = ParseColor("TemplateGridBackground");
        MinorGridColor = ParseColor("TemplateMinorGridColor");
        MajorGridColor = ParseColor("TemplateMajorGridColor");
        AxisColor = ParseColor("TemplateAxisColor");
        RulerBackground = ParseColor("TemplateRulerBackground");
        RulerTickColor = ParseColor("TemplateRulerTickColor");
        RulerLabelColor = ParseColor("TemplateRulerLabelColor");
        RulerDividerColor = ParseColor("TemplateRulerDividerColor");
        GridSpacing = ParseGridValue("TemplateGridSpacing");
        MajorLineEvery = int.Parse("TemplateMajorLineEvery", CultureInfo.InvariantCulture);
    }

    // The spacing token is written with a trailing `d` so it reads as a double in generated code.
    private static double ParseGridValue(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }
}
