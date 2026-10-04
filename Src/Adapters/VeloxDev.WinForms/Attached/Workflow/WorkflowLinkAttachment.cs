using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a WinForms control needs to become one link view, attached to it in a single call. The control stays
/// yours — what a link looks like is your <c>OnPaint</c> — and this helper supplies only what you cannot reasonably
/// write yourself: the endpoint subscription (rebind-safe, for a pooled view), the window-region carving a
/// WinForms child window needs, the geometry, the hit-test contract, and this link's own pointer events.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your view's constructor and give it the palette; the view is then a normal control that draws the
/// link however it wants:
/// <code>
/// public sealed class LinkView : Control
/// {
///     private readonly WorkflowLinkAttachment link;
///
///     public LinkView()
///     {
///         link = WorkflowLinkAttachment.Attach(this);
///         link.SurfaceBackground = ParseColor("#1E1E1E");
///         link.LineColor = ParseColor("#DDFFFFFF");
///         link.PointerEntered += (_, _) => { _lit = true; Invalidate(); };
///     }
///
///     protected override void OnPaint(PaintEventArgs e)
///     {
///         base.OnPaint(e);
///         link.Paint(e.Graphics);   // 最短的一版；不想这样画，就照着 link.Curve 自己画
///     }
/// }
/// </code>
/// </para>
/// <para>
/// A WinForms child window is opaque and cannot composite over its siblings, so the window region is carved to the
/// stroke band of the curve instead of a bounding box: the grid behind stays visible around the line, and only the
/// line's own area can ever cover the canvas. That carving, and the box the control is moved to, are this helper's
/// job — keep <see cref="SurfaceBackground"/> equal to the surface's grid background, or the carved band shows up as
/// a seam.
/// </para>
/// <para>
/// The view belongs behind the node cards. Re-ordering pooled views is the surface's job — see
/// <see cref="WorkflowTreeView"/>'s link-layer arrangement.
/// </para>
/// </remarks>
public sealed class WorkflowLinkAttachment
{
    // 一个控件一份：Attach 幂等，池与宿主都用 For 找它。
    private static readonly ConditionalWeakTable<Control, WorkflowLinkAttachment> Attachments = new();

    /// <summary>Extra width the region gets on each side of the stroke, so the antialiased edge is not clipped.</summary>
    private const float RegionPad = 1.5f;

    private readonly Control target;
    private readonly ModelChangeRelay relay;

