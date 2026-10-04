namespace VeloxDev.AI;

/// <summary>
/// Specifies the expected parameter type for a workflow command.
/// Applied alongside <see cref="AgentContextAttribute"/> to indicate
/// the concrete .NET type the Agent should construct when invoking the command.
/// A <see langword="null"/> <see cref="ParameterType"/> means the command takes no parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public class AgentCommandParameterAttribute : Attribute
{
    /// <summary>
    /// The .NET type of the command parameter, or <see langword="null"/> if the command requires no parameter.
    /// </summary>
    public Type? ParameterType { get; }

    /// <summary>Declares that the command takes no parameter.</summary>
    public AgentCommandParameterAttribute() => ParameterType = null;

    /// <summary>Declares <paramref name="parameterType"/> as the command's parameter type.</summary>
    public AgentCommandParameterAttribute(Type parameterType) => ParameterType = parameterType;
}
