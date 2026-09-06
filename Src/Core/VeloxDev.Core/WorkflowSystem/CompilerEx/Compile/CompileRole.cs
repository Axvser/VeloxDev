namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Declares the role the node passed to <see cref="CompilerViewModel.CompileAsync"/> plays:
/// - <see cref="Root"/>: treat the node as the root starter — compile the execution graph reachable from it
///   downstream, along Targets;
/// - <see cref="Terminal"/>: treat the node as the result terminal — walk backward along Sources to collect its
///   ancestor cone and compile only what is needed to compute that node, from the cone's own entry frontier.
/// </summary>
public enum CompileRole
{
    /// <summary>The node acts as a root starter: compile the sub-graph reachable downstream from it.</summary>
    Root,

    /// <summary>The node acts as a result terminal: compile its ancestor cone, computing only up to that node.</summary>
    Terminal,
}
