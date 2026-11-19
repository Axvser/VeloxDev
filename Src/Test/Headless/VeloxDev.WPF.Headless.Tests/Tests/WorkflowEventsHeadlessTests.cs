using System.Windows.Controls;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// WorkflowEvents 把模型的移动/缩放/删除转给宿主 sink；这里只验节点那条线。
[STATestClass]
public sealed class WorkflowEventsHeadlessTests
{
    private sealed class Sink : IWorkflowNodeEventSink
    {
        public int Moving;
        public int Moved;
        public int Resizing;
        public int Resized;
        public int Deleting;
        public int Deleted;

        public void OnMoving(NodeMoveEventArgs e) => Moving++;
        public void OnMoved(NodeMoveEventArgs e) => Moved++;
        public void OnResizing(NodeResizeEventArgs e) => Resizing++;
        public void OnResized(NodeResizeEventArgs e) => Resized++;
        public void OnDeleting(NodeEventArgs e) => Deleting++;
        public void OnDeleted(NodeEventArgs e) => Deleted++;
    }

    [TestMethod]
    public void NodeSink_HearsAMove()
    {
        var graph = TestGraph.Create();
        var sink = new Sink();
        var view = new UserControl { DataContext = graph.Left };

        WorkflowEvents.SetNode(view, sink);
        graph.Left.GetHelper().Move(new Offset(10d, 5d));
        WpfHeadlessSurface.Pump();

        Assert.IsTrue(sink.Moving + sink.Moved > 0, "the node sink must hear the move");
        WorkflowEvents.SetNode(view, null);
    }
}
