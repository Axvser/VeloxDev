namespace VeloxDev.AI;

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public class AgentContextAttribute(AgentLanguages language, string context) : Attribute
{
    public AgentLanguages Language { get; } = language;
    public string Context { get; } = context;
}