using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class EnumSelectorHelper : NodeHelper<EnumSelectorNodeViewModel>
{
    public override async Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return null;

        // 编译执行：引擎传入 RuntimeContext（IRuntimeContext + ITaskContext），编号编译期固定；
        // 只记录路由方向，不重写徽标。
        if (ctx is IRuntimeContext)
        {
            Component.LastRouted = Component.SelectedValue is { } sv ? $"→ {sv}" : "→ ?";
            return null;
        }

        var context = NetworkFlowContext.From(ctx.Data);
        context.Variables.TryGetValue("selector.value", out var valueKey);

        object? routeValue = null;
        if (valueKey is not null && Component.EnumType is Type selectorType)
        {
            try { routeValue = Enum.Parse(selectorType, valueKey, true); } catch { }
        }
        routeValue ??= Component.SelectedValue;

        Component.LastRouted = routeValue is not null ? $"→ {routeValue}" : "→ ?";
        context.RecordExecution(Component.LastRouted, out var order);
        Component.LastExecutionOrder = order;

        // 自动向下游传递（AutoBroadcast，默认 true）：沿全部输出槽扇出到下游。
        if (Component.AutoBroadcast)
            await BroadcastAsync(context, ct);

        return context;
    }
}
