using System.Drawing;
using System.Windows.Forms;
using Easel.Visuals;
// `TreeView` collides with System.Windows.Forms.TreeView; alias the visual surface product.
using SampleTree = Demo.Views.Workflow.SampleTree;
using WorkflowSurface = Demo.Views.Workflow.TreeView;

namespace Demo;

public sealed class MainForm : Form
{
    // 窗口按 1085×683 逻辑 DIP 设计；客户区尺寸在句柄创建后按实际 DPI 换算。
    private static readonly Size s_logicalClientSize = new(1085, 683);

    public MainForm()
    {
        Text = "VeloxDev WinForms Trimmed";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = s_logicalClientSize;
        BackColor = Color.FromArgb(30, 30, 30);

        var tree = SampleTree.Create();
        var view = new WorkflowSurface(tree);
        // DataContext first: the host context must be in place before Root attaches the surface behavior,
        // otherwise the controller attaches without a tree and restores the viewport a beat later.
        Controls.Add(new VisualHost { Dock = DockStyle.Fill, DataContext = tree, Root = view });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLogicalClientSize();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyLogicalClientSize();

        // 框架会在本通知之后套用系统建议的物理矩形；排到消息队列末尾再校正一次。
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(ApplyLogicalClientSize));
        }
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
}
