using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class BoolSelectorHelper : NodeHelper<BoolSelectorNodeViewModel>
{
    public override async Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return null;

        // Compiled execution: the engine passes RuntimeContext (IRuntimeContext + ITaskContext); the sequence number is fixed at compile time.
        // Only record the routing direction; do not rewrite the badge. The router is routing-only: it must pass
        // the incoming payload through (return ctx.Data), otherwise the selected branch would see null Data.
        if (ctx is IRuntimeContext)
        {
            Component.LastRouted = Component.Condition ? "→ True" : "→ False";
            return ctx.Data;
        }

        var context = NetworkFlowContext.From(ctx.Data);
        context.Variables.TryGetValue("selector.bool", out var conditionKey);
        var condition = conditionKey is not null
            ? bool.TryParse(conditionKey, out var parsed) && parsed
            : Component.Condition;

        Component.LastRouted = condition ? "→ True" : "→ False";
        // Record the routing trace only; do not write LastExecutionOrder — the number badge belongs to the compiled run, and non-compiler starts must not disturb it.
        context.RecordExecution(Component.LastRouted, out _);

        // Auto-forward downstream (AutoBroadcast, default true): in stateless mode, broadcast only along the **currently selected branch** (one of True/False).
        if (Component.AutoBroadcast)
            await SelectorBroadcast.ToSlotAsync(Component, condition ? Component.TrueSlot : Component.FalseSlot, context, ct);

        return context;
    }
}
