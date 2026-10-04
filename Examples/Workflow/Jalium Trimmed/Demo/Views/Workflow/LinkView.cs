// VeloxDev customization: The connection view. This element is yours — how a link looks is your OnRender. The
// adapter's WorkflowLinkAttachment owns everything that is not drawing: the binding and endpoint tracking
// (rebind-safe for a pooled view), the self-bounding, the geometry, the hit-test contract, and the link's pointer
// events.
using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>The connection view: one element that draws one link, plus this demo's hover glow.</summary>
public sealed class LinkView : FrameworkElement
{
    // VeloxDev customization: the glow's colour and how far it spreads. It reads as the line being lit rather
    // than recoloured — the resting stroke is already near white.
    private static readonly Color GlowColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private const double GlowSpread = 6;

    private readonly WorkflowLinkAttachment link;
    private bool _lit;

    public LinkView()
    {
        // One call attaches the rest: binding, endpoints, self-bounding, geometry, hit-testing, pointer events.
        link = WorkflowLinkAttachment.Attach(this);
        link.PortLayout = SlotView.Layout;
        link.LinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
        link.Thickness = 2;

        // VeloxDev customization: the hover glow is this demo's. The attachment already follows the pooled
        // rebinding, so these two lines are the whole subscription — delete them to ship without a glow.
        link.PointerEntered += (_, _) => { _lit = true; InvalidateVisual(); };
        link.PointerLeft += (_, _) => { _lit = false; InvalidateVisual(); };

        // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删由这里写。
        link.KeyDown += (_, e) =>
        {
            if (e.Key != WorkflowKey.Delete || e.Handle.PreventDefault) return;
            if (link.Link is { } current && current.DeleteCommand.CanExecute(null)) current.DeleteCommand.Execute(null);
        };
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_lit && link.Curve is { Length: 4 } curve)
        {
            DrawGlow(dc, curve, link.Thickness);
        }

        // 最短的一版静息线；不想用它的画法，就照着 link.Curve 自己画 —— 几何已经发布给命中了。
        link.Paint(dc);
    }

    // 一圈更宽、半透明的描边垫在静息线下面。
    private static void DrawGlow(DrawingContext dc, Point[] curve, double thickness)
    {
        var figure = new PathFigure { StartPoint = curve[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new BezierSegment(curve[1], curve[2], curve[3], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var glow = Color.FromArgb(0x40, GlowColor.R, GlowColor.G, GlowColor.B);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(glow), thickness + GlowSpread), geometry);
    }
}
