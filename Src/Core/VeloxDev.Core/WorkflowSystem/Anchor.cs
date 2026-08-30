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

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Anchor>(a => a.Horizontal, a => a.Vertical, a => a.Layer);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Anchor((double?)memberValues[0] ?? 0d, (double?)memberValues[1] ?? 0d, (int?)memberValues[2] ?? 0);

    public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);
    public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);
    public static Anchor operator +(Anchor left, Anchor right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical, left.Layer + right.Layer);
    public static Anchor operator -(Anchor left, Anchor right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical, left.Layer - right._layer);
}
