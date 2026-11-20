using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
// `TreeView` collides with System.Windows.Forms.TreeView; alias
// the surface product so `new WorkflowSurface()` is unambiguous in code.
using WorkflowSurface = Demo.Views.Workflow.TreeView;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; the drawing
// alias keeps `new Size(1200, 800)` (ClientSize) unambiguous.
using Size = System.Drawing.Size;

namespace Demo;

public sealed class MainForm : Form
{
    private readonly WorkflowSurface _treeView;

    public MainForm()
    {
        Text = "VeloxDev WinForms Trimmed";
        ClientSize = new Size(1200, 800);
        BackColor = Color.FromArgb(30, 30, 30);

        _treeView = new WorkflowSurface { Dock = DockStyle.Fill };
        Controls.Add(_treeView);

        // The grid decorator, minimap and HUD are PART_* controls declared in TreeView.xaml; the surface
        // behavior resolves them by name and pushes the layout into them once the tree is bound.
        var tree = new TreeViewModel();
        LoadTree(tree);
        _treeView.ViewModel = tree;
    }

    private static void LoadTree(TreeViewModel tree)
    {
        // Shift the world origin into the visible area so nodes in every quadrant are on screen when zoomed.
        tree.Layout.NegativeOffset = new Offset(320, 260);

        var size = new VeloxDev.WorkflowSystem.Size(260, 180);
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

        // 端口默认 SlotChannel.None，不能收发连接；先给三个输出端口开 OneTarget，再走标准收发路径连线。
        foreach (var node in nodes[..3])
        {
            node.OutputSlots[0].SetChannelCommand.Execute(SlotChannel.OneTarget);
        }

        Connect(tree, nodes[0].OutputSlots[0], nodes[1].InputSlot);
        Connect(tree, nodes[1].OutputSlots[0], nodes[3].InputSlot);
        Connect(tree, nodes[2].OutputSlots[0], nodes[0].InputSlot);
    }

    private static void Connect(TreeViewModel tree, SlotViewModel sender, SlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
