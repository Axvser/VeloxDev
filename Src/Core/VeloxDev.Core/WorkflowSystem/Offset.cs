using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维坐标偏移量")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional coordinate offset")]
public sealed partial class Offset(double left = 0d, double top = 0d) : ICloneable, IEquatable<Offset>, ISampleable
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "水平偏移量，像素单位")]
    [AgentContext(AgentLanguages.English, "Horizontal offset in pixels")]
    private double _horizontal = left;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "垂直偏移量，像素单位")]
    [AgentContext(AgentLanguages.English, "Vertical offset in pixels")]
    private double _vertical = top;

    public override bool Equals(object? obj)
    {
        if (obj is Offset other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);
    public override string ToString() => $"Offset({Horizontal},{Vertical})";
    public object Clone() => new Offset(Horizontal, Vertical);
    public bool Equals(Offset? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical;

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Offset>(o => o.Horizontal, o => o.Vertical);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Offset((double?)memberValues[0] ?? 0d, (double?)memberValues[1] ?? 0d);

    public static bool operator ==(Offset left, Offset right) => left.Equals(right);
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);
    public static Offset operator +(Offset left, Offset right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);
    public static Offset operator -(Offset left, Offset right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
