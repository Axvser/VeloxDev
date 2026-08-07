using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow slot view drawn from the <c>M517.3248,511.488 m-123.6992,0 a123.6992,123.6992 0 1 0 247.3984,0 a123.6992,123.6992 0 1 0 -247.3984,0 Z M366.848,991.5904 a47.2064,47.2064 0 0 1 -15.36,-2.5088 A506.368,506.368 0 0 1 32.8704,655.36 a46.08,46.08 0 1 1 88.32,-26.2144 A414.0544,414.0544 0 0 0 383.9616,928.2048 a46.08,46.08 0 0 1 -15.104,89.6 Z M648.2944,997.888 a46.08,46.08 0 0 1 -13.1072,-90.2656 A413.952,413.952 0 0 0 920.9344,646.8608 a46.08,46.08 0 1 1 87.04,30.208 A506.3168,506.3168 0 0 1 674.5088,996.9408 a45.2608,45.2608 0 0 1 -13.1072,1.9456 Z M957.44,426.5984 a46.08,46.08 0 0 1 -44.1344,-32.9728 A414.0544,414.0544 0 0 0 652.544,120.9728 a46.08,46.08 0 1 1 30.1568,-87.04 A506.368,506.368 0 0 1 991.6416,467.3984 a46.08,46.08 0 0 1 -31.0272,57.2928 a45.2608,45.2608 0 0 1 -13.1584,1.8944 Z M83.3024,407.0912 a46.08,46.08 0 0 1 -43.5712,-61.44 A506.4704,506.4704 0 0 1 373.248,26.9824 a46.08,46.08 0 1 1 26.112,88.3712 A413.952,413.952 0 0 0 100.7104,367.0528 a46.08,46.08 0 0 1 -43.52,31.0272 Z</c> SVG path data.
/// The fill/stroke colors follow the WPF template contract:
/// Sender + Receiver = Violet, Sender = Tomato, Receiver = Lime, otherwise the
/// standby <c>#DD1E1E1E</c>. Wrapping itself in
/// <see cref="WorkflowSlotConnectionBehavior"/> makes the slot participate in
/// link creation (drag from slot to slot).
/// </summary>
public partial class TemplateSlotView : ComponentBase, IDisposable
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
