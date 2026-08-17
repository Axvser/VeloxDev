using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class BoolSelectorHelper : NodeHelper<BoolSelectorNodeViewModel>
{
    public override async Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return null;

        // 编译执行：引擎传入 RuntimeContext（IRuntimeContext + ITaskContext），编号编译期固定；
        // 只记录路由方向，不重写徽标。
        if (ctx is IRuntimeContext)
        {
            Component.LastRouted = Component.Condition ? "→ True" : "→ False";
            return null;
        }

        var context = NetworkFlowContext.From(ctx.Data);
        context.Variables.TryGetValue("selector.bool", out var conditionKey);
        var condition = conditionKey is not null
            ? bool.TryParse(conditionKey, out var parsed) && parsed
            : Component.Condition;

        Component.LastRouted = condition ? "→ True" : "→ False";
        // 只记路由轨迹；不写 LastExecutionOrder —— 编号徽标是编译机器专属，非编译器启动不扰动。
        context.RecordExecution(Component.LastRouted, out _);

        // 自动向下游传递（AutoBroadcast，默认 true）：无状态只沿**当前选中分支**（True/False 之一）广播。
        if (Component.AutoBroadcast)
            await SelectorBroadcast.ToSlotAsync(Component, condition ? Component.TrueSlot : Component.FalseSlot, context, ct);

        return context;
    }
}