    private IWorkflowLinkViewModel? link;
    private bool canRender = true;
    private bool isVirtual;
    private Color lineColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
    private float thickness = 1.5f;
    private float pullMinimum = 40f;
    private Color surfaceBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);

    // 当前帧：窗口局部坐标下的四个曲线控制点（起点、两个控制点、终点），以及窗口被它平移过的原点。
    // 没东西可画时为 null。
    private PointF[]? windowCurve;
    private Region? windowRegion;

    // 表面现在的投影，与节点卡片收到的是同一份。端点本身是**画布系**，而判「这个口贴着节点哪条边」
    // 的依据（node.Anchor / node.Size）是**模型系** —— 两者正好差这个平移，所以方向那一趟要先搬过去。
    private Point surfacePan;
    private Offset surfaceContent = new(0, 0);

    /// <summary>Attaches the link machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The control that is going to draw one link.</param>
    /// <returns>The attachment, for chaining the palette and the event subscriptions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowLinkAttachment Attach(Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static control => new WorkflowLinkAttachment(control));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The control to look up.</param>
    public static WorkflowLinkAttachment? For(Control target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowLinkAttachment(Control target)
    {
        this.target = target;
        relay = new ModelChangeRelay(target, OnModelChanged);

        // 不透明填充，且与窗口被雕出来的那片网格同色：窗口正好盖住描边带，所以这个填充在画布上必须看不出来。
        target.BackColor = surfaceBackground;
        // SetStyle 在 Control 上是 protected，所以那几行只能由视图自己写（见本类 remarks 的模板）——
        // 这里只做得出外部做得到的事：关掉焦点与命中，免得描边带吞掉本该发给画布或手势的输入。
        target.TabStop = false;
        target.Enabled = false;
        ApplyRegion(null);

        target.Disposed += (_, _) => Detach();
    }

    /// <summary>Raised when the pointer arrives over this link.</summary>
    public event EventHandler<WorkflowPointerEnteredEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this link.</summary>
    public event EventHandler<WorkflowPointerExitedEventArgs>? PointerLeft;

    /// <summary>Raised when a pointer button goes down over this link.</summary>
    public event EventHandler<WorkflowPointerPressedEventArgs>? PointerPressed;

    /// <summary>Raised when a pointer button comes up over this link.</summary>
    public event EventHandler<WorkflowPointerReleasedEventArgs>? PointerReleased;

    /// <summary>Raised when a key goes down while this link is the key's target (the pointer is on it).</summary>
    public event EventHandler<WorkflowKeyDownEventArgs>? KeyDown;

    /// <summary>Raised when a key comes up while this link is the key's target.</summary>
    public event EventHandler<WorkflowKeyUpEventArgs>? KeyUp;

    /// <summary>The link this control currently draws, or <see langword="null"/> before the first bind.</summary>
    public IWorkflowLinkViewModel? Link => link;

    /// <summary>
    /// The four Bézier control points of the curve, in this control's own coordinates — what <see cref="Paint"/>
    /// draws, published for hit-testing at the same time. <see langword="null"/> when there is nothing to draw.
    /// </summary>
    public PointF[]? Curve => windowCurve;

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => lineColor;
        set { lineColor = value; target.Invalidate(); }
    }

    /// <summary>The stroke width, in pixels.</summary>
    /// <remarks>Changing it re-carves the window region on the next geometry rebuild.</remarks>
    public float Thickness
    {
        get => thickness;
        set
        {
            if (thickness == value || value <= 0) return;
            thickness = value;
            RebuildGeometry();
        }
    }

    /// <summary>
    /// The least horizontal distance, in pixels, each control point is pulled away from its own endpoint — what
    /// makes the curve leave both ports horizontally.
    /// </summary>
    /// <remarks>
    /// The pull actually used is the larger of this and half the horizontal gap between the endpoints, so two
    /// ports close together do not degenerate the curve into a straight segment. Changing this re-carves the
    /// window region on the next geometry rebuild.
    /// </remarks>
    public float PullMinimum
    {
        get => pullMinimum;
        set
        {
            if (pullMinimum == value || value < 0) return;
            pullMinimum = value;
            RebuildGeometry();
        }
    }

    /// <summary>The colour behind the link — the surface's grid background.</summary>
    public Color SurfaceBackground
    {
        get => surfaceBackground;
        set
        {
            if (surfaceBackground == value) return;
            surfaceBackground = value;
            target.BackColor = value;
            target.Invalidate();
        }
    }

    /// <summary>
    /// Binds this control to a link: subscribes to it and to both its endpoints, and rebuilds the geometry. Called
    /// by the view pool when a pooled control is handed to another link — a hand-written view only needs it if it
    /// is not pooled.
    /// </summary>
    /// <param name="link">The link, or <see langword="null"/> to detach.</param>
    public void Bind(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(this.link, link))
        {
            Sync(link);
            return;
        }

        // 池把这个控件回收给另一条线：旧连线不能再指着这个控件，否则它会把一条已经不由这里画的线报成可命中
        RetractCurve();
        HookInput(this.link, null);

        this.link = link;
        target.Tag = link;
        HookInput(null, link);

        // 连线自己，加上它两端 —— 端点的锚点变了，几何就得重算。
        relay.Set(link as INotifyPropertyChanged);
        relay.Add(link?.Sender as INotifyPropertyChanged);
        relay.Add(link?.Receiver as INotifyPropertyChanged);

        Sync(link);
    }

    /// <summary>
    /// Draws the resting line for <see cref="Curve"/>, which is what a link looks like with nothing else added.
    /// Call it from your <c>OnPaint</c>, or ignore it and draw the curve yourself — either way the curve has already
    /// been published for hit-testing.
    /// </summary>
    /// <param name="g">The surface to paint on.</param>
    public void Paint(Graphics g)
    {
        if (g is null) throw new ArgumentNullException(nameof(g));

        var points = windowCurve;
        if (points is null || points.Length < 4) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(lineColor, thickness);
        if (isVirtual)
        {
            pen.DashStyle = DashStyle.Dash;
            pen.DashPattern = [4f, 2f];
        }

        using var curve = new GraphicsPath();
        curve.AddBezier(points[0], points[1], points[2], points[3]);
        g.DrawPath(pen, curve);
    }

    /// <summary>
    /// Records the surface's current projection — the same pair the node cards get — so the endpoints can be
    /// read in the frame <c>node.Anchor</c> lives in when the curve's direction is worked out.
    /// </summary>
    /// <param name="panOffset">The surface's signed pan translation, in pixels.</param>
    /// <param name="contentOffset">The world origin the surface is drawing at.</param>
    public void ApplySurfacePosition(Point panOffset, Offset contentOffset)
    {
        if (surfacePan == panOffset
            && surfaceContent.Horizontal == contentOffset.Horizontal
            && surfaceContent.Vertical == contentOffset.Vertical)
        {
            return;
        }

        surfacePan = panOffset;
        surfaceContent = contentOffset;
        RebuildGeometry();
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    public static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    // 换绑就是换订阅：这条线的四个指针事件跟着走，池化控件因此不需要视图自己记一份。
    private void HookInput(IWorkflowLinkViewModel? previous, IWorkflowLinkViewModel? next)
    {
        if (previous?.GetHelper() is IWorkflowInputEvents old)
        {
            old.Input.PointerEntered -= OnInputPointerEntered;
            old.Input.PointerExited -= OnInputPointerExited;
            old.Input.PointerPressed -= OnInputPointerPressed;
            old.Input.PointerReleased -= OnInputPointerReleased;
            old.Input.KeyDown -= OnInputKeyDown;
            old.Input.KeyUp -= OnInputKeyUp;
        }

        if (next?.GetHelper() is IWorkflowInputEvents now)
        {
            now.Input.PointerEntered += OnInputPointerEntered;
            now.Input.PointerExited += OnInputPointerExited;
            now.Input.PointerPressed += OnInputPointerPressed;
            now.Input.PointerReleased += OnInputPointerReleased;
            now.Input.KeyDown += OnInputKeyDown;
            now.Input.KeyUp += OnInputKeyUp;
        }
    }

    private void OnInputPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e) => PointerEntered?.Invoke(this, e);

    private void OnInputPointerExited(object? sender, WorkflowPointerExitedEventArgs e) => PointerLeft?.Invoke(this, e);

    private void OnInputPointerPressed(object? sender, WorkflowPointerPressedEventArgs e) => PointerPressed?.Invoke(this, e);

    private void OnInputPointerReleased(object? sender, WorkflowPointerReleasedEventArgs e) => PointerReleased?.Invoke(this, e);

    private void OnInputKeyDown(object? sender, WorkflowKeyDownEventArgs e) => KeyDown?.Invoke(this, e);

    private void OnInputKeyUp(object? sender, WorkflowKeyUpEventArgs e) => KeyUp?.Invoke(this, e);

    // 控件销毁：退订、撤曲线、交还区域。
    private void Detach()
    {
        HookInput(link, null);
        RetractCurve();
        relay.Clear();
        windowRegion?.Dispose();
        windowRegion = null;
    }

    /// <summary>Re-reads the bound link and re-carves the window. Called on bind and on recycle.</summary>
    /// <param name="link">The link.</param>
    public void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        canRender = link.IsVisible;
        RebuildGeometry();
    }

    // 连线自己或它任一端动了：可见性可能变了，几何也要重算。重算前重读一次可见性 —— 端点的变更不影响它，
    // 但读一次是幂等的，比按属性名分叉更不容易漏。
    private void OnModelChanged(PropertyChangedEventArgs e)
    {
        canRender = link?.IsVisible == true;
        RebuildGeometry();
    }

    // 按当前端点重建窗口盒与区域。端点是画布局部坐标（插槽布局按每个插槽控件的屏幕位置写进去的），所以
    // 盒子直接用，不加节点视图自己加的那份平移。
    private void RebuildGeometry()
    {
        var current = link;
        // NaN 门：插槽锚点在画布测量之前是 NaN，真实连线要等就绪；虚拟连线占位（Parent 为 null）豁免。
        if (current is null || !canRender || !WorkflowSlotUpdateGate.IsLinkRenderReady(current))
        {
            Clear();
            return;
        }

        var sender = current.Sender;
        var receiver = current.Receiver;
        if (sender is null || receiver is null)
        {
            Clear();
            return;
        }

        // 每次都重算，绝不用缓存值：一个从虚拟（手势）连线回收来的池化视图，接着画真实连线时不能还画着虚线。
        isVirtual = sender.Parent is null && receiver.Parent is null;

        var points = BuildCurve(current, sender, receiver);
        if (!IsDrawable(points))
        {
            Clear();
            return;
        }

        // 把这条线画出来的几何提交给 Core：命中判定与绘制从此共用同一条曲线，不再各推一遍。
        // 坐标是画布局部坐标（slot.Anchor 的空间），也是表面指针事件所在的空间 —— 两边必须同系。
        // 发布不依赖视图是否真的画了一笔：几何与可见性这里都知道，画法归用户，命中契约归这里。
        current.PublishCurve(
            LinkCurve.FromCubic(
                points[0].X, points[0].Y, points[1].X, points[1].Y,
                points[2].X, points[2].Y, points[3].X, points[3].Y),
            target);

        using var strokePen = new Pen(Color.Black, thickness + 2 * RegionPad) { LineJoin = LineJoin.Miter };
        using var strokePath = new GraphicsPath();
        strokePath.AddBezier(points[0], points[1], points[2], points[3]);
        strokePath.Widen(strokePen);

        var bounds = strokePath.GetBounds();
        var originX = (float)Math.Floor(bounds.Left);
        var originY = (float)Math.Floor(bounds.Top);
        using (var shift = new Matrix(1f, 0f, 0f, 1f, -originX, -originY))
        {
            strokePath.Transform(shift);
        }

        var local = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            local[i] = new PointF(points[i].X - originX, points[i].Y - originY);
        }

        windowCurve = local;

        // 先雕形状、再挪窗口：区域是窗口客户区坐标（与窗口位置无关），而反过来的话，SetWindowPos 之后、
        // 区域更新之前那一瞬，新露出来的矩形会先按 BackColor 画一次 —— 每帧闪一下方框。
        ApplyRegion(strokePath);

        // 落在整像素上，好让画出来的线保持它原来在画布局部的那条路径；小数余量留在点的坐标里。
        target.Location = new Point((int)originX, (int)originY);
        target.Size = new System.Drawing.Size(
            Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)originX + 1),
            Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)originY + 1));

        target.Invalidate();
    }

    // 画不出来：曲线撤掉、区域清空、盒子不再回答。
    private void Clear()
    {
        windowCurve = null;
        RetractCurve();
        ApplyRegion(null);
        target.Invalidate();
    }

    // 撤销这条线发布的曲线：画不出来时它不该再回答命中。曲线撤掉，LinkHelper 里那个 Visual 也跟着清。
    private void RetractCurve() => link?.PublishCurve(null);

    // 把雕好的形状交给窗口。WinForms 会把区域拷贝进窗口，所以上一个托管 Region 归我们处置。
    // 「什么都不画」必须是**空**区域：`new Region()` 是 GDI+ 的**无限**区域，窗口会整块露出来 ——
    // 池化视图带着上一次的盒子走到这条路上时，画布上就留下一个 SurfaceBackground 色的方框
    //（用户报的「黑色盒子」；实测能挂 1.8 秒，直到几何再次可画）。
    private void ApplyRegion(GraphicsPath? strokePath)
    {
        var next = strokePath is null ? new Region() : new Region(strokePath);
        if (strokePath is null)
        {
            next.MakeEmpty();
        }

        var previous = windowRegion;
        windowRegion = next;
        target.Region = next;
        previous?.Dispose();
    }

    // GDI+ 拒绝加宽一条它描不出来的路径：端点落在同一个像素上（连线手势的第一帧）、或者锚点还没测量（NaN）。
    // 不用 float.IsFinite / `^1` 索引 —— 本包要能编到 netframework4.6.1。
    private static bool IsDrawable(PointF[] points)
    {
        foreach (var point in points)
        {
            if (float.IsNaN(point.X) || float.IsNaN(point.Y)
                || float.IsInfinity(point.X) || float.IsInfinity(point.Y))
            {
                return false;
            }
        }

        var last = points[points.Length - 1];
        return Math.Abs(points[0].X - last.X) >= 0.5f || Math.Abs(points[0].Y - last.Y) >= 0.5f;
    }

    // 起点、两个控制点、终点 —— AddBezier 要的那四个点。
    // 每个控制点沿**自己那个口**所在边的外法线拉（Core 的 LinkCurve.LinkCurvePoints 给的），
    // 所以口在上/下边时竖直出线、反向连线也不会把控制点戳进自己节点；拖拽预览退回房规。
    private PointF[] BuildCurve(IWorkflowLinkViewModel link, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        // 端点是**画布系**，而 LinkCurvePoints 按 node.Anchor / node.Size（**模型系**）判每个口贴哪条边 ——
        // 两系差一个表面投影。所以先把端点搬进模型系让它判，再把算出的四个点搬回画布系（画与命中都在这个系）。
        // 不搬的后果实测过：投影一大，左缘的口会被判成上边，反向连线该往左翻出去的那一端就不翻了。
        var ox = surfacePan.X + surfaceContent.Horizontal;
        var oy = surfacePan.Y + surfaceContent.Vertical;

        var points = LinkCurve.LinkCurvePoints(
            link,
            sender.Anchor.Horizontal - ox, sender.Anchor.Vertical - oy,
            receiver.Anchor.Horizontal - ox, receiver.Anchor.Vertical - oy,
            pullMinimum);

        var result = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            result[i] = new PointF((float)(points[i].X + ox), (float)(points[i].Y + oy));
        }

        return result;
    }
}
