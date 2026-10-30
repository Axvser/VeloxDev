using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow link view: one cubic curve that leaves each end horizontally, with a comet
/// travelling along it.
/// <para>
/// The comet is a bright head, a tail that fades behind it, and a halo that follows the head — and it
/// is cut out of the curve <b>by arc length</b> rather than by a gradient brush. That is the whole
/// reason this view keeps its own sample table: a <c>LinearGradientBrush</c>'s axis is the straight
/// line between the two ends, so on a curve it lights the string rather than the rope.
/// </para>
/// <para>
/// The animation itself is not a Blazor render. The markup paints the curve and 16 + 16 comet paths
/// once; <c>wwwroot/flow.js</c> then owns the per-frame geometry and alpha for every link through one
/// shared <c>requestAnimationFrame</c> loop. This component only registers the SVG after the first
/// render and pushes an update when the geometry, selection or colour changes.
/// </para>
/// <para>
/// Every number the SVG carries goes through <see cref="N"/>. Razor writes a bare <c>double</c> in the
/// current culture, and a comma decimal separator produces an attribute the browser silently drops —
/// which for a path means no path at all.
/// </para>
/// </summary>
public partial class TemplateLinkView : ComponentBase, IDisposable, IAsyncDisposable
{
    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
    private const double PullMinimum = 40;

    // 拖尾分几段画。flow.js 与这里共享同一个元素数：改这个常量必须同时改 JS 的 SEGMENTS。
    private const int TailSegments = 16;

    /// <summary>Gets or sets the link rendered by this view.</summary>
    [Parameter]
    public IWorkflowLinkViewModel? Link { get; set; }

