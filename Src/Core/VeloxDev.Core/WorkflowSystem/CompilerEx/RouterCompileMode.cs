namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The router's compile mode: decides the branch dictionary returned by
/// <see cref="ICompileTimeRouter.GetRouteTable"/> at compile-time.
/// - <see cref="Static"/>: returns only the currently selected branch at compile-time; the compiled graph contains
///   only the selected path, with order fixed at compile-time.
/// - <see cref="Dynamic"/>: returns all branches at compile-time; the compiled graph keeps every branch and the key
///   is resolved via ResolveRouteKey at runtime.
/// </summary>
public enum RouterCompileMode
{
    /// <summary>Returns only the currently selected branch at compile-time (compiled graph = single path).</summary>
    Static,

    /// <summary>Returns all branches at compile-time (compiled graph = branch structure); runtime decides which one runs.</summary>
    Dynamic,
}
