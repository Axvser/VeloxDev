using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An ordered collection of nodes forming a single linear segment (with no link between its first and last nodes)
/// is treated as one execution entry.
/// </summary>
public sealed partial class ExecuteEntry : ActionEntry
{
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> _nodes = [];
}
