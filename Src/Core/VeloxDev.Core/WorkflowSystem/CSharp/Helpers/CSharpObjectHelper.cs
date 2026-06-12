namespace VeloxDev.WorkflowSystem.CSharp;

public sealed class CSharpObjectHelper : NodeHelper<CSharpObject>
{
    /// <summary>
    /// Optional runtime-only parameter forwarded to registered value converters.
    /// </summary>
    public object? ConversionParameter { get; set; }

    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        if (Component is null) return;

        var method = Component.SelectedMethodMember
            ?? throw new InvalidOperationException(
                "No host method has been selected.");
        var result = await Component.InvokeSelectedMethodAsync(
            method.AcceptsWorkflowInput ? parameter : null,
            ConversionParameter,
            ct).ConfigureAwait(false);

        if (method.ProducesWorkflowOutput)
        {
            await BroadcastAsync(result, ct).ConfigureAwait(false);
        }
    }

    public override async Task<bool> ValidateBroadcastAsync(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver,
        object? parameter,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Component?.SelectedMethodMember?.ProducesWorkflowOutput != true)
        {
            return false;
        }

        if (receiver.Parent?.GetHelper() is CSharpObjectHelper receiverHelper
            && receiverHelper.Component is not null)
        {
            if (Component is null
                || !Component.CanConnectTo(receiverHelper.Component))
            {
                return false;
            }
        }

        return await base.ValidateBroadcastAsync(
            sender,
            receiver,
            parameter,
            ct).ConfigureAwait(false);
    }
}
