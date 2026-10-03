using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow link view rendered as a cubic Bézier that leaves each port
/// horizontally, mirroring the WPF template's geometry. The curve derives from the
/// endpoint slot anchors; it spans the whole canvas so links are absolutely positioned
/// (overflow visible) and redraw whenever the endpoints move.
/// </summary>
public partial class LinkView : ComponentBase, IDisposable
{
    // Minimum control-point pull: two ports close together would otherwise degenerate the curve
    // into a straight segment and lose the horizontal exit at each end.
    private const double PullMinimum = 40;

    /// <summary>Gets or sets the link rendered by this view.</summary>
    [Parameter]
    public IWorkflowLinkViewModel? Link { get; set; }

    /// <summary>Gets or sets the canvas size the link spans.</summary>
    [Parameter]
    public double CanvasWidth { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height the link spans.</summary>
    [Parameter]
    public double CanvasHeight { get; set; } = 1080;

    /// <summary>Gets or sets an optional line-color override (defaults to <c>#DDFFFFFF</c>).</summary>
    [Parameter]
    public string? LineColorOverride { get; set; }

    /// <summary>Gets or sets an optional thickness override (defaults to <c>2</c>).</summary>
    [Parameter]
    public string? ThicknessOverride { get; set; }

    /// <summary>Gets or sets an explicit virtual-link override (defaults to the sender/receiver-parent heuristic).</summary>
    [Parameter]
    public bool? IsVirtualOverride { get; set; }

    /// <summary>Gets or sets an explicit visibility override (defaults to <see cref="IWorkflowLinkViewModel.IsVisible"/>).</summary>
    [Parameter]
    public bool? CanRenderOverride { get; set; }

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private string LineColor => LineColorOverride ?? ToCss("#DDFFFFFF");
    private double Thickness
    {
        get
        {
            if (ThicknessOverride is not null
                && double.TryParse(ThicknessOverride, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }

            return double.Parse("2", CultureInfo.InvariantCulture);
        }
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
            if (byte.TryParse(alpha, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var a))
            {
                return FormattableString.Invariant(
                    $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})");
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
    private bool CanRender { get; set; } = true;
    private bool IsVirtual { get; set; }

    private bool EffectiveCanRender => CanRenderOverride ?? CanRender;
    private bool EffectiveIsVirtual => IsVirtualOverride ?? IsVirtual;

    // Keep these invariant — the strings land in SVG/CSS attributes, where a culture's comma decimal
    // separator would serialize an unparseable value.
    private string CanvasWidthCss => CanvasWidth.ToString("0.#", CultureInfo.InvariantCulture);
    private string CanvasHeightCss => CanvasHeight.ToString("0.#", CultureInfo.InvariantCulture);
    private string ThicknessCss => Thickness.ToString("0.#", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Link);
    }

    private void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        CanRender = link.IsVisible;
        IsVirtual = IsVirtualLink(link);

        if (link is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnLinkChanged;
        }

        if (link.Sender is INotifyPropertyChanged s)
        {
            _senderNotifier = s;
            s.PropertyChanged += OnEndpointChanged;
        }

        if (link.Receiver is INotifyPropertyChanged r)
        {
            _receiverNotifier = r;
            r.PropertyChanged += OnEndpointChanged;
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            CanRender = Link?.IsVisible == true;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender)
            or nameof(IWorkflowLinkViewModel.Receiver)
            or null or "")
        {
            IsVirtual = IsVirtualLink(Link);
        }

        InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (IsVirtualOverride is not null || CanRenderOverride is not null)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private bool IsVirtualLink(IWorkflowLinkViewModel? link)
        => link is null || (link.Sender?.Parent is null && link.Receiver?.Parent is null);

    // Extension point: the control points set the curve's shape. Both are pulled horizontally by
    // max(PullMinimum, |dx| / 2), which is what makes the line leave each port horizontally — keep
    // that property if you replace the formula, and mirror any change in the adapter's zoom JS,
    // which re-formats this same curve while a wheel zoom collapses the nodes.
    private string BuildCurve()
    {
        var link = Link;
        if (link is null) return "";

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null) return "";

        // NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before
        // the GUI measures the endpoints would serialize NaN coordinates and paint a stale
        // frame that jumps back once measurement lands — the first-entry flicker the XAML
        // adapters guard against via WorkflowLinkRenderEx.IsRenderReady(). Skip until both
        // non-virtual endpoints are measured. Placeholder endpoints (Parent is null, e.g. the
        // VirtualLink gesture) are exempt and render immediately.
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link)) return "";

        double sx = sender.Anchor.Horizontal;
        double sy = sender.Anchor.Vertical;
        double ex = receiver.Anchor.Horizontal;
        double ey = receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the
        // reset intermediate frames (Reset nulls the anchors before clearing IsVisible), which
        // would serialize a "NaN,NaN" path. Suppress until the coordinates are real.
        if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsNaN(ex) || double.IsNaN(ey)) return "";

        double dx = ex - sx;
        double pull = Math.Max(PullMinimum, Math.Abs(dx) * 0.5);

        // Invariant: the path is SVG, and the adapter's zoom JS rewrites this same attribute with
        // '.' separators — a culture-dependent format would make the two disagree every frame.
        return FormattableString.Invariant(
            $"M {sx:F1},{sy:F1} C {sx + pull:F1},{sy:F1} {ex - pull:F1},{ey:F1} {ex:F1},{ey:F1}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnLinkChanged;
            _notifier = null;
        }

        if (_senderNotifier is not null)
        {
            _senderNotifier.PropertyChanged -= OnEndpointChanged;
            _senderNotifier = null;
        }

        if (_receiverNotifier is not null)
        {
            _receiverNotifier.PropertyChanged -= OnEndpointChanged;
            _receiverNotifier = null;
        }
    }
}
