namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The sizing and placement inputs the adapter pushes into the minimap pipeline before a pass:
/// requested size, edge gap, minimum size, draw padding, corner radii, and the free corner of the surface.
/// </summary>
public readonly struct MinimapMetrics : IEquatable<MinimapMetrics>
{
    /// <summary>The requested minimap width before the available corner clamps it.</summary>
    public double Width { get; init; }

    /// <summary>The requested minimap height before the available corner clamps it.</summary>
    public double Height { get; init; }

    /// <summary>The free corner the minimap is placed in, including its gap.</summary>
    public SurfaceAvailableArea Available { get; init; }

    /// <summary>The smallest width or height the minimap may shrink to.</summary>
    public double MinSize { get; init; }

    /// <summary>The inner padding around the mapped content.</summary>
    public double Padding { get; init; }

    /// <summary>The corner radius applied to each node thumbnail.</summary>
    public double NodeCornerRadius { get; init; }

    /// <summary>The corner radius applied to the minimap frame.</summary>
    public double MinimapCornerRadius { get; init; }

    /// <summary>The smallest thumbnail width or height, so tiny nodes stay visible.</summary>
    public double MinimumThumbSize { get; init; }

    /// <summary>The default metrics: a 200 x 140 minimap, 12 gap, 20 minimum size, 2 padding, and 2 minimum thumb size.</summary>
    public static MinimapMetrics Default => new()
    {
        Width = 200d,
        Height = 140d,
        Available = new SurfaceAvailableArea { Margin = 12d },
        MinSize = 20d,
        Padding = 2d,
        NodeCornerRadius = 1d,
        MinimapCornerRadius = 4d,
        MinimumThumbSize = 2d,
    };

    /// <summary>Compares every placement input; used to avoid remapping when an adapter pushes unchanged values.</summary>
    /// <param name="other">The metrics to compare with.</param>
    /// <returns><see langword="true"/> when all inputs are equal.</returns>
    public bool Equals(MinimapMetrics other)
        => Width.Equals(other.Width)
            && Height.Equals(other.Height)
            && Available.Equals(other.Available)
            && MinSize.Equals(other.MinSize)
            && Padding.Equals(other.Padding)
            && NodeCornerRadius.Equals(other.NodeCornerRadius)
            && MinimapCornerRadius.Equals(other.MinimapCornerRadius)
            && MinimumThumbSize.Equals(other.MinimumThumbSize);
}
