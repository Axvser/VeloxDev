using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An ordered collection of nodes forming a single linear segment (with no link between its first and last nodes)
/// is treated as one chain segment.
/// </summary>
public sealed partial class ChainSegment : CompileSegment
{
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> _nodes = [];
}
