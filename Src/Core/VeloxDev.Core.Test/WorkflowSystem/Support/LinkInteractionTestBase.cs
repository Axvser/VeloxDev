using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The shared scene for hub tests: two nodes with one slot each, one link between them, and the tree that holds
/// it. Preview/Outcome tests differ only in what they subscribe to, so they all start from here.
/// </summary>
public abstract class LinkInteractionTestBase
{
    /// <summary>The radius the hub was built with — <see cref="LinkHitTestEx.DefaultHitRadius"/>.</summary>
    protected const double Radius = LinkHitTestEx.DefaultHitRadius;

    protected static IWorkflowLinkViewModel ReadyLink(double sx, double sy, double ex, double ey, bool visible = true)
    {
        var a = new NodeDefaultViewModel();
        var b = new NodeDefaultViewModel();
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        sa.Anchor = new Anchor(sx, sy, 0);
        sb.Anchor = new Anchor(ex, ey, 0);

        var link = new LinkDefaultViewModel { Sender = sa, Receiver = sb, IsVisible = visible };
        link.PublishCurve(LinkCurve.BuildCubic(sx, sy, ex, ey, 40));
        return link;
    }

    protected static TreeDefaultViewModel TreeWith(params IWorkflowLinkViewModel[] links)
    {
        var tree = new TreeDefaultViewModel();
        var visible = tree.GetHelper().VisibleItems;
        visible.Clear();
        foreach (var link in links) visible.Add(link);
        return tree;
    }

    protected static PointerEvent Move(double x, double y)
        => new(PointerPhase.Moved, new Anchor(x, y, 0));

    /// <summary>
    /// A tree whose link really belongs to it — nodes, slots, <c>Links</c> and <c>LinksMap</c> — so
    /// <c>DeleteCommand</c> actually removes it. <see cref="TreeWith"/> deliberately does not do this, which is
    /// what the policy-free tests want.
    /// </summary>
    protected static TreeDefaultViewModel DeletableTree(out IWorkflowLinkViewModel link)
    {
        var tree = new TreeDefaultViewModel();
        var a = new NodeDefaultViewModel { Parent = tree };
        var b = new NodeDefaultViewModel { Parent = tree };
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        tree.Nodes.Add(a);
        tree.Nodes.Add(b);

        link = new LinkDefaultViewModel { Sender = sa, Receiver = sb, IsVisible = true };
        tree.Links.Add(link);
        tree.LinksMap[sa] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel> { [sb] = link };
        tree.GetHelper().VisibleItems.Add(link);
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]));
        return tree;
    }

    /// <summary>A stand-in for a link view: it implements <see cref="ILinkHighlight"/>, so the hub can light it.</summary>
    protected sealed class HighlightingVisual : ILinkHighlight
    {
        private bool highlighted;

        public bool IsHighlighted
        {
            get => highlighted;
            set
            {
                if (highlighted == value) return;
                highlighted = value;
                HighlightChanges++;
            }
        }

        public int HighlightChanges { get; private set; }
    }
}
