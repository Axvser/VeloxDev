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

        // The grid decorator, minimap and HUD are part of the visual surface; the tree is bound through ViewModel.
        var tree = new TreeViewModel();
        LoadTree(tree);
        _treeView.ViewModel = tree;

        // 视觉表面用 Pan 定位世界；首帧布局完成后把世界原点放到 (320,260)，与其它六家的 Trimmed demo 一致。
        var positioned = false;
        _treeView.Surface.Paint += (_, _) =>
        {
            if (positioned)
            {
                return;
            }

            positioned = true;
            // Paint 事件早于表面的布局；排到消息队列末尾再定位，此时视口尺寸已经写入。
            _treeView.Surface.BeginInvoke(
                new Action(() => _treeView.Surface.SetPan(new System.Drawing.PointF(320f, 260f))));
        };
    }

    private static void LoadTree(TreeViewModel tree)
    {
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
    }
}
