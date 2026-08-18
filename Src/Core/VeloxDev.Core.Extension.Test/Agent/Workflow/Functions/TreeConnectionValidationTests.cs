using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 覆盖建连期（设计期）的连接校验层 —— <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/>。
/// 与运行期 <see cref="IAccessContext"/>（AccessAsync 边门禁，见 BroadcastModeTests）不同：
/// 树助手在画布拖线时否决一条边（返回 false → 连线不建立、虚拟连接复位），
/// 这是数据进入任何节点之前的第一道闸。默认实现恒 true，demo 亦未覆写 —— 需要测试本地助手触达。
/// </summary>
[TestClass]
public class TreeConnectionValidationTests
{
    [TestMethod]
    public void ValidateConnection_RejectsEdge_NoLinkIsCreated()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // 树助手否决这条边：拒绝连接 start.OutputSlot → sink.InputSlot。
        ((RejectingTreeHelper)helper).RejectSender = start.OutputSlot!;

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(sink.InputSlot!);

        Assert.AreEqual(0, tree.Links.Count, "vetoed edge never materializes as a link");
        Assert.AreEqual(0, start.OutputSlot!.Targets.Count, "sender slot gained no target");
        Assert.AreEqual(0, sink.InputSlot!.Sources.Count, "receiver slot gained no source");
        Assert.IsFalse(tree.VirtualLink.IsVisible, "virtual connection reset after the veto");
        Assert.AreEqual(SlotState.StandBy, start.OutputSlot.State,
            "sender slot returns to standby after the veto");
    }

    [TestMethod]
    public void ValidateConnection_AllowsEdge_LinkIsCreated()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // 控制组：不否决 —— 同一条边照常建立。
        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(sink.InputSlot!);

        Assert.AreEqual(1, tree.Links.Count, "non-vetoed edge connects normally");
        Assert.AreEqual(1, start.OutputSlot!.Targets.Count, "sender slot gained its target");
        Assert.AreEqual(1, sink.InputSlot!.Sources.Count, "receiver slot gained its source");
    }

    [TestMethod]
    public void ValidateConnection_RejectsSecondEdge_KeepsFirstAndResetsVirtualLink()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var a = new NodeViewModel { Title = "A", DelayMilliseconds = 1 };
        var b = new NodeViewModel { Title = "B", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(a); helper.CreateNode(b);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // 只拒绝 start → b 这条边。
        ((RejectingTreeHelper)helper).RejectSender = start.OutputSlot!;
        ((RejectingTreeHelper)helper).RejectReceiver = b.InputSlot!;

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(a.InputSlot!);   // 允许 → 建立
        Assert.AreEqual(1, tree.Links.Count, "first edge connected");

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(b.InputSlot!);   // 否决 → 不建立

        Assert.AreEqual(1, tree.Links.Count, "second edge vetoed — only the first link remains");
        Assert.IsFalse(tree.VirtualLink.IsVisible, "virtual connection reset after the veto");
    }
}

/// <summary>
/// 测试用树助手：按 (Sender, Receiver) 组合否决连接。
/// 默认放行；只拒绝 <see cref="RejectSender"/>（任意接收端）或
/// <see cref="RejectSender"/> + <see cref="RejectReceiver"/> 的组合。
/// </summary>
public class RejectingTreeHelper : TreeHelper
{
    public IWorkflowSlotViewModel? RejectSender { get; set; }
    public IWorkflowSlotViewModel? RejectReceiver { get; set; }

    public override bool ValidateConnection(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
    {
        if (RejectSender is null || !ReferenceEquals(sender, RejectSender))
            return true;
        // 命中被拒发送端：未指定 RejectReceiver 时拒绝全部目标；否则只拒绝精确配对。
        return RejectReceiver is not null && !ReferenceEquals(receiver, RejectReceiver);
    }
}
