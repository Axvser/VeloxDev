using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维坐标偏移量")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional coordinate offset")]
public sealed partial class Offset(double left = 0d, double top = 0d) : ICloneable, IEquatable<Offset>, ISampleable, ISampler
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

    public ISampler Normalize(object? start, object? end, object? options) => this;

    public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
    {
        if (t <= 0) { property.SetValue(target, start); return; }
        if (t >= 1) { property.SetValue(target, end); return; }

        var s1 = start as Offset;
        var s2 = end as Offset ?? new Offset();
        var baseOffset = s1 ?? new Offset();
        var deltaH = s2.Horizontal - baseOffset.Horizontal;
        var deltaV = s2.Vertical - baseOffset.Vertical;

        if (s1 is null)
        {
            // 无现有实例可原地修改（空起点）→ 构造回退
            property.SetValue(target, new Offset(
                baseOffset.Horizontal + deltaH * t,
                baseOffset.Vertical + deltaV * t));
            return;
        }

        // 原地修改现有实例，不 new
        s1.Horizontal += deltaH * t;
        s1.Vertical += deltaV * t;
    }

    public static bool operator ==(Offset left, Offset right) => left.Equals(right);
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);
    public static Offset operator +(Offset left, Offset right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);
    public static Offset operator -(Offset left, Offset right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
