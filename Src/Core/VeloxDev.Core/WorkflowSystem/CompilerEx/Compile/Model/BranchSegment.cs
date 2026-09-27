using System.Collections.ObjectModel;
using System.Runtime.Serialization;
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
public sealed partial class BranchSegment : CompileSegment
{
    [VeloxProperty] private IWorkflowNodeViewModel? _router;
    [VeloxProperty] private ObservableCollection<BranchOption> _options = [];
    [VeloxProperty] private bool _isDynamic;
    [VeloxProperty] private object? _compileKey;

    /// <summary>
    /// <see cref="CompileKey"/>'s type, recorded by the compiler only when the key is an enum — see
    /// <see cref="CompileKeyNormalizer"/>. <c>null</c> for every other key kind, which needs no side channel.
    /// </summary>
    [VeloxProperty] private string? _compileKeyTypeName;

    /// <summary>
    /// Restores an enum key after loading. Runs as a Newtonsoft callback rather than in the generated property
    /// setter: all properties are populated before the callback, whereas a setter would fire during the compiler's
    /// own assignment and, on load, before a type-name member declared later in the document had been read.
    /// </summary>
    [OnDeserialized]
    internal void NormalizeCompileKey(StreamingContext _)
        => CompileKey = CompileKeyNormalizer.Normalize(CompileKey, CompileKeyTypeName);
}
