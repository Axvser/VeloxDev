using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem.Attachments;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

/// <summary>
/// The adapter-level behavior facades: node, slot and link views are ordinary controls that attach a helper.
/// </summary>
[STATestClass]
public sealed class WorkflowBaseControlsHeadlessTests
{
    [TestMethod]
    public void NodeAttachment_BindsAndPlacesTheCard()
    {
        var graph = TestGraph.Create(connect: false);
        using var host = new Panel { Width = 800, Height = 600 };
        using var card = new Panel { Width = 240, Height = 160 };
        host.Controls.Add(card);

        var attachment = WorkflowNodeAttachment.Attach(card);
        attachment.Node = graph.Left;
        attachment.ApplySurfacePosition(new Point(10, 20), new Offset(5, 5));

        Assert.AreSame(graph.Left, attachment.Node);
        Assert.AreSame(graph.Left, card.Tag);
        Assert.AreEqual(
            (int)Math.Round(graph.Left.Anchor.Horizontal) + 15,
            card.Location.X);
        Assert.AreEqual(
            (int)Math.Round(graph.Left.Anchor.Vertical) + 25,
            card.Location.Y);
    }

    [TestMethod]
    public void SlotAttachment_BindsTheSlotAndMirrorsItsState()
    {
        var graph = TestGraph.Create(connect: false);
        using var glyph = new Panel { Width = 18, Height = 18 };

        var attachment = WorkflowSlotAttachment.Attach(glyph);
        attachment.Slot = graph.LeftOutput;
        attachment.SlotPath = "M0,0 L1024,0 L1024,1024 Z";

        Assert.AreSame(graph.LeftOutput, attachment.Slot);
        Assert.AreSame(graph.LeftOutput, glyph.Tag);
        Assert.AreEqual(graph.LeftOutput.State, attachment.Slot?.State);
        Assert.IsNotNull(attachment.IconPath);
    }

    [TestMethod]
    public void LinkAttachment_BindsTheLinkAndAppliesItsPalette()
    {
        var graph = TestGraph.Create();
        using var view = new Panel { Width = 12, Height = 12 };

        var attachment = WorkflowLinkAttachment.Attach(view);
        attachment.Bind(graph.Link);
        attachment.LineColor = Color.Red;
        attachment.Thickness = 3f;
        attachment.SurfaceBackground = Color.Black;

        Assert.AreSame(graph.Link, attachment.Link);
        Assert.AreEqual(Color.Red, attachment.LineColor);
        Assert.AreEqual(3f, attachment.Thickness);
        Assert.AreEqual(Color.Black, attachment.SurfaceBackground);
    }

    [TestMethod]
    public void SlotConnectionBehavior_RegistersItsXamlAttachedProperty()
    {
        using var view = new Panel();

        var property = VeloxDev.WinForms.Xaml.XamlRuntime.RequireAttached(
            typeof(WorkflowSlotConnectionBehavior), "IsEnabled");
        property.SetValue(view, true);

        Assert.IsTrue(WorkflowSlotConnectionBehavior.GetIsEnabled(view));
    }

}
