using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

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

    // VeloxDev customization: 悬停高亮 —— 本 demo 自己订这条线的输入事件实现，
    // 模板里那份没有这一段。只改这一条 path 的描边：新增元素拿不到 data-veloxdev-link-curve，
    // 缩放的 JS 不跟它，会画出一条不跟手的重影。
    private const string HighlightColor = "#FFFFFFFF";
    private const double HighlightWidthBonus = 1.5;

    private bool _hover;

    /// <summary>
    /// Whether the pointer is on this link. This view drives it itself, from the tree's
    /// input route — see <see cref="OnPointerEntered"/>.
    /// </summary>
    public bool IsHighlighted
    {
        get => _hover;
        set
        {
            if (_hover == value) return;
            _hover = value;
            _ = InvokeAsync(StateHasChanged);
        }
    }

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

    // 高亮只换这条 path 的颜色与线宽：线本身是白的这版，高亮取更亮的浅青
    private string StrokeColor => _hover ? HighlightColor : LineColor;
    private string StrokeWidthCss => (_hover ? Thickness + HighlightWidthBonus : Thickness).ToString("0.#", CultureInfo.InvariantCulture);

    private void OnPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, WorkflowPointerExitedEventArgs e) => IsHighlighted = false;

    // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删、怎么删由这里写。
    private void OnKeyDown(object? sender, WorkflowKeyDownEventArgs e)
    {
        if (e.Key != WorkflowKey.Delete) return;
        if (e.Handle.PreventDefault) return;
        if ((Link) is { } link && link.DeleteCommand.CanExecute(null)) link.DeleteCommand.Execute(null);
    }

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

        // 悬停高亮是本 demo 的：订**这条线自己的** Helper 就够了 —— 路由会告诉它指针什么时候进来、
        // 什么时候离开，这里不必再去比 target 是谁。
        if (link.GetHelper() is IWorkflowInputEvents events)
        {
            events.Input.PointerEntered += OnPointerEntered;
            events.Input.PointerExited += OnPointerExited;
        events.Input.KeyDown += OnKeyDown;
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

        // NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before
        // the GUI measures the endpoints would serialize NaN coordinates and paint a stale
        // frame that jumps back once measurement lands — the first-entry flicker the XAML
        // adapters guard against via WorkflowLinkRenderEx.IsRenderReady(). Skip until both
        // non-virtual endpoints are measured. Placeholder endpoints (Parent is null, e.g. the
        // VirtualLink gesture) are exempt and render immediately.
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link))
        {
            link.PublishCurve(null);
            return "";
        }

        double sx = sender.Anchor.Horizontal;
        double sy = sender.Anchor.Vertical;
        double ex = receiver.Anchor.Horizontal;
        double ey = receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the
        // reset intermediate frames (Reset nulls the anchors before clearing IsVisible), which
        // would serialize a "NaN,NaN" path. Suppress until the coordinates are real.
        if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsNaN(ex) || double.IsNaN(ey))
        {
            link.PublishCurve(null);
            return "";
        }

        // The control points come from Core so the drawn path and the published hit-test curve cannot
        // describe different shapes; both leave each port along that port's own edge normal.
        var points = LinkCurve.LinkCurvePoints(link, sx, sy, ex, ey, PullMinimum);
        _curve = LinkCurve.BuildLinkCubic(link, sx, sy, ex, ey, PullMinimum, LinkCurve.DefaultSampleCount);
        link.PublishCurve(_curve, this);

        // Invariant: the path is SVG, and the adapter's zoom JS rewrites this same attribute with
        // '.' separators — a culture-dependent format would make the two disagree every frame.
        return FormattableString.Invariant(
            $"M {points[0].X:F1},{points[0].Y:F1} C {points[1].X:F1},{points[1].Y:F1} {points[2].X:F1},{points[2].Y:F1} {points[3].X:F1},{points[3].Y:F1}");
    }

    // Forwarding the pointer into the hub is what makes the link interactive: the hub decides which link
    // is under the pointer; the hover highlight and Delete are this demo's own
    // (OnPointerEntered). Nothing here decides anything — the browser's stroke-only hit region is the outer
    // gate, and the hub is the judge.
    private async Task OnPointerEnter(MouseEventArgs e)
    {
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(SurfacePointerKind.Entered, e.ClientX, e.ClientY, target: Link);
        }
    }

    private async Task OnPointerExit(MouseEventArgs e)
    {
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(SurfacePointerKind.Exited, e.ClientX, e.ClientY, target: Link);
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

        // 视图比树活得短：退了订，路由不会往一个已经走掉的渲染器里发事件
        if (Link?.GetHelper() is IWorkflowInputEvents events)
        {
            events.Input.PointerEntered -= OnPointerEntered;
            events.Input.PointerExited -= OnPointerExited;
        events.Input.KeyDown -= OnKeyDown;
        }
    }
}
