using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow minimap overlay that renders a thumbnail overview of the surface's
/// nodes plus the visible viewport rectangle. Wraps the adapter's
/// <see cref="WorkflowMinimapOverlay"/> with the default symbol-driven colors.
/// </summary>
public partial class MinimapOverlay : ComponentBase
{
    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>Gets or sets the minimap width in pixels.</summary>
    [Parameter]
    public double Width { get; set; } = 180;

    /// <summary>Gets or sets the minimap height in pixels.</summary>
    [Parameter]
    public double Height { get; set; } = 120;

    /// <summary>Gets or sets the id of the surface scroll container that navigation should scroll.</summary>
    [Parameter]
    public string? ScrollViewerId { get; set; }

    private string Background { get; } = ToCss("#D2141922");
    private string Border { get; } = ToCss("#DC94A3B8");
    private string NodeFill { get; } = ToCss("#DC38BDF8");
    private string ViewportStroke { get; } = ToCss("#F0FFFFFF");

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
