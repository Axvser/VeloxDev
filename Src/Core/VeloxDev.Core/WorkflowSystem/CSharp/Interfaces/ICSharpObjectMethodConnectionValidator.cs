namespace VeloxDev.WorkflowSystem.CSharp;

/// <summary>
/// Determines whether an ordered sender method can connect to a receiver method.
/// </summary>
public interface ICSharpObjectMethodConnectionValidator
{
    /// <summary>
    /// Returns true when this validator handled the pair and sets the connection result.
    /// </summary>
    bool TryValidate(
        MethodMember sender,
        MethodMember receiver,
        out bool canConnect);
}
