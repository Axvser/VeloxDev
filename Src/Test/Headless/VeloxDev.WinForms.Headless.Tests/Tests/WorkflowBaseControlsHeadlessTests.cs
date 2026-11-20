using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

/// <summary>
/// The adapter-level behavior facades: node, slot and link views are ordinary controls that attach a helper.
/// The dynamic slot rows are a helper too, not a card base class.
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
    public void NodeRowsBehavior_BuildsARowForEachOutputSlot()
    {
        var graph = TestGraph.Create(connect: false);
        using var card = CreateCardWithRowsHost();
        var attachment = WorkflowNodeAttachment.Attach(card);

        WorkflowNodeRowsBehavior.SetSlotViewFactory(card, () => new Panel());
        WorkflowNodeRowsBehavior.SetIsEnabled(card, true);

        attachment.Node = graph.Left;
        Application.DoEvents();

        var rowsHost = Descendants(card).Single(control => control.Name == "PART_DynamicOutputs");
        Assert.IsTrue(rowsHost.Controls.Count > 0, "the bound node's slots must produce rows");
    }

    [TestMethod]
    public void NodeRowsBehavior_RendersAPureInputSlotAsTheBarePort()
    {
        var graph = TestGraph.Create(connect: false);
        graph.RightInput.GetHelper().SetChannel(SlotChannel.MultipleSources);

        using var card = CreateCardWithRowsHost();
        var attachment = WorkflowNodeAttachment.Attach(card);

        WorkflowNodeRowsBehavior.SetSlotViewFactory(card, () => new Panel());
        WorkflowNodeRowsBehavior.SetIsEnabled(card, true);

        attachment.Node = graph.Right;
        Application.DoEvents();

        var rowsHost = Descendants(card).Single(control => control.Name == "PART_DynamicOutputs");
        var rowsPanel = rowsHost.Controls.OfType<Panel>().Single();
        var slotViews = Descendants(rowsPanel)
            .Where(control => WorkflowSlotAttachment.For(control) is not null)
            .ToList();

        Assert.AreEqual(1, slotViews.Count, "a source-only slot is one bare input port, not an output row");
        Assert.AreSame(graph.RightInput, WorkflowSlotAttachment.For(slotViews[0])!.Slot);
    }

    private static Panel CreateCardWithRowsHost()
    {
        var card = new Panel { Width = 240, Height = 160 };
        card.Controls.Add(new Panel
        {
            Name = "PART_DynamicOutputs",
            Dock = DockStyle.Fill,
        });
        card.CreateControl();
        return card;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
