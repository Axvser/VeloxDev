using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The shared scene for input tests: two nodes with one slot each, one link between them, and the tree that holds
/// it — plus the argument factories the tests route through <see cref="WorkflowInput"/>.
/// </summary>
public abstract class WorkflowInputTestBase
{
    /// <summary>The radius the default link hit test uses — <see cref="LinkHitTestEx.DefaultHitRadius"/>.</summary>
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

    /// <summary>A stand-in for the view that produced the input — what the argument carries as <c>Source</c>.</summary>
    protected sealed class SourceView
    {
    }

    /// <summary>The input capability of a component's helper, for subscribing in a test.</summary>
    protected static IInputEvents Events(object component) => component switch
    {
        IWorkflowLinkViewModel link => (IInputEvents)link.GetHelper(),
        IWorkflowSlotViewModel slot => (IInputEvents)slot.GetHelper(),
        IWorkflowNodeViewModel node => (IInputEvents)node.GetHelper(),
        IWorkflowTreeViewModel tree => (IInputEvents)tree.GetHelper(),
        _ => throw new ArgumentException($"Not a workflow component: {component.GetType()}", nameof(component)),
    };

    // 目标由适配器判出来，所以这些工厂都收一个 target：测试直接指定「谁被指到了」，
    // Core 不再自己判命中（判命中的是 LinkHitTestEx，单测在 LinkHitTestExTests 里）。
    protected static PointerMovedEventArgs Move(double x, double y, IWorkflowViewModel? target = null, int layer = 0)
        => new(new Anchor(x, y, layer), InputModifiers.None, new SourceView(), target, new WorkflowEventHandle());

    protected static PointerEnteredEventArgs Enter(double x, double y, IWorkflowViewModel? target = null)
        => new(new Anchor(x, y, 0), InputModifiers.None, new SourceView(), target, new WorkflowEventHandle());

    protected static PointerExitedEventArgs Exit(double x, double y, IWorkflowViewModel? target = null)
        => new(new Anchor(x, y, 0), InputModifiers.None, new SourceView(), target, new WorkflowEventHandle());

    protected static PointerPressedEventArgs Press(
        double x, double y, MouseButton button = MouseButton.Right, IWorkflowViewModel? target = null)
        => new(new Anchor(x, y, 0), InputModifiers.None, new SourceView(), target, button, 1, new WorkflowEventHandle());

    protected static PointerReleasedEventArgs Release(
        double x, double y, MouseButton button = MouseButton.Left, IWorkflowViewModel? target = null)
        => new(new Anchor(x, y, 0), InputModifiers.None, new SourceView(), target, button, 1, new WorkflowEventHandle());

    protected static PointerWheelEventArgs Wheel(double x, double y, double deltaY, IWorkflowViewModel? target = null)
        => new(new Anchor(x, y, 0), InputModifiers.None, new SourceView(), target, 0d, deltaY, new WorkflowEventHandle());

    protected static KeyDownEventArgs Down(
        InputKey key, IWorkflowViewModel? target = null, int rawKeyCode = 0, InputModifiers modifiers = InputModifiers.None)
        => new(key, rawKeyCode, modifiers, false, new SourceView(), target, new WorkflowEventHandle());

    protected static KeyUpEventArgs Up(InputKey key, IWorkflowViewModel? target = null)
        => new(key, 0, InputModifiers.None, false, new SourceView(), target, new WorkflowEventHandle());
}
