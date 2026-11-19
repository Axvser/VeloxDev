using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 的插槽布局按名字找控件、用 Tag 取节点/插槽、PointToScreen→PointToClient 量中心。
// 不显示窗体也能建句柄；BeginInvoke 排的 Sync 用 Application.DoEvents 泵。
[STATestClass]
public sealed class SlotLayoutHeadlessTests
{
    [TestMethod]
    public void NamedSlots_GetAnchorsFromTheirMeasuredCentres()
    {
        var graph = TestGraph.Create(connect: false);
        var node = graph.Left;
        var input = new SlotDefaultViewModel();
        var output = new SlotDefaultViewModel();
        node.GetHelper().CreateSlot(input);
        node.GetHelper().CreateSlot(output);

        var inputView = new Panel { Name = "PART_InputSlot", Tag = input, Width = 16, Height = 16, Location = new Point(8, 8) };
        var outputView = new Panel { Name = "PART_OutputSlot", Tag = output, Width = 16, Height = 16, Location = new Point(136, 8) };

        var host = new Panel { Tag = node, Width = 160, Height = 90 };
        host.Controls.Add(inputView);
        host.Controls.Add(outputView);
        host.CreateControl();
        host.PerformLayout();
        Application.DoEvents();

        WorkflowSlotLayoutBehavior.SetSlotNames(host, "PART_InputSlot,PART_OutputSlot");
        WorkflowSlotLayoutBehavior.SetIsEnabled(host, true);
        Application.DoEvents();
        host.PerformLayout();
        Application.DoEvents();

        // WinForms 的坐标宿默认是宿主 Panel，走 SlotAnchorFromCanvasLocal：锚点 = 客户区中心。
        // 真实宿主里节点 Location 已含 node.Anchor；这里宿主未定位，所以就是 (16,16) / (144,16)。
        Assert.AreEqual(16d, input.Anchor.Horizontal, 2d);
        Assert.AreEqual(16d, input.Anchor.Vertical, 2d);
        Assert.AreEqual(144d, output.Anchor.Horizontal, 2d);
        Assert.AreEqual(16d, output.Anchor.Vertical, 2d);

        WorkflowSlotLayoutBehavior.SetIsEnabled(host, false);
        host.Dispose();
    }
}
