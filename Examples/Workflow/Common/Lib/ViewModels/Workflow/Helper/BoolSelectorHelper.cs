using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class BoolSelectorHelper : NodeHelper<BoolSelectorNodeViewModel>
{
    public override Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return Task.CompletedTask;

        var context = NetworkFlowContext.From(parameter);
        context.Variables.TryGetValue("selector.bool", out var conditionKey);
        var condition = conditionKey is not null
            ? bool.TryParse(conditionKey, out var parsed) && parsed
            : Component.Condition;

        Component.LastRouted = condition ? "→ True" : "→ False";
        Component.WorkResult = parameter; // 编译执行中直接透传数据
        return Task.CompletedTask;
    }
}
