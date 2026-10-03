using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The model layer's standard events: the framework asks before it changes a node's placement, a node's lifetime,
/// a slot's channel or a connection — and reports after, with the same handle.
/// </summary>
/// <remarks>
/// The placement events carry <b>complete</b> anchors — layer included. That is the rule for every value the
/// framework hands out for a component: a host that applies <c>To</c> itself, or keeps <c>From</c> to undo, must not
/// lose the layer to a bare <c>new Anchor(x, y, 0)</c>.
/// </remarks>
[TestClass]
public sealed class ModelEventTests
{
    private static IWorkflowNodeViewModel NodeIn(TreeHolder holder, double x = 0, double y = 0, int layer = 0)
    {
        var node = new NodeDefaultViewModel { Parent = holder.Tree, Anchor = new Anchor(x, y, layer) };
        holder.Tree.Nodes.Add(node);
        return node;
    }

    // ── 落位 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Moving_PreventDefault_PinsTheNode()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder, 10, 20);
        ((IWorkflowNodeEvents)node.GetHelper()).Moving += (_, e) => e.Handle.PreventDefault = true;

        node.MoveCommand.Execute(new Offset(5, 5));

        Assert.AreEqual(10d, node.Anchor.Horizontal, 1e-9, "被钉住的节点不动");
        Assert.AreEqual(20d, node.Anchor.Vertical, 1e-9);
    }

    [TestMethod]
    public void MovingAndMoved_CarryOneHandleAndCompletePlacements()
    {
        // 这条钉住用户定的规则：事件里的 Anchor 是**完整落位**，图层必须跟着走。
        var holder = new TreeHolder();
        var node = NodeIn(holder, 10, 20, layer: 7);
        NodeMoveEventArgs? moving = null;
        NodeMoveEventArgs? moved = null;
        ((IWorkflowNodeEvents)node.GetHelper()).Moving += (_, e) => moving = e;
        ((IWorkflowNodeEvents)node.GetHelper()).Moved += (_, e) => moved = e;

        node.MoveCommand.Execute(new Offset(5, -3));

        Assert.IsNotNull(moving);
        Assert.IsNotNull(moved);
        Assert.AreSame(moving!.Handle, moved!.Handle, "两相共用一个句柄");

        Assert.AreEqual(10d, moving.From.Horizontal, 1e-9);
        Assert.AreEqual(20d, moving.From.Vertical, 1e-9);
        Assert.AreEqual(15d, moving.To.Horizontal, 1e-9);
        Assert.AreEqual(17d, moving.To.Vertical, 1e-9);

        Assert.AreEqual(7, moving.From.Layer, "From 带着图层");
        Assert.AreEqual(7, moving.To.Layer, "To 也带着图层");
        Assert.AreEqual(7, node.Anchor.Layer, "落位之后节点自己的图层还在");
    }

    [TestMethod]
    public void Moving_StopPropagation_MovesButReportsNothing()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder, 10, 20);
        var moved = 0;
        ((IWorkflowNodeEvents)node.GetHelper()).Moving += (_, e) => e.Handle.StopPropagation = true;
        ((IWorkflowNodeEvents)node.GetHelper()).Moved += (_, _) => moved++;

        node.MoveCommand.Execute(new Offset(5, 5));

        Assert.AreEqual(15d, node.Anchor.Horizontal, 1e-9, "默认动作照跑");
        Assert.AreEqual(0, moved, "只是不报");
    }

    // ── 尺寸 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Resizing_PreventDefault_KeepsTheOldSize()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder);
        node.Size = new Size { Width = 100, Height = 50 };
        NodeResizeEventArgs? resizing = null;
        ((IWorkflowNodeEvents)node.GetHelper()).Resizing += (_, e) =>
        {
            resizing = e;
            e.Handle.PreventDefault = true;
        };

        node.SetSizeCommand.Execute(new Size { Width = 200, Height = 90 });

        Assert.IsNotNull(resizing);
        Assert.AreEqual(100d, resizing!.From.Width, 1e-9);
        Assert.AreEqual(200d, resizing.To.Width, 1e-9);
        Assert.AreEqual(100d, node.Size.Width, 1e-9, "被拒绝之后尺寸没变");
    }

    // ── 生命周期 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Deleting_PreventDefault_KeepsTheNodeInTheTree()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder);
        ((IWorkflowNodeEvents)node.GetHelper()).Deleting += (_, e) => e.Handle.PreventDefault = true;

        node.DeleteCommand.Execute(null);

        Assert.IsTrue(holder.Tree.Nodes.Contains(node), "拒绝之后节点还在");
    }

    [TestMethod]
    public void Deleting_NoSubscriber_DeletesAsItAlwaysDid()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder);
        var deleted = 0;
        ((IWorkflowNodeEvents)node.GetHelper()).Deleted += (_, _) => deleted++;

        node.DeleteCommand.Execute(null);

        Assert.IsFalse(holder.Tree.Nodes.Contains(node));
        Assert.AreEqual(1, deleted);
    }

    // ── 通道 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ChannelChanging_PreventDefault_KeepsTheChannel()
    {
        var holder = new TreeHolder();
        var node = NodeIn(holder);
        var slot = new SlotDefaultViewModel { Parent = node };
        node.Slots.Add(slot);
        var before = slot.Channel;
        SlotChannelEventArgs? changing = null;
        ((IWorkflowSlotEvents)slot.GetHelper()).ChannelChanging += (_, e) =>
        {
            changing = e;
            e.Handle.PreventDefault = true;
        };

        slot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);

        Assert.IsNotNull(changing);
        Assert.AreEqual(before, changing!.From);
        Assert.AreEqual(SlotChannel.MultipleTargets, changing.To);
        Assert.AreEqual(before, slot.Channel, "被拒绝之后通道没变");
    }

    // ── 连接（业务层） ──────────────────────────────────────────────────────

    [TestMethod]
    public void Connecting_PreventDefault_RefusesTheConnection()
    {
        var holder = new TreeHolder();
        var a = NodeIn(holder);
        var b = NodeIn(holder, 200, 0);
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);

        ConnectionEventArgs? connecting = null;
        ((IWorkflowTreeEvents)holder.Tree.GetHelper()).Connecting += (_, e) =>
        {
            connecting = e;
            e.Handle.PreventDefault = true;
        };

        holder.Tree.GetHelper().SendConnection(sa);
        holder.Tree.GetHelper().ReceiveConnection(sb);

        Assert.IsNotNull(connecting, "连接之前先问过");
        Assert.AreSame(sa, connecting!.Sender);
        Assert.AreSame(sb, connecting.Receiver);
        Assert.AreEqual(0, holder.Tree.Links.Count, "被拒绝之后没有连线");
    }

    [TestMethod]
    public void Connecting_NoSubscriber_ConnectsAsItAlwaysDid()
    {
        var holder = new TreeHolder();
        var a = NodeIn(holder);
        var b = NodeIn(holder, 200, 0);
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        var connected = 0;
        ((IWorkflowTreeEvents)holder.Tree.GetHelper()).Connected += (_, _) => connected++;

        holder.Tree.GetHelper().SendConnection(sa);
        holder.Tree.GetHelper().ReceiveConnection(sb);

        Assert.AreEqual(1, holder.Tree.Links.Count);
        Assert.AreEqual(1, connected);
    }

    // 一颗树，用于把节点挂上去（节点的 Parent 就是它）。
    private sealed class TreeHolder
    {
        public TreeDefaultViewModel Tree { get; } = new();
    }
}
