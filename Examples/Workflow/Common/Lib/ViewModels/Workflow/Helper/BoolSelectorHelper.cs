using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class BoolSelectorHelper : NodeHelper<BoolSelectorNodeViewModel>
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return;

        var context = NetworkFlowContext.From(parameter);
        context.Variables.TryGetValue("selector.bool", out var conditionKey);
        var condition = conditionKey is not null
            ? bool.TryParse(conditionKey, out var parsed) && parsed
            : Component.Condition;

        Component.LastRouted = condition ? "→ True" : "→ False";

        var targetSlot = condition ? Component.TrueSlot : Component.FalseSlot;
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
