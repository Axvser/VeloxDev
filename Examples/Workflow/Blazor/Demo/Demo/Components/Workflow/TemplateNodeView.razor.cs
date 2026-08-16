using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow node card that renders its title, optional body content, and
/// input/output slot hosts. The card drags via <c>WorkflowNodeDragBehavior</c> and
/// re-measures its slots via <c>WorkflowSlotLayoutBehavior</c> (both wired in the
/// .razor markup). Slots are supplied through <see cref="InputSlots"/> and
/// <see cref="OutputSlots"/> render fragments so consumers can drop slot views
/// (see <c>SlotView</c>) or full connection behaviors into the hosts.
/// </summary>
public partial class TemplateNodeView : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the node rendered by this view.</summary>
    [Parameter]
    public IWorkflowNodeViewModel? Node { get; set; }

    /// <summary>Gets or sets the workflow tree that owns connection gestures.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets type-specific body content (inputs, labels, etc.).</summary>
    [Parameter]
    public RenderFragment? BodyContent { get; set; }

    /// <summary>Gets or sets the input-slot host content (slot views wired to input slots).</summary>
    [Parameter]
    public RenderFragment? InputSlots { get; set; }

    /// <summary>Gets or sets the output-slot host content (slot views wired to output slots).</summary>
    [Parameter]
    public RenderFragment? OutputSlots { get; set; }

    /// <summary>Gets or sets an optional display-title override (falls back to the node's Name/Title).</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>Gets or sets an optional card background override (defaults to <c>#DDFFFFFF</c>).</summary>
    [Parameter]
    public string? Background { get; set; }

    /// <summary>Gets or sets an optional header foreground override (defaults to <c>#DD1E1E1E</c>).</summary>
    [Parameter]
    public string? Foreground { get; set; }

    /// <summary>Gets or sets an optional border brush override (defaults to <c>#331E1E1E</c>).</summary>
    [Parameter]
    public string? BorderBrush { get; set; }

    /// <summary>Gets or sets an optional border thickness override (defaults to <c>1</c>).</summary>
    [Parameter]
    public string? BorderThickness { get; set; }

    /// <summary>Gets or sets an optional corner radius override (defaults to <c>6</c>).</summary>
    [Parameter]
    public string? CornerRadius { get; set; }

    /// <summary>Gets or sets whether the card may paint slots outside its bounds (default false).</summary>
    [Parameter]
    public bool AllowOverflow { get; set; }

    private INotifyPropertyChanged? _notifier;
    private string _title = "";

    // Execution feedback: read reflectively from the node view-model (present on the demo's
    // NodeViewModel, BoolSelector and EnumSelector) so any card can show which step it ran in
    // (#N order badge) and glow while it is executing, mirroring the XAML adapters.
    private bool _isRunning;
    private bool _hasOrderBadge;
    private string _orderText = "";
    private bool _hasLoadBadge;
    private string _loadText = "";

    // Colors mirror NodeViewModel's running chrome (#FFD54A accent on amber-tinted surfaces) so
    // the Blazor card matches the WinForms/WPF running look.
    private const string RunningAccent = "#FFD54A";
    private const string RunningHeader = "#413612";
    private const string RunningCardBg = "#2D2817";
    private const string RunningDivider = "rgba(255,213,74,0.35)";

    private bool IsRunning => _isRunning;
    private bool HasOrderBadge => _hasOrderBadge;
    private string OrderText => _orderText;
    private bool HasLoadBadge => _hasLoadBadge;
    private string LoadText => _loadText;

    private string BackgroundCss => Background ?? ToCss("#DDFFFFFF");
    private string ForegroundCss => Foreground ?? ToCss("#DD1E1E1E");
    private string BorderBrushCss => _isRunning ? RunningAccent : (BorderBrush ?? ToCss("#331E1E1E"));
    private string BorderThicknessCss => WithCssUnits(BorderThickness ?? "1", "px");
    private string CornerRadiusCss => WithCssUnits(CornerRadius ?? "6", "px");
    private string CardBackgroundCss => _isRunning ? RunningCardBg : BackgroundCss;
    private string HeaderBackgroundCss => _isRunning ? RunningHeader : "transparent";
    private string HeaderDividerCss => _isRunning ? RunningDivider : "rgba(255,255,255,0.08)";

    /// <summary>
    /// Appends <paramref name="suffix"/> to a CSS length placeholder unless it already carries
    /// CSS units, so XAML-style symbol values (<c>1</c>, <c>6</c>) become valid CSS lengths.
    /// </summary>
    private static string WithCssUnits(string value, string suffix)
    {
        var text = value.Trim();
        if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("%", StringComparison.Ordinal)
            || text.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("em", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return text + suffix;
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

    private string FallbackTitle => _title;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        SyncTitle();
        SyncExecutionState();
        if (Node is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnNodeChanged;
        }
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Skip geometry: Anchor/Size fires every frame while the node is dragged (MoveCommand +
        // slot re-measure). The position is owned by WorkflowNodeDragBehavior via JS, and the card
        // does not depend on it, so re-rendering here is pure waste. Everything else re-renders so
        // the execution state (IsRunning, LastExecutionOrder, RunCount/WaitCount) drives the step
        // badge and running highlight live during a run.
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            return;
        }

        if (e.PropertyName is "Name" or "Title" or null or "")
        {
            SyncTitle();
        }

        SyncExecutionState();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Reads the node's execution feedback state. <see cref="IWorkflowNodeViewModel"/> does not
    /// expose it, so the common properties are looked up reflectively (same pattern as
    /// <see cref="SyncTitle"/>); missing properties simply stay at their defaults.
    /// </summary>
    private void SyncExecutionState()
    {
        _isRunning = ReadBool("IsRunning");
        _hasOrderBadge = ReadBool("HasExecutionOrder");
        _orderText = ReadString("ExecutionOrderText");
        _hasLoadBadge = ReadBool("HasWorkLoad");
        _loadText = ReadString("WorkLoadText");
    }

    private object? Read(string property)
        => Node?.GetType().GetProperty(property)?.GetValue(Node);

    private bool ReadBool(string property)
        => Read(property) is true;

    private string ReadString(string property)
        => Read(property)?.ToString() ?? "";

    /// <summary>
    /// Reads the display title from the node. <see cref="IWorkflowNodeViewModel"/> does not
    /// expose a name, so look up a <c>Name</c> or <c>Title</c> property reflectively
    /// (works with any node view-model, including the built-in VeloxDev samples).
    /// </summary>
    private void SyncTitle()
    {
        if (Node is null)
        {
            _title = "";
            return;
        }

        var property = Node.GetType().GetProperty("Name") ?? Node.GetType().GetProperty("Title");
        _title = property?.GetValue(Node)?.ToString() ?? "";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnNodeChanged;
            _notifier = null;
        }
    }
}
