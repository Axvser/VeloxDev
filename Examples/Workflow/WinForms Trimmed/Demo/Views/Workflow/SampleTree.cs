using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using VeloxDev.WorkflowSystem;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo.Views.Workflow;

/// <summary>The sample model the trimmed demo shows.</summary>
internal static class SampleTree
{
    /// <summary>A fresh sample model; the window's XAML uses it as the DataContext.</summary>
    public static TreeViewModel New => Create();

    private static TreeViewModel Create()
    {
        var tree = new TreeViewModel();

        // Shift the world origin into the visible area so nodes in every quadrant are on screen when zoomed.
        tree.Layout.NegativeOffset = new Offset(320, 260);

        var size = new Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Lower-right (+x,+y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = 140 } },
            new NodeViewModel { Name = "Lower-left  (-x,+y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = 140 } },
            new NodeViewModel { Name = "Upper-left  (-x,-y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = -140 } },
            new NodeViewModel { Name = "Upper-right (+x,-y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = -140 } }
        };

        foreach (var node in nodes)
        {
            tree.CreateNodeCommand.Execute(node);
        }

        nodes[0].OutputSlots.SetSelector(typeof(bool));
        nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
        nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
        nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[3].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // The seeded scene is the baseline; the first undo should target the user's edit, not tear it down.
        tree.GetHelper().ClearHistory();

        return tree;
    }
}
