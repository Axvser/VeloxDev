using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维尺寸")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional size")]
public sealed partial class Size(double width = 0d, double height = 0d) : ICloneable, IEquatable<Size>, ISampleable, ISampler
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

    public ISampler Normalize(object? start, object? end, object? options) => this;

    public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
    {
        if (t <= 0) { property.SetValue(target, start); return; }
        if (t >= 1) { property.SetValue(target, end); return; }

        var s1 = start as Size;
        var s2 = end as Size ?? new Size();
        var baseSize = s1 ?? new Size();
        var deltaW = s2.Width - baseSize.Width;
        var deltaH = s2.Height - baseSize.Height;

        if (s1 is null)
        {
            // 无现有实例可原地修改（空起点）→ 构造回退
            property.SetValue(target, new Size(
                baseSize.Width + deltaW * t,
                baseSize.Height + deltaH * t));
            return;
        }

        // 原地修改现有实例，不 new
        s1.Width += deltaW * t;
        s1.Height += deltaH * t;
    }

    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
