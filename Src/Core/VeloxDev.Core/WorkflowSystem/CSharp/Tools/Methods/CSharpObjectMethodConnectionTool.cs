namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectMethodConnectionTool
{
    private static readonly ICSharpObjectMethodConnectionValidator DefaultValidator =
        new DefaultCSharpObjectMethodConnectionValidator();

    internal static bool CanConnect(
        MethodMember sender,
        MethodMember receiver,
        IEnumerable<ICSharpObjectMethodConnectionValidator> validators)
    {
        if (!sender.ProducesWorkflowOutput
            || !receiver.AcceptsWorkflowInput)
        {
            return false;
        }

        foreach (var validator in validators)
        {
            try
            {
                if (validator.TryValidate(sender, receiver, out var canConnect))
                {
                    return canConnect;
                }
            }
            catch
            {
            }
        }

        return DefaultValidator.TryValidate(sender, receiver, out var fallback)
            && fallback;
    }
}
