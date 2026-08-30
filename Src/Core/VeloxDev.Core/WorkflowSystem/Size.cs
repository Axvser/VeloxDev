using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维尺寸")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional size")]
public sealed partial class Size(double width = 0d, double height = 0d) : ICloneable, IEquatable<Size>, ISampleable
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

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Size>(s => s.Width, s => s.Height);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Size((double?)memberValues[0] ?? 0d, (double?)memberValues[1] ?? 0d);

    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
