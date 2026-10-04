using System;
using System.Collections.ObjectModel;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Serialization;

/// <summary>
/// Serializes a <see cref="CompiledGraph"/> as a document in its own right.
/// <para>
/// A compiled graph is an ordinary VeloxDev view model — segments holding nodes in observable collections, no slots
/// and no links in its own shape — so it serializes with the same machinery a tree does. What it needs on top is a
/// decision about <b>how far the document reaches</b>, because a node is the live canvas instance and its writable
/// members point outward: <c>Parent</c> reaches the tree, and a slot's <c>Targets</c>/<c>Sources</c> reach every node
/// connected to it. Serializing without a decision therefore costs the whole tree plus the whole connected component.
/// </para>
/// <para>
/// Two modes, chosen by <c>includeTree</c>:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Snapshot</b> (the default) — drops both outward edges. The document is the segment structure plus each
///     node's own state, which is what a deep copy, an archive or a list view wants. It is <i>not</i> re-mountable:
///     the restored nodes have no <c>Parent</c>, so nothing re-collapses their geometry for the zoom and nothing
///     marks the tree dirty when they move.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>With the tree</b> (<c>includeTree: true</c>) — keeps everything, so the restored graph can be put back on a
///     canvas. The cost is the size of the tree plus the connected component, and that the nodes' <c>Parent</c> is a
///     <i>second</i>, freshly constructed tree: the nodes come back wired to that copy, not to the one you have.
///     </description>
///   </item>
/// </list>
/// <para>
/// Independently of the mode, a restored node gets a <b>fresh <c>RuntimeId</c></b> (it is not a writable member) and
/// carries <b>no compile identity</b> (<c>ICompileTimeAware.CompileContext</c> is not writable either). Branch keys
/// are the exception that is repaired: an enum key comes back as its number from any round trip, so the compiler
/// records the type beside it and it is restored on load — see <c>CompileKeyNormalizer</c>.
/// </para>
/// </summary>
public static class CompiledGraphEx
{
    /// <summary>
    /// The declared property types a snapshot drops: the tree back-pointer (which reaches the tree) and the slot
    /// topology collections (which reach the whole connected component).
    /// </summary>
    private static readonly Type[] SnapshotExclusions =
    [
        typeof(IWorkflowTreeViewModel),
        typeof(ObservableCollection<IWorkflowSlotViewModel>),
    ];

    /// <summary>Serializes <paramref name="graph"/> as a snapshot — see the type's remarks for what that keeps.</summary>
    /// <param name="graph">The compiled graph to write.</param>
    /// <param name="includeTree">
    /// <c>false</c> (the default) for a self-contained snapshot; <c>true</c> to keep the outward references so the
    /// document can be re-mounted.
    /// </param>
    /// <param name="options">
    /// Optional serializer settings. <see cref="SerializationOptions"/> is a fluent builder that mutates itself, so
    /// the exclusions are applied to the instance you pass; pass <c>null</c> (or nothing) to leave it alone.
    /// </param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <c>null</c>.</exception>
    public static string SerializeCompiledGraph(
        this CompiledGraph graph, bool includeTree = false, SerializationOptions? options = null)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));

        return graph.Serialize(BuildOptions(includeTree, options));
    }

    /// <summary>Reads a document written by <see cref="SerializeCompiledGraph"/>.</summary>
    /// <param name="json">The document.</param>
    /// <returns>The restored graph, whose nodes hold only what the document carried.</returns>
    /// <remarks>
    /// No exclusions are applied on the way in, deliberately: the filter exists to stop the <i>writer</i> from
    /// following references out of the graph, and a reader follows nothing — a member the document does not contain
    /// simply stays as its constructor left it.
    /// </remarks>
    public static CompiledGraph? DeserializeCompiledGraph(this string json)
        => json.Deserialize<CompiledGraph>();

    private static SerializationOptions BuildOptions(bool includeTree, SerializationOptions? options)
    {
        var effective = options ?? SerializationOptions.Create();
        return includeTree ? effective : effective.WithExcludedPropertyTypes(SnapshotExclusions);
    }
}