    /// <summary>Gets or sets the canvas size the link spans.</summary>
    [Parameter]
    public double CanvasWidth { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height the link spans.</summary>
    [Parameter]
    public double CanvasHeight { get; set; } = 1080;

    /// <summary>Gets or sets an optional line-color override (defaults to <c>#CC38BDF8</c>).</summary>
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

    /// <summary>Gets or sets whether the owning surface holds this link as its current selection.</summary>
    /// <remarks>
    /// The surface owns the selection because Delete and the right-click menu both act on it. This view
    /// keeps only a local hover flag for the highlight; the selection has to outlive the hover, or the
    /// highlight would drop the moment the pointer leaves the curve for the menu.
    /// </remarks>
    [Parameter]
    public bool IsSelected { get; set; }

    // 表面把自身级联下来，本视图据此把指针事件转发进它的输入路由；画在表面之外时为 null
    [CascadingParameter]
    private WorkflowSurfaceBehavior? Surface { get; set; }

    // flow.js 只有一个模块实例，所有连线共享同一条 rAF 循环
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    // 虚拟连线的阶段（发起 / 有效候选 / 无效候选）：本视图的外观，读模型自己的 Preview。
    private ConnectionPreviewState _preview;

    // 阶段色是本 demo 的外观：有效绿、无效红，其余沿用模板给的线色。
    private const string PreviewValidColor = "#FF34D399";
    private const string PreviewInvalidColor = "#FFF87171";
    private const string DefaultLineColor = "#CC38BDF8";

    // 常量色只解析一次：这几个属性每帧被 Razor 读多次，解析结果本身不会变
    private static readonly string PreviewValidCss = ToCss(PreviewValidColor);
    private static readonly string PreviewInvalidCss = ToCss(PreviewInvalidColor);
    private static readonly string DefaultLineCss = ToCss(DefaultLineColor);
    private static readonly string SelectedColorCss = ToCss(SelectedColor);

    private string StageColor => _preview switch
    {
        ConnectionPreviewState.ValidTarget => PreviewValidCss,
        ConnectionPreviewState.InvalidTarget => PreviewInvalidCss,
        _ => LineColor,
    };

    // 线色按值缓存：父级每帧传入同一个字符串时不再重跑 ToCss
    private string? _lineColorKey;
    private bool _lineColorKeySet;
    private string _lineColorCss = DefaultLineCss;

    private string LineColor
    {
        get
        {
            var value = LineColorOverride;
            if (!_lineColorKeySet || !string.Equals(_lineColorKey, value, StringComparison.Ordinal))
            {
                _lineColorKeySet = true;
                _lineColorKey = value;
                _lineColorCss = value ?? DefaultLineCss;
            }

            return _lineColorCss;
        }
    }

    // 线宽解析同样只在参数变化时做一次：光晕、管壁与每段宽度都要问它
    private string? _thicknessKey;
    private double _thicknessValue = 2;

    private double Thickness
    {
        get
        {
            var key = ThicknessOverride;
            if (!string.Equals(_thicknessKey, key, StringComparison.Ordinal))
            {
                _thicknessKey = key;
                _thicknessValue = key is not null
                    && double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                        ? t
                        : 2;
            }

            return _thicknessValue;
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

    // 数值一律走不变文化：Razor 按当前区域写裸 double，逗号小数点会让浏览器读不出这个属性
    // 画布尺寸按值缓存，参数不动时不再每帧格式化
    private bool _canvasWidthSet;
    private double _canvasWidthValue;
    private string _canvasWidthCss = "";

    private string CanvasWidthCss
    {
        get
        {
            if (_canvasWidthSet && _canvasWidthValue == CanvasWidth) return _canvasWidthCss;
            _canvasWidthSet = true;
            _canvasWidthValue = CanvasWidth;
            return _canvasWidthCss = N(CanvasWidth);
        }
    }

    private bool _canvasHeightSet;
    private double _canvasHeightValue;
    private string _canvasHeightCss = "";

    private string CanvasHeightCss
    {
        get
        {
            if (_canvasHeightSet && _canvasHeightValue == CanvasHeight) return _canvasHeightCss;
            _canvasHeightSet = true;
            _canvasHeightValue = CanvasHeight;
            return _canvasHeightCss = N(CanvasHeight);
        }
    }

    private string ThicknessCss => _thicknessCss;
    private string HaloOuterCss => _haloOuterCss;
    private string HaloInnerCss => _haloInnerCss;
    private double HaloOuterWidth => LitThickness + 9;
    private double HaloInnerWidth => LitThickness + 4;

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #region Flow effect (JS-owned)

    // 逐帧工作全在 flow.js：这里只在几何、选中态或线色变化时登记 / 更新一次。
    // Blazor 的渲染频率因此回到「几何变了才画」，与动画时钟彻底脱钩。
    private IJSObjectReference? _flowModule;
    private ElementReference _svgRef;
    private int _flowId;
    private bool _flowRegistered;

    // 版本号在 Prepare 里由几何 / 样式 / 配色的缓存更新时自增；OnAfterRender 只把变化推给 JS，
    // 父级因平移缩放重渲染时版本不变，因此不会产生任何互操作。
    private int _flowVersion;
    private int _flowVersionSent;
    private bool _flowBusy;
    private bool _disposed;

    // 与标记里的绘制条件同一判据：能画、不是虚拟连线、端点已测量。
    private bool ShouldFlow => !EffectiveIsVirtual && EffectiveCanRender
        && Link is not null && TryEndpoints(out _, out _, out _, out _);

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (_disposed) return;

        if (!ShouldFlow)
        {
            await DropFlowAsync();
            return;
        }

        // 插槽测量会在首帧后连发几次渲染：不能有两个导入 / 登记同时在飞，否则同一个 SVG
        // 会被登记两次，先完成的那个 id 随后落在一个已被去重删掉的表项上。
        if (_flowBusy) return;

        _flowBusy = true;
        try
        {
            if (!_flowRegistered)
            {
                var module = _flowModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./flow.js");
                // 记下调用前（而非返回后）的版本：往返期间若有新渲染，post-check 会把它补推一次。
                var versionAtCall = _flowVersion;
                _flowId = await module.InvokeAsync<int>("register", _svgRef, _mixColors, Thickness, LitThickness);
                _flowRegistered = true;
                _flowVersionSent = versionAtCall;
            }
            else if (_flowVersionSent != _flowVersion)
            {
                _flowVersionSent = _flowVersion;
                if (_flowModule is { } current)
                {
                    await current.InvokeVoidAsync("update", _flowId, _mixColors, Thickness, LitThickness);
                }
            }
        }
        catch (JSDisconnectedException)
        {
            // 电路在往返途中断开：组件随后会被释放，注册表随页面一起消失
        }
        catch (ObjectDisposedException)
        {
            // 模块先于本次回调被释放，同样无需再做什么
        }
        finally
        {
            _flowBusy = false;
        }

        // 飞行期间落地的那次渲染被忙标记跳过了：把它的最新状态在这里补推一次。
        if (_disposed || !_flowRegistered) return;

        if (!ShouldFlow)
        {
            await DropFlowAsync();
            return;
        }

        if (_flowVersionSent == _flowVersion) return;

        _flowVersionSent = _flowVersion;
        if (_flowModule is { } latestModule)
        {
            try
            {
                await latestModule.InvokeVoidAsync("update", _flowId, _mixColors, Thickness, LitThickness);
            }
            catch
            {
                // 电路已断开：注册表随页面消失，无需处理
            }
        }
    }

    private async Task DropFlowAsync()
    {
        if (!_flowRegistered) return;

        _flowRegistered = false;
        if (_flowModule is { } module)
        {
            try
            {
                await module.InvokeVoidAsync("unregister", _flowId);
            }
            catch
            {
                // 电路可能已经断开；JS 注册表随页面一起消失，这里无需再做什么
            }
        }
    }

    #endregion

    #region Geometry

    // 画出来的几何与弧长都问 LinkCurve；本视图不再各存一份采样点、累计长度与取点实现。
    private LinkCurve? _curve;

    // 端点缓存：同一条链接端点没动就不重建曲线，也不重复发布。
    private IWorkflowLinkViewModel? _cacheLink;
    private double _cacheSx = double.NaN;
    private double _cacheSy;
    private double _cacheEx;
    private double _cacheEy;

    // 采样点的文本与整条线体路径随曲线缓存：曲线不动时它们逐帧复用，不再重建
    private string[] _samplePoints = Array.Empty<string>();
    private readonly StringBuilder _segmentBuilder = new(256);

    /// <summary>The full resting curve, as one path.</summary>
    private string BodyPath { get; set; } = "";

    // 固定样式：随选中状态 / 线宽 / 线色变化，而不是随帧变化。
    // _mixColors 是彗星 16 段的调色板（尾梢本色 → 头部白热），样式变化时算一次，登记/更新时整组交给 flow.js。
    private readonly string[] _mixColors = new string[TailSegments];
    private bool _styleValid;
    private bool _styleLit;
    private double _styleThickness = double.NaN;
    private string _thicknessCss = "2";
    private string _litThicknessCss = "2";
    private string _haloOuterCss = "11";
    private string _haloInnerCss = "6";

    private bool _cometColorValid;
    private bool _cometColorLit;
    private string? _cometColorKey;

    /// <summary>
    /// Refreshes the arc-length table when the endpoints moved, and the style / colour caches when
    /// the selection or line colour changed. Returns false when the link cannot be drawn at all.
    /// </summary>
    private bool Prepare()
    {
        if (!TryEndpoints(out var sx, out var sy, out var ex, out var ey))
        {
            Link?.PublishCurve(null);
            return false;
        }

        // 换链接也要重建：池中视图可能被复用，端点凑巧相同不代表曲线属于新那条。
        if (!ReferenceEquals(_cacheLink, Link)
            || sx != _cacheSx || sy != _cacheSy || ex != _cacheEx || ey != _cacheEy)
        {
            _cacheLink = Link;
            _cacheSx = sx;
            _cacheSy = sy;
            _cacheEx = ex;
            _cacheEy = ey;
            _curve = LinkCurve.BuildLinkCubic(Link, sx, sy, ex, ey, PullMinimum, LinkCurve.DefaultSampleCount);
            CacheCurveText();
            Link?.PublishCurve(_curve, this);
        }

        EnsureStyles();
        if (!EffectiveIsVirtual) EnsureCometColors();
        return true;
    }

    /// <summary>
    /// Reads the two endpoints, honouring the render gates the XAML adapters use. Returns false when
    /// the link has no drawable geometry yet.
    /// </summary>
    /// <remarks>
    /// NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before the GUI
    /// measures the endpoints would serialize NaN coordinates and paint a stale frame that jumps back
    /// once measurement lands — the first-entry flicker the XAML adapters guard against via
    /// WorkflowLinkRenderEx.IsRenderReady(). Skip until both non-virtual endpoints are measured.
    /// Placeholder endpoints (Parent is null, e.g. the VirtualLink gesture) are exempt.
    /// </remarks>
    private bool TryEndpoints(out double sx, out double sy, out double ex, out double ey)
    {
        sx = sy = ex = ey = 0;

        var link = Link;
        if (link?.Sender is null || link.Receiver is null) return false;
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link)) return false;

        sx = link.Sender.Anchor.Horizontal;
        sy = link.Sender.Anchor.Vertical;
        ex = link.Receiver.Anchor.Horizontal;
        ey = link.Receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the reset
        // intermediate frames, which would serialize a "NaN,NaN" path. Suppress until real.
        return !double.IsNaN(sx) && !double.IsNaN(sy) && !double.IsNaN(ex) && !double.IsNaN(ey);
    }

