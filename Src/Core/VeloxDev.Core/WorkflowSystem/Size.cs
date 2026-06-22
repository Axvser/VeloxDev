using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维尺寸")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional size")]
public sealed partial class Size(double width = 0d, double height = 0d) : ICloneable, IEquatable<Size>, IInterpolable
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "宽度，像素单位")]
    [AgentContext(AgentLanguages.English, "Width in pixels")]
    private double _width = width;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "高度，像素单位")]
    [AgentContext(AgentLanguages.English, "Height in pixels")]
    private double _height = height;

    public override bool Equals(object? obj)
    {
        if (obj is Size size)
        {
            return Width == size.Width && Height == size.Height;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"Size({Width},{Height})";
    public object Clone() => new Size(Width, Height);
    public bool Equals(Size? other) => other is not null && Width == other.Width && Height == other.Height;

    public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
    {
        if (steps <= 0) return [];

        var s1 = start as Size ?? new Size();
        var s2 = end as Size ?? s1;
        if (steps == 1) return [s2];

        var deltaW = s2.Width - s1.Width;
        var deltaH = s2.Height - s1.Height;

        List<object?> result = new(steps);
        for (int i = 0; i < steps; i++)
        {
            var t = (double)i / (steps - 1);
            result.Add(new Size(
                s1.Width + deltaW * t,
                s1.Height + deltaH * t
            ));
        }
        result[0] = start;
        result[steps - 1] = end;
        return result;
    }

    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
