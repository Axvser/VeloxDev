// VeloxDev customization: Add node-specific parameters or state here; workflow behavior is configured in the .razor markup.
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// A Blazor workflow node card that renders its title, optional body content, and
/// input/output slot hosts. The card drags via <c>WorkflowNodeDragBehavior</c> and
/// re-measures its slots via <c>WorkflowSlotLayoutBehavior</c> (both wired in the
/// .razor markup). Slots are supplied through <see cref="InputSlots"/> and
/// <see cref="OutputSlots"/> render fragments so consumers can drop slot views
/// (see <c>SlotView</c>) or full connection behaviors into the hosts.
/// </summary>
public partial class TemplateClass : ComponentBase, IDisposable
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

    /// <summary>Gets or sets an optional card background override (defaults to <c>TemplateNodeBackground</c>).</summary>
    [Parameter]
    public string? Background { get; set; }

    /// <summary>Gets or sets an optional header foreground override (defaults to <c>TemplateNodeForeground</c>).</summary>
    [Parameter]
    public string? Foreground { get; set; }

    /// <summary>Gets or sets an optional border brush override (defaults to <c>TemplateNodeBorderBrush</c>).</summary>
    [Parameter]
    public string? BorderBrush { get; set; }

    /// <summary>Gets or sets an optional border thickness override (defaults to <c>TemplateNodeBorderThickness</c>).</summary>
    [Parameter]
    public string? BorderThickness { get; set; }

    /// <summary>Gets or sets an optional corner radius override (defaults to <c>TemplateNodeCornerRadius</c>).</summary>
    [Parameter]
    public string? CornerRadius { get; set; }

    /// <summary>Gets or sets whether the card may paint slots outside its bounds (default false).</summary>
    [Parameter]
    public bool AllowOverflow { get; set; }

    private INotifyPropertyChanged? _notifier;
    private string _title = "";

    private string BackgroundCss => Background ?? ToCss("TemplateNodeBackground");
    private string ForegroundCss => Foreground ?? ToCss("TemplateNodeForeground");
    private string BorderBrushCss => BorderBrush ?? ToCss("TemplateNodeBorderBrush");
    private string BorderThicknessCss => WithCssUnits(BorderThickness ?? "TemplateNodeBorderThickness", "px");
    private string CornerRadiusCss => WithCssUnits(CornerRadius ?? "TemplateNodeCornerRadius", "px");

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
        if (Node is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnNodeChanged;
        }
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Name" or "Title" or null or "")
        {
            SyncTitle();
            InvokeAsync(StateHasChanged);
        }
    }

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
