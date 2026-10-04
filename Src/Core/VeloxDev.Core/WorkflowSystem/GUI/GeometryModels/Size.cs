using System.Runtime.Serialization;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A two-dimensional size.</summary>
[AgentContext(AgentLanguages.Chinese, "表示一个二维尺寸")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional size")]
public sealed partial class Size(double width = 0d, double height = 0d)
    : ICloneable, IEquatable<Size>
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "宽度，像素单位")]
    [AgentContext(AgentLanguages.English, "Width in pixels")]
    private double _width = width;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "高度，像素单位")]
    [AgentContext(AgentLanguages.English, "Height in pixels")]
    private double _height = height;

    // 序列化状态（仅运行期，不写入 JSON）：与 Anchor._collapseScale/_owner 同一套契约。
    [NonSerialized]
    private Scale? _collapseScale;
    [NonSerialized]
    private Size? _owner;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is Size size)
        {
            return Width == size.Width && Height == size.Height;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"Size({Width},{Height})";

    /// <inheritdoc />
    public object Clone() => new Size(Width, Height);

    /// <summary>Returns whether <paramref name="other"/> has the same dimensions.</summary>
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

    // 钩子直接挂在 BCL 那四个特性上，生成器会调它们。方法必须是 internal 或更宽 ——
    // 生成代码在同一个程序集的 VeloxDev.Serialization.Generated 里，private 够不着。
    [OnSerializing]
    internal void OnSerializing(StreamingContext context)
    {
        // 把坍缩的瞬态展开回原始/世界坐标，让 JSON 文件存的是世界坐标。
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
    internal void OnSerialized(StreamingContext context)
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
    internal void OnDeserialized(StreamingContext context)
    {
        // 读取器把原始 JSON 值填进了这个瞬态（经节点的 getter 读入）却跳过了 setter；
        // 把还原出来的值推回它坍缩自的那个字段。
        if (_owner is not null)
        {
            _owner._width = _width;
            _owner._height = _height;
            _owner = null;
        }
        _collapseScale = null;
    }

    /// <summary>Returns whether <paramref name="a"/> and <paramref name="b"/> are equal.</summary>
    public static bool operator ==(Size a, Size b) => a.Equals(b);

    /// <summary>Returns whether <paramref name="a"/> and <paramref name="b"/> differ.</summary>
    public static bool operator !=(Size a, Size b) => !a.Equals(b);

    /// <summary>Adds two sizes component-wise.</summary>
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);

    /// <summary>Subtracts <paramref name="b"/> from <paramref name="a"/> component-wise.</summary>
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
