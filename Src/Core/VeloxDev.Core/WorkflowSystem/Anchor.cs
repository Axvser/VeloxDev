using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "用于在工作流系统中描述组件的空间位置")]
[AgentContext(AgentLanguages.English, "Used to describe the spatial position of components in the workflow system")]
public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0) : ICloneable, IEquatable<Anchor>, ISampleable, ISampler
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

    public ISampler Normalize(object? start, object? end, object? options) => this;

    public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
    {
        if (t <= 0) { property.SetValue(target, start); return; }
        if (t >= 1) { property.SetValue(target, end); return; }

        var s1 = start as Anchor;
        var s2 = end as Anchor ?? new Anchor();
        var baseAnchor = s1 ?? new Anchor();
        var deltaH = s2.Horizontal - baseAnchor.Horizontal;
        var deltaV = s2.Vertical - baseAnchor.Vertical;
        var deltaL = s2.Layer - baseAnchor.Layer;

        if (s1 is null)
        {
            // 无现有实例可原地修改（空起点）→ 构造回退
            property.SetValue(target, new Anchor(
                baseAnchor.Horizontal + deltaH * t,
                baseAnchor.Vertical + deltaV * t,
                baseAnchor.Layer + (int)Math.Round(deltaL * t)));
            return;
        }

        // 原地修改现有实例，不 new
        s1.Horizontal += deltaH * t;
        s1.Vertical += deltaV * t;
        s1.Layer += (int)Math.Round(deltaL * t);
    }

    public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);
    public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);
    public static Anchor operator +(Anchor left, Anchor right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical, left.Layer + right.Layer);
    public static Anchor operator -(Anchor left, Anchor right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical, left.Layer - right._layer);
}
