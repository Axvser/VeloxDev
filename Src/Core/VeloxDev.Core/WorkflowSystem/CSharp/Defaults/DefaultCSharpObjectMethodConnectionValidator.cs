namespace VeloxDev.WorkflowSystem.CSharp;

public sealed class DefaultCSharpObjectMethodConnectionValidator
    : ICSharpObjectMethodConnectionValidator
{
    public bool TryValidate(
        MethodMember sender,
        MethodMember receiver,
        out bool canConnect)
    {
        if (sender is null) throw new ArgumentNullException(nameof(sender));
        if (receiver is null) throw new ArgumentNullException(nameof(receiver));

        if (!sender.ProducesWorkflowOutput
            || !receiver.AcceptsWorkflowInput
            || sender.Return.Count != 1
            || receiver.Parameters.Count != 1)
        {
            canConnect = false;
            return true;
        }

        var output = sender.Return[0];
        var input = receiver.Parameters[0];
        if (output.IsVoid || !input.UseWorkflowInput)
        {
            canConnect = false;
            return true;
        }

        var outputType = CSharpObjectTypeTool.ResolveType(output.ValueType);
        var inputType = CSharpObjectTypeTool.ResolveType(input.ValueType);
        canConnect = outputType is not null
            && inputType is not null
            && CSharpObjectTypeTool.IsTypeCompatible(outputType, inputType);
        return true;
    }
}
