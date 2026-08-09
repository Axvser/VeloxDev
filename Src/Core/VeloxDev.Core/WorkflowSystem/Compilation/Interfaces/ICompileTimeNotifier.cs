namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Optional interface for workflow nodes that want to be notified at compile time,
/// as soon as they are assigned a compiled identity.
///
/// Unlike <see cref="ICompileTimeSink"/> (execution lifecycle notifications),
/// this fires synchronously during <see cref="WorkflowCompiler.Compile"/> — before any
/// execution begins — so a node can learn its own compiled <see cref="CompiledIdentity"/>
/// (composite UID + sequential ID) the moment it is compiled.
/// </summary>
public interface ICompileTimeNotifier
{
    /// <summary>
    /// Called once per compiled item, immediately after the item is added to its
    /// <see cref="CompilationResult"/>. Implement to react to the assignment of a compiled
    /// identity.
    /// </summary>
    /// <param name="item">The compiled item wrapping this node.</param>
    void OnCompiled(CompiledItem item);
}
