using System.Runtime.Serialization;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "用于在工作流系统中描述组件的空间位置")]
[AgentContext(AgentLanguages.English, "Used to describe the spatial position of components in the workflow system")]
public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0) : ICloneable, IEquatable<Anchor>, ISampleable
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "水平坐标，单位为像素")]
    [AgentContext(AgentLanguages.English, "Horizontal coordinate, in pixels")]
    private double _horizontal = left;
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "垂直坐标，单位为像素")]
    [AgentContext(AgentLanguages.English, "Vertical coordinate, in pixels")]
    private double _vertical = top;
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "图层，行为取决于GUI")]
    [AgentContext(AgentLanguages.English, "Layer, behavior depends on the GUI")]
    private int _layer = layer;

    // Serialization state (runtime-only, never written to JSON):
    //  - _collapseScale: scale the transient was collapsed by, so [OnSerializing] can write the raw/world value.
    //  - _owner: the field this transient is a collapsed view of. On load Newtonsoft populates the transient
    //    in place and skips the node's Anchor setter, so [OnDeserialized] pushes the raw value back into _owner.
    [NonSerialized]
    private Scale? _collapseScale;
    [NonSerialized]
    private Anchor? _owner;

    public override bool Equals(object? obj)
    {
        if (obj is Anchor other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical && Layer == other.Layer;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical, Layer);
    public override string ToString() => $"Anchor({Horizontal},{Vertical},{Layer})";
    public object Clone() => new Anchor(Horizontal, Vertical, Layer);
    public bool Equals(Anchor? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical && Layer == other.Layer;

    /// <summary>View value collapsed toward the world origin by <paramref name="scale"/> (identity when null/1). The transient remembers its scale and owner so serialization can restore the raw value.</summary>
    public Anchor Collapse(Scale? scale)
    {
        if (scale is null || (scale.Horizontal == 1d && scale.Vertical == 1d)) return this;
        var sx = scale.Horizontal == 0d ? 1d : 1d / scale.Horizontal;
        var sy = scale.Vertical == 0d ? 1d : 1d / scale.Vertical;
        if (sx == 1d && sy == 1d) return this;
        return new Anchor(Horizontal * sx, Vertical * sy, Layer) { _collapseScale = scale, _owner = this };
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        // Expand the collapsed transient back to raw/world values so the JSON file stores world coordinates.
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _horizontal *= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _vertical *= scaleY.Vertical;
        }
    }

    [OnSerialized]
    private void OnSerialized(StreamingContext context)
    {
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _horizontal /= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _vertical /= scaleY.Vertical;
        }
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        // Newtonsoft populated this transient (read through the node's getter) with the raw JSON value but
        // skipped the setter; push the restored value back into the field it was collapsed from.
        if (_owner is not null)
        {
            _owner._horizontal = _horizontal;
            _owner._vertical = _vertical;
            _owner._layer = _layer;
            _owner = null;
        }
        _collapseScale = null;
    }

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Anchor>(a => a.Horizontal, a => a.Vertical, a => a.Layer);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Anchor((double?)memberValues[0] ?? 0d, (double?)memberValues[1] ?? 0d, (int?)memberValues[2] ?? 0);

    public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);
    public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);
    public static Anchor operator +(Anchor left, Anchor right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical, left.Layer + right.Layer);
    public static Anchor operator -(Anchor left, Anchor right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical, left.Layer - right._layer);
}
