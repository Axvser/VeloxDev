using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class EnumSelectorHelper : NodeHelper<EnumSelectorNodeViewModel>
{
    public override Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return Task.CompletedTask;

        var context = NetworkFlowContext.From(parameter);
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
        Component.WorkResult = parameter; // 编译执行中直接透传数据
        return Task.CompletedTask;
    }
}
