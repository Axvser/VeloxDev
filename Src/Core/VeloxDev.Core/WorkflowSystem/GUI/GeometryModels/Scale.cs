using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维缩放因子，默认 1.0 表示不缩放")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional scale factor; 1.0 means no scaling")]
public sealed partial class Scale(double horizontal = 1d, double vertical = 1d) : ICloneable, IEquatable<Scale>, ISampleable
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "水平缩放因子，1.0 表示不缩放")]
    [AgentContext(AgentLanguages.English, "Horizontal scale factor, 1.0 means no scaling")]
    private double _horizontal = horizontal;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "垂直缩放因子，1.0 表示不缩放")]
    [AgentContext(AgentLanguages.English, "Vertical scale factor, 1.0 means no scaling")]
    private double _vertical = vertical;

    public override bool Equals(object? obj)
    {
        if (obj is Scale other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);
    public override string ToString() => $"Scale({Horizontal},{Vertical})";
    public object Clone() => new Scale(Horizontal, Vertical);
    public bool Equals(Scale? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical;

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Scale>(s => s.Horizontal, s => s.Vertical);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Scale((double?)memberValues[0] ?? 1d, (double?)memberValues[1] ?? 1d);

    public static bool operator ==(Scale left, Scale right) => left.Equals(right);
    public static bool operator !=(Scale left, Scale right) => !left.Equals(right);
    public static Scale operator +(Scale left, Scale right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);
    public static Scale operator -(Scale left, Scale right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
