namespace VeloxDev.AI;

/// <summary>Attaches a natural-language description to a type or member, in one language.</summary>
[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public class AgentContextAttribute(AgentLanguages language = AgentLanguages.English, string context = "") : Attribute
{
    /// <summary>The language the description is written in.</summary>
    public AgentLanguages Language { get; } = language;

    /// <summary>The description text.</summary>
    public string Context { get; } = context;
}