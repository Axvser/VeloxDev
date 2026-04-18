using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class EnumSelectorHelper : NodeHelper<EnumSelectorNodeViewModel>
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return;

        var context = NetworkFlowContext.From(parameter);
        context.Variables.TryGetValue("selector.value", out var valueKey);

        object? routeValue = null;
        if (valueKey is not null && Component.EnumType is Type selectorType)
        {
            try { routeValue = Enum.Parse(selectorType, valueKey, true); } catch { }
        }
        routeValue ??= Component.SelectedValue;

        if (routeValue is null) return;

        Component.LastRouted = $"→ {routeValue}";

        var targetSlot = Component.GetSlotForValue(routeValue);
        if (targetSlot is null) return;

        foreach (var receiver in targetSlot.Targets.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            var receiverNode = receiver.Parent;
            if (receiverNode is null) continue;

            if (!await ValidateBroadcastAsync(targetSlot, receiver, parameter, ct).ConfigureAwait(false))
                continue;

            receiverNode.WorkCommand.Execute(parameter);
        }
    }
}
