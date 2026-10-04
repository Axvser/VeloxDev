// VeloxDev customization: Customize line geometry, color, and thickness via the override parameters below.
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A Blazor link view rendered as a cubic Bézier that leaves each port horizontally,
/// mirroring the WPF template's geometry. The curve derives from the endpoint slot anchors;
/// it spans the whole canvas and redraws whenever the endpoints move.
/// </summary>
public partial class TemplateClass : ComponentBase, IDisposable
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

    /// <summary>Gets or sets an optional line-color override (defaults to <c>TemplateLinkColor</c>).</summary>
    [Parameter]
    public string? LineColorOverride { get; set; }

    /// <summary>Gets or sets an optional thickness override (defaults to <c>TemplateLinkThickness</c>).</summary>
    [Parameter]
    public string? ThicknessOverride { get; set; }

    /// <summary>Gets or sets an explicit virtual-link override (defaults to the sender/receiver-parent heuristic).</summary>
    [Parameter]
    public bool? IsVirtualOverride { get; set; }

    /// <summary>Gets or sets an explicit visibility override (defaults to <see cref="IWorkflowLinkViewModel.IsVisible"/>).</summary>
    [Parameter]
    public bool? CanRenderOverride { get; set; }

    // The surface cascades itself down, so a link view drawn inside one can forward the pointer
    // into the shared interaction hub; null when the view is rendered outside a surface.
    [CascadingParameter]
    private WorkflowSurfaceBehavior? Surface { get; set; }

    // The hit area is only the painted stroke: the canvas-sized svg itself stays click-through, and a
    // virtual link (the rubber band under the pointer) is not a target at all.
    private string HitTargetCss => EffectiveIsVirtual ? "none" : "stroke";

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;
    private LinkCurve? _curve;

    private string LineColor => LineColorOverride ?? ToCss("TemplateLinkColor");
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

            return double.Parse("TemplateLinkThickness", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> colors (template symbol defaults) to CSS color
    /// values; passes named colors and <c>rgb()/rgba()</c> strings through unchanged.
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
    // which re-formats this same curve while a wheel zoom collapses the nodes. The same control
    // points also feed the LinkCurve published for hit testing, so keep the two in step.
    private string BuildCurve()
    {
        var link = Link;
        if (link is null) return "";

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null)
        {
            link.PublishCurve(null);
            return "";
        }

        // NaN gate: slot anchors are NaN until the GUI measures them; skip real links until
        // ready (virtual-link placeholders, Parent is null, are exempt).
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link))
        {
            link.PublishCurve(null);
            return "";
        }

        double sx = sender.Anchor.Horizontal;
        double sy = sender.Anchor.Vertical;
        double ex = receiver.Anchor.Horizontal;
        double ey = receiver.Anchor.Vertical;

        // Virtual-link placeholders can still carry NaN anchors on reset frames; suppress
        // until the coordinates are real.
        if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsNaN(ex) || double.IsNaN(ey))
        {
            link.PublishCurve(null);
            return "";
        }

        double dx = ex - sx;
        double pull = Math.Max(PullMinimum, Math.Abs(dx) * 0.5);

        _curve = LinkCurve.BuildCubic(sx, sy, ex, ey, PullMinimum, LinkCurve.DefaultSampleCount);
        link.PublishCurve(_curve, this);

        // Invariant: the path is SVG, and the adapter's zoom JS rewrites this same attribute with
        // '.' separators — a culture-dependent format would make the two disagree every frame.
        return FormattableString.Invariant(
            $"M {sx:F1},{sy:F1} C {sx + pull:F1},{sy:F1} {ex - pull:F1},{ey:F1} {ex:F1},{ey:F1}");
    }

    // Forwarding the pointer into the hub is what makes the link interactive: the hub decides which link
    // is under the pointer and deletes it on Delete (AutoDelete). Nothing here decides anything — the
    // browser's stroke-only hit region is the outer gate, and the route is the judge. These two handlers also
    // hand over this view's own link as the target, so a hover subscriber hears the event on the link itself.
    private async Task OnPointerEnter(MouseEventArgs e)
    {
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(
                SurfacePointerKind.Entered, e.ClientX, e.ClientY, target: Link);
        }
    }

    private async Task OnPointerExit(MouseEventArgs e)
    {
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(
                SurfacePointerKind.Exited, e.ClientX, e.ClientY, target: Link);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Link?.PublishCurve(null);

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
