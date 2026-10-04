using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The canvas geometry: origin size, offsets, zoom scale and zoom settings.</summary>
public sealed partial class CanvasLayout : ICloneable, IEquatable<CanvasLayout>
{
    [VeloxProperty] private Size originSize = new(1920, 1080);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);

    [VeloxProperty] private Scale scale = new(1, 1);

    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);

    [VeloxProperty] private Offset viewportOffset = new(0, 0);

    [VeloxProperty] private ZoomCenter zoomCenter = ZoomCenter.ViewportCenter;

    [VeloxProperty] private Anchor collapsePivot = new(0, 0, 0);

    /// <summary>Returns a copy of this layout adapted to <paramref name="targetOriginSize"/>, with a viewport position that centers it.</summary>
    /// <param name="targetOriginSize">The origin size the copy starts from.</param>
    /// <param name="suggestedViewportX">The suggested horizontal viewport position.</param>
    /// <param name="suggestedViewportY">The suggested vertical viewport position.</param>
    public CanvasLayout AdaptTo(
        Size targetOriginSize,
        out double suggestedViewportX,
        out double suggestedViewportY)
    {
        var adapted = new CanvasLayout
        {
            OriginSize     = new Size(targetOriginSize.Width, targetOriginSize.Height),
            PositiveOffset = new Offset(PositiveOffset.Horizontal, PositiveOffset.Vertical),
            NegativeOffset = new Offset(NegativeOffset.Horizontal, NegativeOffset.Vertical),
            Scale          = new Scale(Scale.Horizontal, Scale.Vertical),
            ViewportOffset = new Offset(ViewportOffset.Horizontal, ViewportOffset.Vertical),
            ZoomCenter     = ZoomCenter,
            CollapsePivot  = new Anchor(CollapsePivot.Horizontal, CollapsePivot.Vertical, CollapsePivot.Layer),
        };

        var newActualWidth  = targetOriginSize.Width  + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var newActualHeight = targetOriginSize.Height + PositiveOffset.Vertical   + NegativeOffset.Vertical;

        suggestedViewportX = newActualWidth  / 2.0 - NegativeOffset.Horizontal;
        suggestedViewportY = newActualHeight / 2.0 - NegativeOffset.Vertical;

        return adapted;
    }

    /// <summary>Returns a copy of this layout adapted to <paramref name="targetOriginSize"/>.</summary>
    /// <param name="targetOriginSize">The origin size the copy starts from.</param>
    public CanvasLayout AdaptTo(Size targetOriginSize)
        => AdaptTo(targetOriginSize, out _, out _);

    /// <summary>Returns whether <paramref name="other"/> has the same geometry and zoom settings.</summary>
    public bool Equals(CanvasLayout? other)
        => other is not null &&
           OriginSize == other.OriginSize &&
           PositiveOffset == other.PositiveOffset &&
           NegativeOffset == other.NegativeOffset &&
           Scale == other.Scale &&
           ZoomCenter == other.ZoomCenter;

    /// <inheritdoc />
    public object Clone() => new CanvasLayout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        PositiveOffset = new Offset(this.PositiveOffset.Horizontal, this.PositiveOffset.Vertical),
        NegativeOffset = new Offset(this.NegativeOffset.Horizontal, this.NegativeOffset.Vertical),
        Scale = new Scale(this.Scale.Horizontal, this.Scale.Vertical),
        ViewportOffset = new Offset(this.ViewportOffset.Horizontal, this.ViewportOffset.Vertical),
        ZoomCenter = this.ZoomCenter,
        CollapsePivot = new Anchor(this.CollapsePivot.Horizontal, this.CollapsePivot.Vertical, this.CollapsePivot.Layer),
    };

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is CanvasLayout layout)
        {
            return OriginSize == layout.OriginSize &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset &&
                   Scale == layout.Scale &&
                   ZoomCenter == layout.ZoomCenter;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, PositiveOffset, NegativeOffset, Scale, ZoomCenter);
    }

    [VeloxCommand]
    private Task Update(object? parameter, CancellationToken ct)
    {
        Update();
        return Task.CompletedTask;
    }
    private void Update()
    {
        // 两种缩放模式下画布几何相同：节点一律朝世界原点坍缩（Anchor/Size 的 getter 除以 Scale），
        // 画布位置为 scroll − NegativeOffset；ViewportCenter 缩放只移动视口、保持该几何，靠滚动把坍缩
        // 轴的世界点压在视口中心下，从不平移或缩放画布本身。
        var baseWidth = OriginSize.Width + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var baseHeight = OriginSize.Height + PositiveOffset.Vertical + NegativeOffset.Vertical;

        // 放大时自动扩展：Scale < 1 时坍缩内容按 1/Scale 超出世界范围，需把可滚动尺寸补到能容纳
        // （Scale > 1 已能容纳）。视口/表面据 ActualSize 重算，滚动范围随之更新。
        var sx = Scale.Horizontal > 0 && Scale.Horizontal < 1 ? 1d / Scale.Horizontal : 1d;
        var sy = Scale.Vertical > 0 && Scale.Vertical < 1 ? 1d / Scale.Vertical : 1d;

        ActualSize.Width = baseWidth * sx;
        ActualSize.Height = baseHeight * sy;
        ActualOffset = new Offset(NegativeOffset.Horizontal, NegativeOffset.Vertical);

        OnPropertyChanged(nameof(ActualSize));
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();

    // Scale 只影响每个节点的视图变换，不影响世界空间布局；重新触发 ActualSize/Offset 让视图刷新。
    partial void OnScaleChanged(Scale oldValue, Scale newValue) => Update();

    // CollapsePivot 由适配器在一次缩放手势中紧挨着 Scale 之前写入；随后的 Scale 变化会重算范围，
    // 所以只改 pivot 时不应再调整画布尺寸。
    partial void OnCollapsePivotChanged(Anchor oldValue, Anchor newValue) { }
    partial void OnZoomCenterChanged(ZoomCenter oldValue, ZoomCenter newValue) { }
}
