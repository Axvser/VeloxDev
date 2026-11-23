using System.Drawing;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// The visual backend writes slot anchors from the template's measured slot parts. The node card and the port
// drag are separate attached behaviors; both are driven here through real pointer input.
[STATestClass]
public sealed class SlotLayoutHeadlessTests
{
    // The test template puts an 18x18 port flush with the node edge: its centre is 9px in and vertically
    // centred. OneTarget sits on the right edge, OneSource on the left.
    private const double SlotRadius = 9d;

    [TestMethod]
    public void NamedSlots_GetAnchorsFromTheirMeasuredCentres()
    {
        var graph = TestGraph.Create(connect: false);
        graph.LeftOutput.Channel = SlotChannel.OneTarget;
        graph.RightInput.Channel = SlotChannel.OneSource;

        using var surface = new WinFormsHeadlessSurface(graph);

        Assert.AreEqual(
            graph.Left.Anchor.Horizontal + graph.Left.Size.Width - SlotRadius,
            graph.LeftOutput.Anchor.Horizontal,
            1d,
            "an outgoing port anchors at the centre of the part on the right edge");
        Assert.AreEqual(
            graph.Left.Anchor.Vertical + (graph.Left.Size.Height / 2d),
            graph.LeftOutput.Anchor.Vertical,
            1d);

        Assert.AreEqual(
            graph.Right.Anchor.Horizontal + SlotRadius,
            graph.RightInput.Anchor.Horizontal,
            1d,
            "an incoming port anchors at the centre of the part on the left edge");
        Assert.AreEqual(
            graph.Right.Anchor.Vertical + (graph.Right.Size.Height / 2d),
            graph.RightInput.Anchor.Vertical,
            1d);
    }

    [TestMethod]
    public void NodeDrag_MovesTheAnchorThroughTheRealBehavior()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var anchor = graph.Left.Anchor;
        var pressWorld = new PointF(
            (float)(anchor.Horizontal + 60d),
            (float)(anchor.Vertical + 12d));
        var moveWorld = new PointF(pressWorld.X + 35f, pressWorld.Y + 25f);

        Assert.IsTrue(surface.Press(surface.WorldToSurface(pressWorld)));
        Assert.IsTrue(surface.MoveTo(surface.WorldToSurface(moveWorld)));
        Assert.IsTrue(surface.Release(surface.WorldToSurface(moveWorld)));

        Assert.AreEqual(anchor.Horizontal + 35d, graph.Left.Anchor.Horizontal, 1d,
            "the drag delta is measured in the canvas coordinate space");
        Assert.AreEqual(anchor.Vertical + 25d, graph.Left.Anchor.Vertical, 1d);
    }

    [TestMethod]
    public void SlotConnection_PressMoveRelease_CreatesTheLink()
    {
        var graph = TestGraph.Create(connect: false);
        graph.LeftOutput.Channel = SlotChannel.OneTarget;
        graph.RightInput.Channel = SlotChannel.OneSource;

        using var surface = new WinFormsHeadlessSurface(graph);
        Assert.AreEqual(0, graph.Tree.Links.Count);

        var senderPoint = surface.WorldToSurface(new PointF(
            (float)graph.LeftOutput.Anchor.Horizontal,
            (float)graph.LeftOutput.Anchor.Vertical));
        var receiverPoint = surface.WorldToSurface(new PointF(
            (float)graph.RightInput.Anchor.Horizontal,
            (float)graph.RightInput.Anchor.Vertical));

        Assert.IsTrue(surface.Press(senderPoint), "the press must land on the sender port");
        surface.MoveTo(receiverPoint);
        Assert.IsTrue(surface.Release(receiverPoint));

        Assert.AreEqual(1, graph.Tree.Links.Count,
            "releasing over the receiver must complete the connection");
        var link = graph.Tree.Links[0];
        Assert.AreSame(graph.LeftOutput, link.Sender);
        Assert.AreSame(graph.RightInput, link.Receiver);
    }
}
