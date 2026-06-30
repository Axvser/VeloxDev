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
    /// condition/selector values to the downstream nodes that should
    /// receive the output.
    ///
    /// Using direct node references instead of slot indices ensures
    /// the routing stays valid even if the user later modifies
    /// connection relationships.
    ///
    /// Example — a conditional node with "Yes" / "No" branches:
    ///   { "yes" → yesNode, "no" → noNode }
    /// </summary>
    IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable();
}
