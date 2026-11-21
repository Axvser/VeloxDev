using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An ordered collection of compiled segments viewed as a compiled graph.
/// Nestable: BranchSegment / ParallelSegment each hold a child <see cref="CompiledGraph"/>.
/// The compiled output is immutable — it only describes the set of possible executions; the actual path is driven
/// by runtime state.
/// <para>
/// It also owns the <b>compile identity</b> of every node it contains. The compiler writes the bindings here and the
/// runtime reads them back from here; nodes are never asked to carry compiler state. That is why a node without
/// <see cref="ICompileTimeAware"/> still takes part in joins and redirects.
/// </para>
/// </summary>
public sealed partial class CompiledGraph
{
    [VeloxProperty] private ObservableCollection<CompileSegment> _entries = [];

    // 节点 → 编译身份。同一趟编译的每一层子图共享这一份；运行期只读，扇出的并发分支安全。
    // 不是 VeloxProperty ⇒ 不进归档：还原出来的图没有绑定，与还原节点丢失编译身份一致。
    internal IReadOnlyDictionary<IWorkflowNodeViewModel, NodeCompileInfo> CompileBindings { get; init; } =
        new Dictionary<IWorkflowNodeViewModel, NodeCompileInfo>(
            WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);

    /// <summary>
    /// Reads the compile identity the compiler assigned to <paramref name="node"/> in this graph.
    /// </summary>
    /// <param name="node">A node of this compiled graph.</param>
    /// <param name="info">The identity, when the node was compiled into this graph.</param>
    /// <returns>
    /// <see langword="true"/> when the node has a binding; <see langword="false"/> for nodes that were never
    /// compiled into it and for a graph restored from a document, whose bindings are not serialized.
    /// </returns>
    public bool TryGetCompileInfo(IWorkflowNodeViewModel node, out NodeCompileInfo? info)
    {
        if (node is null)
        {
            info = null;
            return false;
        }

        return CompileBindings.TryGetValue(node, out info);
    }
}
