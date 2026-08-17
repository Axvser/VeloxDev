using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// 选择器的无状态广播工具：只向**指定槽**（当前选中值对应的分支）的下游广播，
/// 而不是沿全部输出槽扇出。供 Bool/Enum 选择器在无状态（非编译广播）模式下按选中值走单分支。
/// </summary>
internal static class SelectorBroadcast
{
    /// <summary>
    /// 只把数据投递到 <paramref name="slot"/> 的下游目标（逐条过 owner 的 AccessAsync 门禁，
    /// 与 <c>StandardBroadcastAsync</c> 一致）。槽为空则无事发生。
    /// </summary>
    public static async Task ToSlotAsync(IWorkflowNodeViewModel owner, IWorkflowSlotViewModel? slot, object? data, CancellationToken ct)
    {
        if (slot is null) return;

        foreach (var receiver in slot.Targets.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            var receiverNode = receiver.Parent;
            if (receiverNode is null) continue;

            var ctx = new TaskContext(data, slot, receiver);
            var helper = owner.GetHelper();
            if (helper is not null && !await helper.AccessAsync(ctx, ct).ConfigureAwait(false))
                continue;

            receiverNode.ReceiveCommand.Execute(ctx);
        }
    }
}
