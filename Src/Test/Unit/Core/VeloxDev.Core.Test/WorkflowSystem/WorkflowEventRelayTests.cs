using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The relay the declarative platforms stand on: a host hands over one sink object and the framework forwards the
/// component's model events into it — refusal included, and the subscription is disposable.
/// </summary>
[TestClass]
public sealed class WorkflowEventRelayTests
{
    [TestMethod]
    public void Attach_ForwardsBothPhasesWithCompletePlacements()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Parent = tree, Anchor = new Anchor(10, 20, layer: 3) };
        tree.Nodes.Add(node);
        var sink = new NodeSink();
        using var subscription = WorkflowEventRelay.Attach(node, sink);

        node.MoveCommand.Execute(new Offset(5, 5));

        Assert.AreEqual(1, sink.MovingCount);
        Assert.AreEqual(1, sink.MovedCount);
        Assert.AreEqual(10d, sink.Moving!.From.Horizontal, 1e-9);
        Assert.AreEqual(15d, sink.Moving.To.Horizontal, 1e-9);
        Assert.AreEqual(3, sink.Moving.To.Layer, "落位带着图层");
    }

    [TestMethod]
    public void Attach_ASinkCanRefuseTheAction()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Parent = tree, Anchor = new Anchor(10, 20) };
        tree.Nodes.Add(node);
        var sink = new NodeSink { RefuseMoving = true };
        using var subscription = WorkflowEventRelay.Attach(node, sink);

        node.MoveCommand.Execute(new Offset(5, 5));

        Assert.AreEqual(10d, node.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(1, sink.MovingCount);
        Assert.AreEqual(0, sink.MovedCount);
        Assert.IsNotNull(subscription, "能力具备时订阅非空");
    }

    [TestMethod]
    public void Dispose_StopsTheForwarding()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Parent = tree, Anchor = new Anchor(10, 20) };
        tree.Nodes.Add(node);
        var sink = new NodeSink();
        WorkflowEventRelay.Attach(node, sink)?.Dispose();

        node.MoveCommand.Execute(new Offset(5, 5));

        Assert.IsTrue(node.Anchor.Horizontal != 10d, "动作照做");
        Assert.AreEqual(0, sink.MovingCount, "只是不再转发");
    }

    [TestMethod]
    public void Attach_ForwardsConnections()
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
        var sink = new TreeSink();
        using var subscription = WorkflowEventRelay.Attach(tree, sink);

        tree.GetHelper().SendConnection(sa);
        tree.GetHelper().ReceiveConnection(sb);

        Assert.AreEqual(1, sink.ConnectingCount);
        Assert.AreEqual(1, sink.ConnectedCount);
        Assert.AreSame(sa, sink.Connecting!.Sender);
        Assert.AreSame(sb, sink.Connecting.Receiver);
    }

    private sealed class NodeSink : IWorkflowNodeEventSink
    {
        public bool RefuseMoving { get; init; }
        public int MovingCount { get; private set; }
        public int MovedCount { get; private set; }
        public NodeMoveEventArgs? Moving { get; private set; }

        public void OnMoving(NodeMoveEventArgs e)
        {
            MovingCount++;
            Moving = e;
            if (RefuseMoving) e.Handle.PreventDefault = true;
        }

        public void OnMoved(NodeMoveEventArgs e) => MovedCount++;
        public void OnResizing(NodeResizeEventArgs e) { }
        public void OnResized(NodeResizeEventArgs e) { }
        public void OnDeleting(NodeEventArgs e) { }
        public void OnDeleted(NodeEventArgs e) { }
    }

    private sealed class TreeSink : IWorkflowTreeEventSink
    {
        public int ConnectingCount { get; private set; }
        public int ConnectedCount { get; private set; }
        public ConnectionEventArgs? Connecting { get; private set; }

        public void OnConnecting(ConnectionEventArgs e)
        {
            ConnectingCount++;
            Connecting = e;
        }

        public void OnConnected(ConnectionEventArgs e) => ConnectedCount++;
    }
}
