using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A Blazor grid decorator mirroring the surface's scroll/content offset as ruler tick
/// bars, driven by the color/spacing parameters below. Renders the adapter's
/// <see cref="WorkflowGridDecorator"/>; the grid background is drawn by the surface CSS.
/// </summary>
public partial class TemplateClass : ComponentBase
{
    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>Gets or sets the ruler thickness in pixels.</summary>
    [Parameter]
    public double RulerThickness { get; set; } = 28;

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = ParseGridValue("TemplateGridSpacing");

    /// <summary>Gets or sets the number of minor cells between major lines.</summary>
    [Parameter]
    public int MajorLineEvery { get; set; } = int.Parse("TemplateMajorLineEvery", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets or sets the ruler background color.</summary>
    [Parameter]
    public string? RulerBackground { get; set; }

    /// <summary>Gets or sets the ruler tick color.</summary>
    [Parameter]
    public string? RulerTickColor { get; set; }

    /// <summary>Gets or sets the ruler label color.</summary>
    [Parameter]
    public string? RulerLabelColor { get; set; }

    /// <summary>Gets or sets the ruler divider color.</summary>
    [Parameter]
    public string? RulerDividerColor { get; set; }

    /// <summary>Gets or sets the axis (world 0) tick color.</summary>
    [Parameter]
    public string? AxisColor { get; set; }

    private string RulerBackgroundCss => RulerBackground ?? ToCss("TemplateRulerBackground");
    private string RulerTickColorCss => RulerTickColor ?? ToCss("TemplateRulerTickColor");
    private string RulerLabelColorCss => RulerLabelColor ?? ToCss("TemplateRulerLabelColor");
    private string RulerDividerColorCss => RulerDividerColor ?? ToCss("TemplateRulerDividerColor");
    private string AxisColorCss => AxisColor ?? ToCss("TemplateAxisColor");

    private static double ParseGridValue(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> colors (template symbol defaults) to CSS color
    /// values; passes named colors and <c>rgb()/rgba()</c> strings through unchanged.
    /// </summary>
    private static string ToCss(string value)
    {
        var text = value.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            var alpha = text.Substring(1, 2);
            var rgb = text.Substring(3);
            if (byte.TryParse(alpha, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})";
            }
        }

        if (text.Length == 7 && text[0] == '#')
        {
            return text;
        }

        return text;
    }

    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);
}