    // 曲线变化时才重建：每个采样点的 "Lx,y" 与整条线体的 d 都只格式化一次。
    // 彗星切段已交给 flow.js，这里只留下线体与命中用的采样文本。
    private void CacheCurveText()
    {
        var curve = _curve;
        if (curve is null || curve.Count == 0)
        {
            _samplePoints = Array.Empty<string>();
            BodyPath = "";
            _flowVersion++;
            return;
        }

        if (_samplePoints.Length != curve.Count) _samplePoints = new string[curve.Count];

        var sb = _segmentBuilder;
        for (int i = 0; i < curve.Count; i++)
        {
            sb.Clear();
            sb.Append('L').Append(N(curve.XAt(i))).Append(',').Append(N(curve.YAt(i)));
            _samplePoints[i] = sb.ToString();
        }

        BodyPath = curve.Length > 0 ? SegmentPath(0, curve.Length) : "";
        _flowVersion++;
    }

    // 取 [from, to] 这一段弧长上的折线。两端各自插值到精确位置，中间用现成采样点。
    // 返回 SVG 的 path d —— 一段拖尾就是一个独立的元素，才能各带各的颜色与不透明度
    private string SegmentPath(double from, double to)
    {
        var curve = _curve;
        if (curve is null) return "";

        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);
        if (to <= from) return "";

