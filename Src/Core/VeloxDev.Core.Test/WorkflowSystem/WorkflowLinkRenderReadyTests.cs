using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// 核心 NaN 值判定下 Link 渲染就绪契约的单元测试。
///
/// 契约:链接可见,且双端端点锚点均已就位 —— slot 锚点默认水平/垂直坐标为
/// <see cref="double.NaN"/>(无值 / 待 GUI 测量),真实 GUI 测量写入非 NaN 坐标后才放行。
/// 测试直接设置 slot.Anchor 模拟 GUI 测量;不依赖任何事件、时间戳或 GUI 通知。
/// </summary>
[TestClass]
public class WorkflowLinkRenderReadyTests
{
    private static IWorkflowNodeViewModel NodeWithSlots(params IWorkflowSlotViewModel[] slots)
    {
        var node = new NodeDefaultViewModel();
        foreach (var slot in slots)
        {
            slot.Parent = node;
            node.Slots.Add(slot);
        }
        return node;
    }

    private static IWorkflowSlotViewModel Slot() => new SlotDefaultViewModel();

    private static IWorkflowLinkViewModel Link(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        => new LinkDefaultViewModel { Sender = sender, Receiver = receiver, IsVisible = true };

    [TestMethod]
    public void NotVisible_NotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = new LinkDefaultViewModel { Sender = a.Slots[0], Receiver = b.Slots[0] };
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsVisible);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void DefaultAnchor_NotReady()
    {
        // 默认锚点为 NaN(未测量) → 不渲染,避免读到"无值"坐标的错位帧。
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        Assert.IsTrue(double.IsNaN(a.Slots[0].Anchor.Horizontal));
        Assert.IsTrue(double.IsNaN(a.Slots[0].Anchor.Vertical));
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void PlaceholderEndpoint_Ready()
    {
        // 未挂载到节点的占位端点(Parent 空,如拖拽预览/虚拟链接占位端点)没有可等待的布局 → 就绪。
        var link = Link(Slot(), Slot());
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void SingleEndpointMeasured_StillNotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void BothEndpointsMeasured_Ready()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void MeasuredThenDefaulted_NotReady()
    {
        // 锚点从已测量值回到默认 NaN(如虚拟链接重置) → 不再就绪。
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());

        b.Slots[0].Anchor = new Anchor(double.NaN, double.NaN, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void OriginAnchor_IsMeasured()
    {
        // 真实测量可能恰好是原点 (0,0,0)(节点在画布原点),此时应视为已就绪。
        // NaN 判定与 (0,0) 无关 —— 这是纯值判定(判 0)做不到的。
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(0, 0, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void HorizontalNaNOnly_NotReady()
    {
        // 只有水平坐标 NaN 也视为未就绪(任一分量未测量即拦截)。
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(double.NaN, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void VerticalNaNOnly_NotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, double.NaN, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void MeasurementResumed_Ready()
    {
        // 离开(重置为 NaN)后再测量 → 恢复就绪。
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());

        a.Slots[0].Anchor = new Anchor(double.NaN, double.NaN, 0);
        Assert.IsFalse(link.IsRenderReady());

        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void NodeWithoutSlots_LinkNotReady()
    {
        // 链接端点引用节点上不存在的 slot → 无测量来源,拦截渲染。
        var a = NodeWithSlots();
        var b = NodeWithSlots(Slot());
        var link = Link(new SlotDefaultViewModel { Parent = a }, b.Slots[0]);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void DynamicSlot_MeasuredAfterCreation_Ready()
    {
        // 节点创建后动态新增的 slot,测量后即就绪,无订阅依赖。
        var node = NodeWithSlots();
        var b = NodeWithSlots(Slot());
        var dynamicSlot = Slot();
        dynamicSlot.Parent = node;
        node.Slots.Add(dynamicSlot);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        dynamicSlot.Anchor = new Anchor(100, 50, 0);
        var link = Link(dynamicSlot, b.Slots[0]);
        Assert.IsTrue(link.IsRenderReady());
    }
}
