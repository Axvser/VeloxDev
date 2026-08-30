using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Microsoft.UI.Xaml;
using VeloxDev.WorkflowSystem;

namespace Demo;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var tree = new TreeViewModel();
        LoadTree(tree);
        ((FrameworkElement)Content).DataContext = tree;
    }

    private static void LoadTree(TreeViewModel tree)
    {
        // Shift the world origin (0,0) into the visible area so nodes in every quadrant are on screen
        // when the workspace zooms (each node collapses toward the origin by Layout.Scale).
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
    }
}
