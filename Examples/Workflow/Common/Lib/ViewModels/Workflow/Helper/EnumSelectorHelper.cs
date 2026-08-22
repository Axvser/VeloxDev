using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class EnumSelectorHelper : NodeHelper<EnumSelectorNodeViewModel>
{
    public override async Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return null;

        // Compiled execution: the engine passes RuntimeContext (IRuntimeContext + ITaskContext); the sequence number is fixed at compile time.
        // Only record the routing direction; do not rewrite the badge. The router is routing-only: it must pass
        // the incoming payload through (return ctx.Data), otherwise the selected branch would see null Data.
        if (ctx is IRuntimeContext)
        {
            Component.LastRouted = Component.SelectedValue is { } sv ? $"[{sv}]" : "[?]";
            return ctx.Data;
        }

        var context = NetworkFlowContext.From(ctx.Data);
        context.Variables.TryGetValue("selector.value", out var valueKey);

        object? routeValue = null;
        if (valueKey is not null && Component.EnumType is Type selectorType)
        {
            try { routeValue = Enum.Parse(selectorType, valueKey, true); } catch { }
        }
        routeValue ??= Component.SelectedValue;

        Component.LastRouted = routeValue is not null ? $"[{routeValue}]" : "[?]";
        // Record the routing trace only; do not write LastExecutionOrder — the number badge belongs to the compiled run, and non-compiler starts must not disturb it.
        context.RecordExecution(Component.LastRouted, out _);

        // Auto-forward downstream (AutoBroadcast, default true): in stateless mode, broadcast only along the branch matching the **currently selected value**.
        if (Component.AutoBroadcast && routeValue is not null)
            await SelectorBroadcast.ToSlotAsync(Component, Component.GetSlotForValue(routeValue), context, ct);

        return context;
    }
}
