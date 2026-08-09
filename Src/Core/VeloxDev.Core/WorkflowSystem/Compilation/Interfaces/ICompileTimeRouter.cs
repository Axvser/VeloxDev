namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Optional interface for nodes that own dynamic/conditional slots
/// (e.g., branching nodes driven by a SlotEnumerator).
///
/// During compilation, the compiler calls <see cref="GetRouteTable"/>
/// to pre-collect the routing logic. This makes compilation safe:
/// the routing is determined once at compile time and is not affected
/// by runtime user manipulation of the node's slots.
/// The compiled route table is stored on <see cref="CompiledItem.RouteTable"/>
/// for execution-time lookup.
/// </summary>
public interface ICompileTimeRouter
{
    /// <summary>
    /// Called during compilation. Returns a read-only mapping from
    /// condition/selector values to the downstream node(s) that should
    /// receive the output.
    ///
    /// A single branch may fan out to MULTIPLE downstream nodes (e.g. one
    /// selector value connected to two targets). The value is a list so every
    /// target is retained — a 1:1 mapping would silently drop all-but-last.
    ///
    /// Using direct node references instead of slot indices ensures
    /// the routing stays valid even if the user later modifies
    /// connection relationships.
    ///
    /// Example — a conditional node with "Yes" / "No" branches:
    ///   { "yes" → [yesNode], "no" → [noNode] }
    /// Example — a branch that fans out:
    ///   { "c" → [node3, node4] }
    /// </summary>
    IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> GetRouteTable();

    /// <summary>
    /// Called at execution time after <see cref="GetRouteTable"/> was collected.
    /// Returns the currently selected route key, which the executor uses to
    /// skip items exclusively reachable via unchosen branches.
    ///
    /// Example — a BoolSelector with <c>Condition = true</c> returns <c>true</c>,
    /// so items exclusive to the <c>false</c> branch are skipped.
    /// </summary>
    object? GetCurrentRouteKey();
}
