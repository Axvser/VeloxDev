using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow grid decorator that mirrors the surface's scroll/content offset as
/// ruler bars (tick marks), driven by the default colors/spacings below. Renders the
/// adapter's <see cref="WorkflowGridDecorator"/> with these values; the grid background
/// itself is drawn by the workflow surface CSS.
/// </summary>
public partial class GridDecorator : ComponentBase
{
    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>Gets or sets the ruler thickness in pixels.</summary>
    [Parameter]
    public double RulerThickness { get; set; } = 28;

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = ParseGridValue("40d");

    /// <summary>Gets or sets the number of minor cells between major lines.</summary>
    [Parameter]
    public int MajorLineEvery { get; set; } = int.Parse("5", System.Globalization.CultureInfo.InvariantCulture);

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

    private string RulerBackgroundCss => RulerBackground ?? ToCss("#C8252526");
    private string RulerTickColorCss => RulerTickColor ?? ToCss("#555555");
    private string RulerLabelColorCss => RulerLabelColor ?? ToCss("#888888");
    private string RulerDividerColorCss => RulerDividerColor ?? ToCss("#3A3D40");
    private string AxisColorCss => AxisColor ?? ToCss("#4D4D4D");

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
    /// Converts XAML-style <c>#AARRGGBB</c> color literals (as used by the template symbols)
    /// into CSS color values, so symbol-driven colors work in Razor views. Also passes
    /// through named colors and CSS <c>rgb()/rgba()</c> strings unchanged.
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
