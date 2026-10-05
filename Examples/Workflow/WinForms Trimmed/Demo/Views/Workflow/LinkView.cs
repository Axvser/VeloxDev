// VeloxDev customization: The connection view. This control is yours — how a link looks is your OnPaint. The
// adapter's WorkflowLinkAttachment owns everything that is not drawing: the endpoint subscription (rebind-safe for
// a pooled view), the window-region carving, the geometry, the hit-test contract, and the link's pointer events.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>The connection view: one control that draws one link, plus this demo's hover glow.</summary>
public sealed class LinkView : Control
{
    // VeloxDev customization: the glow's colour and how far it spreads. It reads as the line being lit rather
    // than recoloured — the resting stroke is already near white.
    private static readonly Color GlowColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private const float GlowSpread = 6f;

    private readonly WorkflowLinkAttachment link;
    private bool _lit;

    public LinkView()
    {
        // Control styles are protected on Control, so they belong in the control — not in the attachment.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);

        // One call attaches the rest: endpoints, geometry, window region, hit-testing, pointer events.
        link = WorkflowLinkAttachment.Attach(this);
        link.SurfaceBackground = WorkflowLinkAttachment.ParseColor("#1E1E1E");
        link.LineColor = WorkflowLinkAttachment.ParseColor("#DDFFFFFF");
        link.Thickness = 2f;

        // VeloxDev customization: the hover glow is this demo's. The attachment already follows the pooled
        // rebinding, so these two lines are the whole subscription — delete them to ship without a glow.
        link.PointerEntered += (_, _) => { _lit = true; Invalidate(); };
        link.PointerLeft += (_, _) => { _lit = false; Invalidate(); };

        // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删由这里写。
        link.KeyDown += (_, e) =>
        {
            if (e.Key != InputKey.Delete || e.Handle.PreventDefault) return;
            if (link.Link is { } current && current.DeleteCommand.CanExecute(null)) current.DeleteCommand.Execute(null);
        };
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        base.OnPaint(e);

        if (_lit && link.Curve is { Length: 4 } curve)
        {
            DrawGlow(e.Graphics, curve, link.Thickness);
        }

        // 最短的一版静息线；不想用它的画法，就照着 link.Curve 自己画 —— 命中契约已经发布过了。
        link.Paint(e.Graphics);
    }

    // 一圈更宽、半透明的描边垫在静息线下面。
    private static void DrawGlow(Graphics g, PointF[] curve, float thickness)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(Color.FromArgb(0x40, GlowColor), thickness + GlowSpread)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var path = new GraphicsPath();
        path.AddBezier(curve[0], curve[1], curve[2], curve[3]);
        g.DrawPath(pen, path);
    }
}
