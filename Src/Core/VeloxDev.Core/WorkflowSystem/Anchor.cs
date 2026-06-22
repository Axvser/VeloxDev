using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "用于在工作流系统中描述组件的空间位置")]
[AgentContext(AgentLanguages.English, "Used to describe the spatial position of components in the workflow system")]
public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0) : ICloneable, IEquatable<Anchor>, IInterpolable
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

    public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
    {
        if (steps <= 0) return [];

        var s1 = start as Anchor ?? new Anchor();
        var s2 = end as Anchor ?? s1;
        if (steps == 1) return [s2];

        var deltaH = s2.Horizontal - s1.Horizontal;
        var deltaV = s2.Vertical - s1.Vertical;
        var deltaL = s2.Layer - s1.Layer;

        List<object?> result = new(steps);
        for (int i = 0; i < steps; i++)
        {
            var t = (double)i / (steps - 1);
            result.Add(new Anchor(
                s1.Horizontal + deltaH * t,
                s1.Vertical + deltaV * t,
                s1.Layer + (int)Math.Round(deltaL * t)
            ));
        }
        result[0] = start;
        result[steps - 1] = end;
        return result;
    }

    public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);
    public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);
    public static Anchor operator +(Anchor left, Anchor right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical, left.Layer + right.Layer);
    public static Anchor operator -(Anchor left, Anchor right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical, left.Layer - right._layer);
}
