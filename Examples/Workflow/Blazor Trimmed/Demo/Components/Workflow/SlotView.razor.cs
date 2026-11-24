// VeloxDev customization: Add connector-specific parameters here only when the platform behavior does not already cover it.
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow slot view drawn from the <c>TemplateSlotPath</c> SVG path data.
/// The fill follows the shared <see cref="WorkflowPresentation.SlotStateColor"/> mapping; wrapping the
/// glyph in <see cref="WorkflowSlotConnectionBehavior"/> makes it participate in link creation.
/// </summary>
public partial class SlotView : ComponentBase, IDisposable
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

    private INotifyPropertyChanged? _notifier;

    // The SVG width/height attributes are parsed by the browser — keep the '.' decimal separator.
    private string SlotSizeCss => SlotSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        if (Slot is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnSlotChanged;
        }
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISlotComponent.State) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotChanged;
            _notifier = null;
        }
    }
}
