using System.Runtime.Serialization;
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

    // Serialization state (runtime-only, never written to JSON): same contract as Anchor._collapseScale/_owner.
    [NonSerialized]
    private Scale? _collapseScale;
    [NonSerialized]
    private Size? _owner;

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

    /// <summary>View value collapsed toward the world origin by <paramref name="scale"/> (identity when null/1). The transient remembers its scale and owner so serialization can restore the raw value.</summary>
    public Size Collapse(Scale? scale)
    {
        if (scale is null || (scale.Horizontal == 1d && scale.Vertical == 1d)) return this;
        var sx = scale.Horizontal == 0d ? 1d : 1d / scale.Horizontal;
        var sy = scale.Vertical == 0d ? 1d : 1d / scale.Vertical;
        if (sx == 1d && sy == 1d) return this;
        return new Size(Width * sx, Height * sy) { _collapseScale = scale, _owner = this };
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        // Expand the collapsed transient back to raw/world values so the JSON file stores world coordinates.
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _width *= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _height *= scaleY.Vertical;
        }
    }

    [OnSerialized]
    private void OnSerialized(StreamingContext context)
    {
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _width /= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _height /= scaleY.Vertical;
        }
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        // Newtonsoft populated this transient (read through the node's getter) with the raw JSON value but
        // skipped the setter; push the restored value back into the field it was collapsed from.
        if (_owner is not null)
        {
            _owner._width = _width;
            _owner._height = _height;
            _owner = null;
        }
        _collapseScale = null;
    }

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.Members<Size>(s => s.Width, s => s.Height);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Size((double?)memberValues[0] ?? 0d, (double?)memberValues[1] ?? 0d);

    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