        var sb = _segmentBuilder;
        sb.Clear();
        var start = curve.PointAtLength(from);
        sb.Append('M').Append(N(start.X)).Append(',').Append(N(start.Y));

        // 弧长单调，二分到第一个 > from 的采样点，只走这段窗口内的点
        for (int i = FirstIndexAfter(curve, from); i < curve.Count; i++)
        {
            if (curve.LengthAt(i) >= to) break;
            sb.Append(_samplePoints[i]);
        }

        var end = curve.PointAtLength(to);
        sb.Append('L').Append(N(end.X)).Append(',').Append(N(end.Y));
        return sb.ToString();
    }

    private static int FirstIndexAfter(LinkCurve curve, double length)
    {
        int lo = 0;
        int hi = curve.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (curve.LengthAt(mid) <= length) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    // 选中 / 线宽变化时才重新格式化：这些字符串每次 Blazor 渲染都会被 Razor 读到
    private void EnsureStyles()
    {
        var thickness = Thickness;
        if (_styleValid && _styleLit == IsLit && _styleThickness == thickness) return;

        _styleValid = true;
        _styleLit = IsLit;
        _styleThickness = thickness;

        _thicknessCss = N(thickness);
        _litThicknessCss = N(LitThickness);
        _haloOuterCss = N(HaloOuterWidth);
        _haloInnerCss = N(HaloInnerWidth);
        _flowVersion++;
    }

    // 彗星配色只在选中状态或线色变化时重算；每段“尾梢本色 → 头部白热”也因此只混一次
    private void EnsureCometColors()
    {
        var key = IsLit ? SelectedColor : (LineColorOverride ?? DefaultLineColor);
        if (_cometColorValid && _cometColorLit == IsLit
            && string.Equals(_cometColorKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _cometColorValid = true;
        _cometColorLit = IsLit;
        _cometColorKey = key;

        var body = ParseColor(key);
        for (int k = 0; k < TailSegments; k++)
        {
            _mixColors[k] = MixToWhite(body, k / (double)TailSegments);
        }

        _flowVersion++;
    }

    // 把模板符号带的 XAML 色值解析成四通道
    // 读不了的（如 CSS 颜色名）退回模板自己的 #CC38BDF8：流程要通道值才能混色，混不了就画不出彗星
    private static (int A, int R, int G, int B) ParseColor(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 8
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return ((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
            }

            if (hex.Length == 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return (0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
        }

        return (0xDD, 0xFF, 0xFF, 0xFF);
    }

    // 尾梢（f0 = 0）是本体的颜色，头（f0 = 1）是白：白热只发生在头部
    private static string MixToWhite((int A, int R, int G, int B) from, double t)
    {
        byte L(int channel) => (byte)Math.Round(channel + ((255 - channel) * t));

        return string.Create(CultureInfo.InvariantCulture,
            $"rgb({L(from.R)},{L(from.G)},{L(from.B)})");
    }

    #endregion

    #region Interaction

    // 选中色与增量沿用另外六家：柔光（浅青）而不是刺目的红，线宽 +1.5，线体不透明度 0.55 → 0.85。
    // 写成 ARGB 是为了让 ToCss 与 ParseColor 都能读同一个常量 —— 管壁与彗星跟线体同源
    private const string SelectedColor = "#FFFFFFFF";
    private const double SelectedWidthBonus = 1.5;

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

    // 亮起来的两个理由：指针在线上，或页面把这条线选住了（右键菜单开着时指针已经不在线上）
    private bool IsLit => _hover || IsSelected;

    private string LitColor => IsLit ? SelectedColorCss : StageColor;
    private double LitThickness => IsLit ? Thickness + SelectedWidthBonus : Thickness;
    private string LitThicknessCss => _litThicknessCss;
    private string BodyOpacity => IsLit ? "0.85" : "0.55";

    // 命中交回描边：只有真正画出来的那圈参与命中（最宽的是外层管壁），距离判定由浏览器做。
    // 虚拟连线整层不参与 —— 它是指针下的橡皮筋，命中了就会抢掉正在拖它的那次手势
    private string HitTargetCss => EffectiveIsVirtual ? "none" : "stroke";

    // 悬停高亮：本视图进入即先亮（这张脸是逐元素的，浏览器进出即可，不必等输入路由走一圈）；
    // 同时把这次进入按本视图这条线转发给路由 —— 命中与否由共享的 LinkHitTestEx 说了算，
    // 本视图不替它下结论。
    private async Task OnPointerEnter(MouseEventArgs e)
    {
        IsHighlighted = true;
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(SurfacePointerKind.Entered, e.ClientX, e.ClientY, target: Link);
        }
    }

    private async Task OnPointerExit(MouseEventArgs e)
    {
        IsHighlighted = false;
        if (Surface is not null)
        {
            await Surface.RoutePointerAsync(SurfacePointerKind.Exited, e.ClientX, e.ClientY, target: Link);
        }
    }

    #endregion

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
        _preview = (link as IConnectionPreviewLink)?.Preview ?? ConnectionPreviewState.None;

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
        if (link.GetHelper() is IInputEvents events)
        {
            events.Input.PointerEntered += OnPointerEntered;
            events.Input.PointerExited += OnPointerExited;
        }
    }

    private void OnPointerEntered(object? sender, PointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, PointerExitedEventArgs e) => IsHighlighted = false;

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

        // 候选口从有效变无效时槽位状态不变，只有模型这一格会动。
        if (e.PropertyName is nameof(IConnectionPreviewLink.Preview) or null or "")
        {
            _preview = (Link as IConnectionPreviewLink)?.Preview ?? ConnectionPreviewState.None;
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeManaged();

        // 渲染器实现了 IAsyncDisposable 时会走 DisposeAsync；同步路径只做尽力而为的清理。
        var module = _flowModule;
        _flowModule = null;
        if (module is not null)
        {
            var registered = _flowRegistered;
            _flowRegistered = false;
            _ = ShutdownModuleAsync(module, registered, _flowId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeManaged();

        var module = _flowModule;
        _flowModule = null;
        if (module is not null)
        {
            var registered = _flowRegistered;
            _flowRegistered = false;
            await ShutdownModuleAsync(module, registered, _flowId);
        }
    }

    private void DisposeManaged()
    {
        // 视图走了就不再画这条线，撤掉发布出去的曲线，免得它继续替一条不存在的线回答命中
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
        if (Link?.GetHelper() is IInputEvents events)
        {
            events.Input.PointerEntered -= OnPointerEntered;
            events.Input.PointerExited -= OnPointerExited;
        }
    }

    // 卸载时电路可能已经断开：注销与模块释放都只是尽力而为，绝不让它冒泡到卸载流程。
    private static async Task ShutdownModuleAsync(IJSObjectReference module, bool registered, int id)
    {
        if (registered)
        {
            try
            {
                await module.InvokeVoidAsync("unregister", id);
            }
            catch
            {
            }
        }

        try
        {
            await module.DisposeAsync();
        }
        catch
        {
        }
    }
}
