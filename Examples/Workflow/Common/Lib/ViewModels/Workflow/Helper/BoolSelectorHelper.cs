using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class BoolSelectorHelper : NodeHelper<BoolSelectorNodeViewModel>
{
    public override Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return Task.CompletedTask;

        // 编译执行：引擎传入 RuntimeContext，编号编译期固定；只记录路由方向，不重写徽标。
        if (parameter is RuntimeContext)
        {
            Component.LastRouted = Component.Condition ? "→ True" : "→ False";
            return Task.CompletedTask;
        }

        var context = NetworkFlowContext.From(parameter);
        context.Variables.TryGetValue("selector.bool", out var conditionKey);
        var condition = conditionKey is not null
            ? bool.TryParse(conditionKey, out var parsed) && parsed
            : Component.Condition;

        Component.LastRouted = condition ? "→ True" : "→ False";
        context.RecordExecution(Component.LastRouted, out var order);
        Component.LastExecutionOrder = order;
        return Task.CompletedTask;
    }
}
