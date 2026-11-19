using global::Jalium.UI.Controls;
using VeloxDev.AdapterTestKit;
using VeloxDev.Jalium.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Jalium.Headless.Tests.Tests;

// Jalium 的事件附着属性与其余六家同名同形；这里只验节点那条线。
[TestClass]
public sealed class WorkflowEventsHeadlessTests
{
    private sealed class Sink : IWorkflowNodeEventSink
    {
        public int Moving;
        public int Moved;

        public void OnMoving(NodeMoveEventArgs e) => Moving++;
        public void OnMoved(NodeMoveEventArgs e) => Moved++;
        public void OnResizing(NodeResizeEventArgs e) { }
        public void OnResized(NodeResizeEventArgs e) { }
        public void OnDeleting(NodeEventArgs e) { }
        public void OnDeleted(NodeEventArgs e) { }
    }

    [TestMethod]
    public void NodeSink_HearsAMove()
    {
        var graph = TestGraph.Create();
        var sink = new Sink();
        var view = new UserControl { DataContext = graph.Left };

        WorkflowEvents.SetNode(view, sink);
        graph.Left.GetHelper().Move(new Offset(10d, 5d));
        JaliumHeadlessSurface.Pump();

        Assert.IsTrue(sink.Moving + sink.Moved > 0, "the node sink must hear the move");
        WorkflowEvents.SetNode(view, null);
    }
}
