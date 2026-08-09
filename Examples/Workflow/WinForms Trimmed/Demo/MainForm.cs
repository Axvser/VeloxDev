using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using VeloxDev.WorkflowSystem;
// `TreeView` collides with System.Windows.Forms.TreeView (implicit using); alias
// the template product so `new WorkflowTreeView()` is unambiguous in code.
using WorkflowTreeView = Demo.Views.Workflow.TreeView;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; the drawing
// alias keeps `new Size(1200, 800)` (ClientSize) unambiguous.
using Size = System.Drawing.Size;

namespace Demo;

public sealed class MainForm : Form
{
    private readonly WorkflowTreeView _treeView;

    public MainForm()
    {
        Text = "VeloxDev WinForms Trimmed";
        ClientSize = new Size(1200, 800);
        BackColor = Color.FromArgb(30, 30, 30);

        _treeView = new WorkflowTreeView { Dock = DockStyle.Fill };
        Controls.Add(_treeView);

        // Minimap before ViewModel: the tree template's AttachTree refreshes only
        // when both the tree and the minimap overlay are present, so attach the
        // overlay first to get the initial decorator/minimap offsets pushed.
        _treeView.MinimapOverlay = new Demo.Views.Workflow.MinimapOverlay();

        var tree = new TreeViewModel();
        LoadTree(tree);
        _treeView.ViewModel = tree;
    }

    private static void LoadTree(TreeViewModel tree)
    {
        var size = new VeloxDev.WorkflowSystem.Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Boolean routes", Size = size, Anchor = new Anchor { Horizontal = 80, Vertical = 80 } },
            new NodeViewModel { Name = "Voltage routes", Size = size, Anchor = new Anchor { Horizontal = 400, Vertical = 220 } },
            new NodeViewModel { Name = "Model routes", Size = size, Anchor = new Anchor { Horizontal = 720, Vertical = 80 } }
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
