using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli;

/// <summary>
/// The session's host-level redirect setting: an optional node index the engine falls back to when a node reports
/// an error but has no <see cref="IRedirectable"/> contract of its own.
/// <para>
/// This is deliberately a session setting, not a parameter on a run call. The engine validates the target as an
/// upstream node of the failing one on every use; the setting is what <c>Context.HostRedirect</c> reads, so every
/// run started after it is set sees the same answer. An out-of-range index is refused when it is set, not silently
/// turned into "decline" later.
/// </para>
/// </summary>
internal sealed class RedirectPolicy(Func<IWorkflowTreeViewModel> tree)
{
    // -1 is "none"; valid node indices are non-negative. An int field is what lets the setting be read without a
    // lock from a run thread while an MCP tool changes it.
    private int _nodeIndex = -1;

    /// <summary>The index in force, or <c>null</c> for none.</summary>
    public int? NodeIndex
    {
        get
        {
            var index = Volatile.Read(ref _nodeIndex);
            return index < 0 ? null : index;
        }
    }

    /// <summary>Sets the fallback node. The index is validated against the tree the session is operating.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index does not name a node.</exception>
    public void Set(int? nodeIndex)
    {
        if (nodeIndex is { } index)
        {
            var count = tree().Nodes.Count;
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex), index,
                    $"The tree has {count} node(s); the index must be between 0 and {count - 1}.");
        }

        Volatile.Write(ref _nodeIndex, nodeIndex ?? -1);
    }

    /// <summary>The delegate installed on every run session. Returns <c>null</c> when nothing is configured.</summary>
    public IWorkflowNodeViewModel? Resolve(IRuntimeContext context, IWorkflowNodeViewModel failing)
    {
        var index = NodeIndex;
        if (index is not { } value) return null;

        var nodes = tree().Nodes;
        return value >= 0 && value < nodes.Count ? nodes[value] : null;
    }
}
