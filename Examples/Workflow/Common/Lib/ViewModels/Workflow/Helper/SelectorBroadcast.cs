using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// Stateless broadcast helper for selectors: broadcasts only to the downstream of the **specified slot**
/// (the branch matching the currently selected value), rather than fanning out across all output slots.
/// Bool/Enum selectors use it to follow a single branch in stateless (non-compiled-broadcast) mode.
/// </summary>
internal static class SelectorBroadcast
{
    /// <summary>
    /// Delivers the data only to <paramref name="slot"/>'s downstream targets (each passes the owner's AccessAsync
    /// gate, consistent with <c>StandardBroadcastAsync</c>). A null slot does nothing.
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
