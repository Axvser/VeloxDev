using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The routing contract a node declares for "upstream data → broadcast targets":
/// - At compile-time <see cref="GetRouteTable"/> declares the branch structure (key → downstream nodes), and the
///   compiler generates BranchSegment from it;
/// - At runtime <see cref="ResolveRouteKey"/> receives the current data payload and returns the key → decides which
///   branch to broadcast to.
/// Static selectors (preset keys) ignore the payload; dynamic routing computes the key from the payload and returns
/// null when called with null at compile-time (cannot be decided at compile-time → all branches stay alive).
/// </summary>
public interface ICompileTimeRouter
{
    /// <summary>Branch table: key → downstream nodes. One branch can fan out to multiple targets.</summary>
    Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable();

    /// <summary>Given the current data payload (an <see cref="IRuntimeContext"/> instance at runtime, null at compile-time), returns the route key.</summary>
    Task<object?> ResolveRouteKey(object? payload);
}
