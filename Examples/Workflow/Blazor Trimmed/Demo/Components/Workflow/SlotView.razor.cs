using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow slot view drawn from the default SVG path data.
/// The fill/stroke colors follow the WPF template contract:
/// Sender + Receiver = Violet, Sender = Tomato, Receiver = Lime, otherwise the
/// standby color. Wrapping itself in
/// <see cref="WorkflowSlotConnectionBehavior"/> makes the slot participate in
/// link creation (drag from slot to slot).
/// </summary>
public partial class SlotView : ComponentBase, IDisposable
{
    private const double IconSize = 20;
    private const double ViewBoxSize = 1024;

    /// <summary>Gets or sets the slot rendered by this view.</summary>
    [Parameter]
    public IWorkflowSlotViewModel? Slot { get; set; }

    /// <summary>Gets or sets the workflow tree that owns connection gestures.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets extra styles for the connection-behavior wrapper.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Gets or sets the rendered size (pixels).</summary>
    [Parameter]
    public double SlotSize { get; set; } = IconSize;

    private INotifyPropertyChanged? _notifier;

    private string SlotSizeCss => SlotSize.ToString("0.#");

    private string FillColor => SlotStateColor(Slot?.State ?? SlotState.StandBy);
    private string StrokeColor => ToCss("#FFFFFFFF");

    private static string SlotStateColor(SlotState state)
    {
        // WPF template contract: Sender+Receiver → Violet, Sender → Tomato,
        // Receiver → Lime, else the standby color.
        if (state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver))
        {
            return "#EE82EE";
        }

        if (state.HasFlag(SlotState.Sender))
        {
            return "#FF6347";
        }

        if (state.HasFlag(SlotState.Receiver))
        {
            return "#32CD32";
        }

        return ToCss("#DD1E1E1E");
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
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State) or null or "")
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
