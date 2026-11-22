// VeloxDev customization: the grid palette and spacing live here.
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace Demo.Views.Workflow;

/// <summary>The workflow backdrop: the world grid and axes behind the surface.</summary>
public sealed class GridDecorator : VisualWorkflowGridDecorator
{
    /// <summary>Creates the backdrop and applies the palette.</summary>
    public GridDecorator()
    {
        Background = XamlRuntime.ParseColor("#1E1E1E");
        MinorLineColor = XamlRuntime.ParseColor("#2A2D2E");
        MajorLineColor = XamlRuntime.ParseColor("#3A3D40");
        AxisColor = XamlRuntime.ParseColor("#4D4D4D");
        GridSpacing = ParseSpacing("40d");
        MajorLineEvery = int.Parse("5", CultureInfo.InvariantCulture);
    }

    // 模板参数允许 "40d" 这种带单位的写法，与其它平台的网格参数保持一致。
    private static double ParseSpacing(string text)
        => double.Parse(text.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
