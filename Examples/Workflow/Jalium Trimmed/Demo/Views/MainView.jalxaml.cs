using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo.Views;

/// <summary>
/// The demo's composition: the workflow tree view, and the sample model behind it.
/// </summary>
/// <remarks>
/// Nothing here wires the surface up — the tree view's markup names its own parts and
/// <c>WorkflowSurfaceBehavior</c> drives them, so this file only builds the sample data and hands the tree
/// over as the data context (the same shape the WPF demo uses).
/// </remarks>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        var tree = new TreeViewModel();
        LoadTree(tree);
        DataContext = tree;
    }

    private static void LoadTree(TreeViewModel tree)
    {
        var size = new Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Boolean routes", Size = size, Anchor = new Anchor { Horizontal = 80, Vertical = 80 } },
            new NodeViewModel { Name = "Voltage routes", Size = size, Anchor = new Anchor { Horizontal = 400, Vertical = 220 } },
            new NodeViewModel { Name = "Model routes", Size = size, Anchor = new Anchor { Horizontal = 720, Vertical = 80 } },
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
