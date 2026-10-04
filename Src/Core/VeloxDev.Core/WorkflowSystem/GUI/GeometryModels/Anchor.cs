using System.Runtime.Serialization;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.WorkflowSystem;

/// <summary>A component's spatial position: horizontal and vertical coordinates and a layer.</summary>
[AgentContext(AgentLanguages.Chinese, "用于在工作流系统中描述组件的空间位置")]
[AgentContext(AgentLanguages.English, "Used to describe the spatial position of components in the workflow system")]
public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0)
    : ICloneable, IEquatable<Anchor>, IVeloxJsonSerializing, IVeloxJsonSerialized, IVeloxJsonDeserialized
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

    // 序列化状态（仅运行期，不写入 JSON）：
    //  - _collapseScale：瞬态被坍缩时的缩放，供 [OnSerializing] 写出原始/世界值。
    //  - _owner：这个瞬态所映射的那个字段。加载时 Newtonsoft 就地填充瞬态并绕过节点的 Anchor setter，
    //    所以 [OnDeserialized] 把原始值推回 _owner。
    [NonSerialized]
    private Scale? _collapseScale;
    [NonSerialized]
    private Anchor? _owner;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is Anchor other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical && Layer == other.Layer;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical, Layer);

    /// <inheritdoc />
    public override string ToString() => $"Anchor({Horizontal},{Vertical},{Layer})";

    /// <inheritdoc />
    public object Clone() => new Anchor(Horizontal, Vertical, Layer);

    /// <summary>Returns whether <paramref name="other"/> has the same coordinates and layer.</summary>
    public bool Equals(Anchor? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical && Layer == other.Layer;

    /// <summary>View value collapsed toward the world origin by <paramref name="scale"/> (identity when null/1). The transient remembers its scale and owner so serialization can restore the raw value.</summary>
    public Anchor Collapse(Scale? scale)
    {
        if (scale is null || (scale.Horizontal == 1d && scale.Vertical == 1d)) return this;
        var sx = scale.Horizontal == 0d ? 1d : 1d / scale.Horizontal;
        var sy = scale.Vertical == 0d ? 1d : 1d / scale.Vertical;
        if (sx == 1d && sy == 1d) return this;
        return new Anchor(Horizontal * sx, Vertical * sy, Layer) { _collapseScale = scale, _owner = this };
    }

    // 两个序列化器并存期间，Newtonsoft 的特性与 VeloxDev 的接口共用同一个函数体：
    // ComponentModelEx 接到新序列化器上之后，特性那一对就可以删了。
    [OnSerializing]
    private void OnSerializing(StreamingContext context) => ((IVeloxJsonSerializing)this).OnSerializing();

    [OnSerialized]
    private void OnSerialized(StreamingContext context) => ((IVeloxJsonSerialized)this).OnSerialized();

    void IVeloxJsonSerializing.OnSerializing()
    {
        // 把坍缩的瞬态展开回原始/世界值，让 JSON 文件存的是世界坐标。
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _horizontal *= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _vertical *= scaleY.Vertical;
        }
    }

    void IVeloxJsonSerialized.OnSerialized()
    {
        if (_collapseScale is { } scale && scale.Horizontal != 1d && scale.Horizontal != 0d)
        {
            _horizontal /= scale.Horizontal;
        }
        if (_collapseScale is { } scaleY && scaleY.Vertical != 1d && scaleY.Vertical != 0d)
        {
            _vertical /= scaleY.Vertical;
        }
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context) => ((IVeloxJsonDeserialized)this).OnDeserialized();

    void IVeloxJsonDeserialized.OnDeserialized()
    {
        // 读取器把原始 JSON 值填进了这个瞬态（经节点的 getter 读入）却跳过了 setter；
        // 把还原出来的值推回它坍缩自的那个字段。
        if (_owner is not null)
        {
            _owner._horizontal = _horizontal;
            _owner._vertical = _vertical;
            _owner._layer = _layer;
            _owner = null;
        }
        _collapseScale = null;
    }

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);

    /// <summary>Adds two anchors component-wise.</summary>
    public static Anchor operator +(Anchor left, Anchor right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical, left.Layer + right.Layer);

    /// <summary>Subtracts <paramref name="right"/> from <paramref name="left"/> component-wise.</summary>
    public static Anchor operator -(Anchor left, Anchor right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical, left.Layer - right._layer);
}
