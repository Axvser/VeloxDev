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
    // 窗口按 1200×800 逻辑 DIP 设计；无论当前监视器 DPI 是多少，客户区都表示这个逻辑尺寸。
    private static readonly Size s_logicalClientSize = new(1200, 800);

    private readonly WorkflowSurface _treeView;

    public MainForm()
    {
        Text = "VeloxDev WinForms Trimmed";

        // 尺寸只由 ApplyLogicalClientSize 按当前 DPI 换算；关掉 WinForms 自己的 autoscale，避免两套缩放相乘。
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = s_logicalClientSize; // 句柄创建前按 96 DPI 的临时尺寸
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

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLogicalClientSize();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyLogicalClientSize();

        // 框架会在本通知之后套用系统建议的物理矩形；排到消息队列末尾再校正一次，保证客户区精确等于逻辑尺寸。
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(ApplyLogicalClientSize));
        }
    }

    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyLogicalClientSize();
    }

    private void ApplyLogicalClientSize()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var scale = DeviceDpi / 96f;
        var size = new Size(
            (int)Math.Round(s_logicalClientSize.Width * scale),
            (int)Math.Round(s_logicalClientSize.Height * scale));
        if (ClientSize != size)
        {
            ClientSize = size;
        }
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
