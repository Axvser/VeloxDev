using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An ordered collection of compiled segments viewed as a compiled graph.
/// Nestable: BranchSegment / ParallelSegment each hold a child <see cref="CompiledGraph"/>.
/// The compiled output is immutable — it only describes the set of possible executions; the actual path is driven
/// by runtime state.
/// </summary>
public sealed partial class CompiledGraph
{
    [VeloxProperty] private ObservableCollection<CompileSegment> _entries = [];
}
