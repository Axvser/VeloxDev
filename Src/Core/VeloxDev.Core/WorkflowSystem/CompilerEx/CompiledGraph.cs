using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An ordered collection of execution entries viewed as a compiled graph.
/// Nestable: BranchEntry / ParallelEntry each hold a child <see cref="CompiledGraph"/>.
/// The compiled output is immutable — it only describes the set of possible executions; the actual path is driven
/// by runtime state.
/// </summary>
public sealed partial class CompiledGraph
{
    [VeloxProperty] private ObservableCollection<ActionEntry> _entries = [];
}
