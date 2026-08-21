using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// A branch point: one router node plus all branch options.
/// Static branches (key known at compile-time) can prune unselected options, and runtime relies on
/// <see cref="CompileKey"/> (the selected value locked at compile time, unaffected by later runtime changes);
/// dynamic branches (<see cref="IsDynamic"/> = true) re-resolve the key at runtime via
/// <see cref="ICompileTimeRouter.ResolveRouteKey"/>.
/// </summary>
public sealed partial class BranchEntry : ActionEntry
{
    [VeloxProperty] private IWorkflowNodeViewModel? _router;
    [VeloxProperty] private ObservableCollection<BranchOption> _options = [];
    [VeloxProperty] private bool _isDynamic;
    [VeloxProperty] private object? _compileKey;
}
