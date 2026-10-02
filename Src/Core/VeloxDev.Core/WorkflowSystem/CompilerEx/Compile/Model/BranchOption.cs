using System.Runtime.Serialization;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// One branch option: key + label + downstream sub-graph.
/// All branches of a dynamic branch stay alive (selected by key at runtime).
/// <see cref="IsTerminal"/>: the branch registers a route key but has no downstream node — selecting it at runtime
/// ends the whole run (the join-point tail segment is not propagated).
/// </summary>
public sealed partial class BranchOption : IVeloxJsonDeserialized
{
    [VeloxProperty] private object? _key;
    [VeloxProperty] private string? _label;
    [VeloxProperty] private CompiledGraph? _graph;
    [VeloxProperty] private bool _isTerminal;

    /// <summary>
    /// <see cref="Key"/>'s type, recorded by the compiler only when the key is an enum — see
    /// <see cref="CompileKeyNormalizer"/>. It has to be carried here as well as on
    /// <see cref="BranchSegment.CompileKey"/>: <see cref="Label"/> is the key's text, not its type, so without this
    /// a restored option could not be matched against a key re-resolved at run time.
    /// </summary>
    [VeloxProperty] private string? _keyTypeName;

    /// <summary>Restores an enum key after loading — see <see cref="BranchSegment.NormalizeCompileKey"/>.</summary>
    /// <remarks>
    /// Both hooks are wired while the two serializers coexist, sharing one body: the attribute is what the
    /// Newtonsoft path calls, the interface is what the VeloxDev serializer calls.
    /// </remarks>
    [OnDeserialized]
    internal void NormalizeKey(StreamingContext _) => ((IVeloxJsonDeserialized)this).OnDeserialized();

    void IVeloxJsonDeserialized.OnDeserialized()
        => Key = CompileKeyNormalizer.Normalize(Key, KeyTypeName);
}
