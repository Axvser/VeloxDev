// VeloxDev customization: Add connector-specific parameters here only when the platform behavior does not already cover it.
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow slot view drawn as a filled SVG circle. The fill and stroke are fixed colours rather than a
/// state mapping; wrapping the glyph in <see cref="WorkflowSlotConnectionBehavior"/> makes it participate in link
/// creation.
/// </summary>
public partial class SlotView : ComponentBase
{
    private const double IconSize = 20;

    /// <summary>Gets or sets the slot rendered by this view.</summary>
    [Parameter]
    public ISlotComponent? Slot { get; set; }

    /// <summary>Gets or sets the workflow tree that owns connection gestures.</summary>
    [Parameter]
    public ITreeComponent? Tree { get; set; }

    /// <summary>Gets or sets extra styles for the connection-behavior wrapper.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Gets or sets the rendered size (pixels).</summary>
    [Parameter]
    public double SlotSize { get; set; } = IconSize;

    // The SVG width/height attributes are parsed by the browser — keep the '.' decimal separator.
    private string SlotSizeCss => SlotSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
