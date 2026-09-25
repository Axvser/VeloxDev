using System.ComponentModel;
using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow node card that renders its title, optional body content, and
/// input/output slot hosts. The card drags via <c>WorkflowNodeDragBehavior</c> and
/// re-measures its slots via <c>WorkflowSlotLayoutBehavior</c> (both wired in the
/// .razor markup). Slots are supplied through <see cref="InputSlots"/> and
/// <see cref="OutputSlots"/> render fragments so consumers can drop slot views
/// (see <c>TemplateSlotView</c>) or full connection behaviors into the hosts.
/// <para>
/// The chrome itself — surface, hairline, the 2px type accent, the 32px title row, the
/// readout capsules — is the shared card design system in <c>app.css</c>, ported from the
/// Avalonia demo. A card names its type through <see cref="Accent"/> and never paints its
/// own surface; the five cards therefore cannot drift apart, which is why the palette is
/// declared once rather than five times.
/// </para>
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

    /// <summary>
    /// Gets or sets the 2px type accent drawn at the head of the title row. Comes from the node's
    /// own card, exactly as each Avalonia card hands its own accent to CardTheme: Controller is
    /// <c>var(--wf-accent-controller)</c>, a worker (Timer/Python) <c>var(--wf-accent-worker)</c>,
    /// the enum router <c>var(--wf-accent-enum)</c>, and a foreign type falls back to the neutral
    /// slate. Defaults to that slate, because a card with no type to declare should not claim one.
    /// </summary>
    [Parameter]
    public string? Accent { get; set; }

    /// <summary>Gets or sets an optional right-hand status capsule (a word, not a number).</summary>
    [Parameter]
    public string? StatusText { get; set; }

    /// <summary>
    /// Gets or sets the name of a node property to read as the status capsule when <see cref="StatusText"/>
    /// is not given. A live capsule has to be read on the node's own change notification — the page that
    /// hosts the card does not re-render when a node's status changes, so a value handed down as a
    /// parameter would freeze at whatever it was when the page last rendered.
    /// </summary>
    [Parameter]
    public string? StatusProperty { get; set; }

    /// <summary>Gets or sets an optional card background override (defaults to the shared card surface).</summary>
    [Parameter]
    public string? Background { get; set; }

    /// <summary>Gets or sets an optional header foreground override.</summary>
    [Parameter]
    public string? Foreground { get; set; }

    /// <summary>Gets or sets an optional border brush override (defaults to the shared hairline).</summary>
    [Parameter]
    public string? BorderBrush { get; set; }

    /// <summary>Gets or sets an optional border thickness override (defaults to <c>1</c>).</summary>
    [Parameter]
    public string? BorderThickness { get; set; }

    /// <summary>Gets or sets an optional corner radius override (defaults to <c>8</c>).</summary>
    [Parameter]
    public string? CornerRadius { get; set; }

    /// <summary>Gets or sets whether the card may paint slots outside its bounds (default false).</summary>
    [Parameter]
    public bool AllowOverflow { get; set; }

    private INotifyPropertyChanged? _notifier;
    private TreeViewModel? _tree;
    private string _title = "";

    // Execution feedback. The order badge and the status capsule come off the node view-model
    // (looked up reflectively, same as the title); "running" does NOT — it comes off the tree,
    // because that is where the flag lives and where it is written. See SyncExecutionState.
    private bool _isRunning;
    private bool _hasOrderBadge;
    private string _orderText = "";
    private string _statusText = "";

    private bool IsRunning => _isRunning;
    private bool HasOrderBadge => _hasOrderBadge;
    private string OrderText => _orderText;

    /// <summary>The capsule text: the explicit override, else the live property, else nothing.</summary>
    private string StatusValue => StatusText ?? _statusText;

    // Design-canvas geometry: the card is authored at the node type's DESIGN size and uniformly
    // scaled into the collapsed Node.Size host via a CSS transform (the Blazor equivalent of the
    // XAML Viewbox). The design size is single-sourced from the type's [DefaultSize] attribute;
    // node types without a DefaultSize (e.g. the generic catch-all card) fall back to their live
    // Size, which is the raw size at scale 1.
    private IWorkflowNodeViewModel? _designNode;
    private (double Width, double Height) _design = (260, 180);

    /// <summary>The type accent, or the neutral slate when the card declares no type.</summary>
    private string AccentCss => string.IsNullOrWhiteSpace(Accent) ? "var(--wf-accent-fallback)" : Accent!;

    /// <summary>Only the ports' host is allowed to paint outside the card, and every card asks for it.</summary>
    private string OverflowCss => AllowOverflow ? "visible" : "hidden";

    /// <summary>
    /// The card's surface, edge and corner all come from the shared <c>.wf-card</c> class; these
    /// parameters exist only to let a one-off card override them without a new stylesheet rule.
    /// Leaving them unset is the normal case, and then not one of them reaches the DOM.
    /// </summary>
    private string SurfaceOverrideCss
    {
        get
        {
            var css = "";
            if (Background is not null) css += $"background:{ToCss(Background)};";
            if (BorderBrush is not null) css += $"border-color:{ToCss(BorderBrush)};";
            if (BorderThickness is not null) css += $"border-width:{WithCssUnits(BorderThickness, "px")};";
            if (CornerRadius is not null) css += $"border-radius:{WithCssUnits(CornerRadius, "px")};";
            return css;
        }
    }

    private (double Width, double Height) Design
    {
        get
        {
            if (!ReferenceEquals(_designNode, Node))
            {
                _designNode = Node;
                _design = ResolveDesignSize();
            }

            return _design;
        }
    }

    /// <summary>CSS scale factor for the design-size card = the zoom collapse factor
    /// (collapsed width / design width). 1 at scale 1, 0.5 at scale 2.</summary>
    private string ScaleCss
    {
        get
        {
            if (Node is null) return "1";
            var width = Node.Size.Width;
            var designWidth = Design.Width;
            if (width <= 0 || designWidth <= 0) return "1";
            return (width / designWidth).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private (double Width, double Height) ResolveDesignSize()
    {
        var node = Node;
        if (node is not null)
        {
            // [DefaultSize] is the single source of each node type's authored design size.
            if (Attribute.GetCustomAttribute(node.GetType(), typeof(DefaultSizeAttribute))
                    is DefaultSizeAttribute d && d.Width > 0 && d.Height > 0)
            {
                return (d.Width, d.Height);
            }

            // No DefaultSize (e.g. a generic catch-all card): the raw size at scale 1 is the design size.
            return (node.Size.Width > 0 ? node.Size.Width : 260,
                    node.Size.Height > 0 ? node.Size.Height : 180);
        }

        return (260, 180);
    }

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
        // Skip Anchor: it fires every frame while the node is dragged (MoveCommand + slot
        // re-measure), the position is owned by WorkflowNodeDragBehavior via JS, and the card
        // does not depend on it, so re-rendering here is pure waste. Size is different: it changes
        // on zoom collapse, and the card must re-render to refresh its design-canvas CSS scale.
        // Everything else re-renders so the execution state (IsRunning, LastExecutionOrder,
        // RunCount/WaitCount) drives the step badge and running highlight live during a run.
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor))
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
    /// Reads the node's execution feedback state.
    /// </summary>
    /// <remarks>
    /// The order badge and the status capsule come off the node view-model reflectively (same pattern as
    /// <see cref="SyncTitle"/>). <c>IsRunning</c> does not: no node view-model has such a property — the flag
    /// belongs to the tree, and the engine drives nodes one at a time without reporting a per-node event a
    /// card could read. So the pill says "a run is in progress on this canvas", which is the only thing that
    /// is actually known here.
    /// </remarks>
    private void SyncExecutionState()
    {
        // Re-resolved every pass: a card is recycled for another node, and its tree comes with it.
        var tree = Node?.Parent as TreeViewModel;
        if (!ReferenceEquals(_tree, tree))
        {
            if (_tree is not null) _tree.PropertyChanged -= OnTreeChanged;
            _tree = tree;
            if (_tree is not null) _tree.PropertyChanged += OnTreeChanged;
        }

        _isRunning = tree?.IsWorkflowRunning == true;
        _hasOrderBadge = ReadBool("HasExecutionOrder");
        _orderText = ReadString("ExecutionOrderText");
        _statusText = StatusProperty is null ? "" : ReadString(StatusProperty);
    }

    private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(TreeViewModel.IsWorkflowRunning)) return;

        SyncExecutionState();
        _ = InvokeAsync(StateHasChanged);
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

        if (_tree is not null)
        {
            _tree.PropertyChanged -= OnTreeChanged;
            _tree = null;
        }
    }
}
