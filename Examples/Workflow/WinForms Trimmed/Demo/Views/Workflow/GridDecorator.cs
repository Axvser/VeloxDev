// VeloxDev customization: The workflow grid. The adapter's WorkflowGridDecorator owns the world-coordinate maths,
// the tick layout, the label formatting and the clipping; this file supplies the palette and the spacing.
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow grid: the adapter's <see cref="WorkflowGridDecorator"/> with this project's palette.
/// </summary>
public sealed class GridDecorator : WorkflowGridDecorator
{
    public GridDecorator()
    {
        GridBackground = ParseColor("#1E1E1E");
        MinorGridColor = ParseColor("#2A2D2E");
        MajorGridColor = ParseColor("#3A3D40");
        AxisColor = ParseColor("#4D4D4D");
        RulerBackground = ParseColor("#70252526");
        RulerTickColor = ParseColor("#555555");
        RulerLabelColor = ParseColor("#888888");
        RulerDividerColor = ParseColor("#3A3D40");
        GridSpacing = ParseGridValue("40d");
        MajorLineEvery = int.Parse("5", CultureInfo.InvariantCulture);
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
