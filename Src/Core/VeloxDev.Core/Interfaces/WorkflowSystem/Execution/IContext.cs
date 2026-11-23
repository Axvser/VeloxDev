
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The root contract of the workflow context hierarchy. Every context entering node execution / the compiler
/// mechanism builds on it and uniformly carries a payload <see cref="Data"/> — the real data at runtime, always
/// null for a compile identity.
/// </summary>
public interface IContext
{
    /// <summary>Payload data; carries real data at runtime, always null for a compile identity.</summary>
    object? Data { get; }
}
