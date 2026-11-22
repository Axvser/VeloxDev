// VeloxDev customization: the grid palette and spacing live here.
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace TemplateNamespace;

/// <summary>The workflow backdrop: the world grid and axes behind the surface.</summary>
public sealed class TemplateClass : VisualWorkflowGridDecorator
{
    /// <summary>Creates the backdrop and applies the palette.</summary>
    public TemplateClass()
    {
        Background = XamlRuntime.ParseColor("TemplateGridBackground");
        MinorLineColor = XamlRuntime.ParseColor("TemplateMinorGridColor");
        MajorLineColor = XamlRuntime.ParseColor("TemplateMajorGridColor");
        AxisColor = XamlRuntime.ParseColor("TemplateAxisColor");
        GridSpacing = ParseSpacing("TemplateGridSpacing");
        MajorLineEvery = int.Parse("TemplateMajorLineEvery", CultureInfo.InvariantCulture);
    }

    // Accept the "40d" spelling used by the grid parameters on the other platforms.
    private static double ParseSpacing(string text)
        => double.Parse(text.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
