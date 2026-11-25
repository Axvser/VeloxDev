namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A cheap fingerprint of everything a slot-layout sync reads: the node's model geometry, the size its view was
/// measured at, how many slot controls the template materialized, and which slot model instances they carry.
/// </summary>
/// <remarks>
/// <para>
/// A layout pass is raised for the whole visual tree, so every node on a canvas hears about every pass its host
/// performs. Comparing this stamp first lets a sync whose inputs did not change be skipped before the expensive half
/// runs — finding the slot controls and translating their centers into canvas coordinates.
/// </para>
/// <para>
/// The stamp covers the geometry the component model owns, the size the view reports, and the identity of the slot
/// model instances. A selector that swaps in a new slot set with the same count therefore still changes the stamp.
/// It cannot see a reflow that moves slot controls without changing any of them (for example a fixed-size card whose
/// inner text reflows). That case still needs a forced request, which is what the non-layout triggers issue.
/// </para>
/// </remarks>
public readonly struct WorkflowLayoutStamp : IEquatable<WorkflowLayoutStamp>
{
    private readonly double anchorX;
    private readonly double anchorY;
    private readonly int layer;
    private readonly double sizeWidth;
    private readonly double sizeHeight;
    private readonly double measuredWidth;
    private readonly double measuredHeight;
    private readonly int slotCount;
    private readonly int slotSignature;

    private WorkflowLayoutStamp(
        double anchorX, double anchorY, int layer,
        double sizeWidth, double sizeHeight,
        double measuredWidth, double measuredHeight,
        int slotCount, int slotSignature)
    {
        this.anchorX = anchorX;
        this.anchorY = anchorY;
        this.layer = layer;
        this.sizeWidth = sizeWidth;
        this.sizeHeight = sizeHeight;
        this.measuredWidth = measuredWidth;
        this.measuredHeight = measuredHeight;
        this.slotCount = slotCount;
        this.slotSignature = slotSignature;
    }

    /// <summary>
    /// Captures the current inputs of <paramref name="node"/>'s slot layout.
    /// </summary>
    /// <param name="node">The node whose slots are being measured.</param>
    /// <param name="measuredWidth">The width the node's view currently reports.</param>
    /// <param name="measuredHeight">The height the node's view currently reports.</param>
    /// <param name="slotCount">The number of slot controls the template currently exposes.</param>
    /// <param name="slotSignature">
    /// A hash of the node's slot model instances. It changes when a selector swaps in a new slot set whose count is
    /// the same, which the count alone cannot see.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public static WorkflowLayoutStamp Capture(
        INodeComponent node,
        double measuredWidth,
        double measuredHeight,
        int slotCount,
        int slotSignature = 0)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var anchor = node.Anchor;
        var size = node.Size;

        return new WorkflowLayoutStamp(
            anchor.Horizontal, anchor.Vertical, anchor.Layer,
            size.Width, size.Height,
            measuredWidth, measuredHeight,
            slotCount, slotSignature);
    }

    /// <inheritdoc />
    public bool Equals(WorkflowLayoutStamp other)
        => anchorX == other.anchorX
           && anchorY == other.anchorY
           && layer == other.layer
           && sizeWidth == other.sizeWidth
           && sizeHeight == other.sizeHeight
           && measuredWidth == other.measuredWidth
           && measuredHeight == other.measuredHeight
           && slotCount == other.slotCount
           && slotSignature == other.slotSignature;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is WorkflowLayoutStamp other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            HashCode.Combine(anchorX, anchorY, layer, sizeWidth, sizeHeight, measuredWidth, measuredHeight, slotCount),
            slotSignature);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> describe the same layout inputs.</summary>
    public static bool operator ==(WorkflowLayoutStamp left, WorkflowLayoutStamp right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(WorkflowLayoutStamp left, WorkflowLayoutStamp right) => !left.Equals(right);
}
