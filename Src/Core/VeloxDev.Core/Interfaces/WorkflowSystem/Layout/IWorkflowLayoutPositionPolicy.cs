namespace VeloxDev.WorkflowSystem;

/// <summary>The axis a position decision was taken on.</summary>
public enum WorkflowLayoutAxis
{
    /// <summary>The decision moves the node along the vertical axis.</summary>
    Vertical,

    /// <summary>The decision moves the node along the horizontal axis.</summary>
    Horizontal,
}

/// <summary>
/// One question a separation stage asks its policy: may this node leave its current position for the proposed
/// one?
/// </summary>
/// <remarks>
/// The stage supplies <see cref="HasHardViolation"/> from the current rectangles, not from the proposal. A
/// policy is therefore able to tell "the node is already breaking the minimum gap" apart from "the node is
/// merely not aligned with the graph".
/// </remarks>
public readonly struct WorkflowLayoutPositionDecision
{
    /// <summary>Creates a decision.</summary>
    /// <param name="nodeIndex">The node being considered.</param>
    /// <param name="currentX">Its current left edge.</param>
    /// <param name="currentY">Its current top edge.</param>
    /// <param name="targetX">The proposed left edge.</param>
    /// <param name="targetY">The proposed top edge.</param>
    /// <param name="isPinned">Whether the node is pinned.</param>
    /// <param name="hasHardViolation">Whether the node currently overlaps or under-spaces a neighbour.</param>
    /// <param name="tolerance">The configured position tolerance for this decision.</param>
    /// <param name="axis">The axis the proposal moves along.</param>
    public WorkflowLayoutPositionDecision(
        int nodeIndex, double currentX, double currentY, double targetX, double targetY,
        bool isPinned, bool hasHardViolation, double tolerance, WorkflowLayoutAxis axis)
    {
        NodeIndex = nodeIndex;
        CurrentX = currentX;
        CurrentY = currentY;
        TargetX = targetX;
        TargetY = targetY;
        IsPinned = isPinned;
        HasHardViolation = hasHardViolation;
        Tolerance = tolerance;
        Axis = axis;
    }

    /// <summary>The node being considered.</summary>
    public int NodeIndex { get; }

    /// <summary>The node's current left edge.</summary>
    public double CurrentX { get; }

    /// <summary>The node's current top edge.</summary>
    public double CurrentY { get; }

    /// <summary>The proposed left edge.</summary>
    public double TargetX { get; }

    /// <summary>The proposed top edge.</summary>
    public double TargetY { get; }

    /// <summary>Whether the node is pinned.</summary>
    public bool IsPinned { get; }

    /// <summary>Whether the node currently overlaps or under-spaces a neighbour.</summary>
    public bool HasHardViolation { get; }

    /// <summary>The configured position tolerance.</summary>
    public double Tolerance { get; }

    /// <summary>The axis the proposal moves along.</summary>
    public WorkflowLayoutAxis Axis { get; }

    /// <summary>The distance the proposal asks for along <see cref="Axis"/>.</summary>
    public double AxisDistance => Axis == WorkflowLayoutAxis.Vertical
        ? Math.Abs(TargetY - CurrentY)
        : Math.Abs(TargetX - CurrentX);
}

/// <summary>
/// Decides whether a separation stage may apply a proposed position to one node.
/// </summary>
/// <remarks>
/// The policy is injected into the stage, not hard-coded there, so a stricter layering solution can register
/// its own rule. A stage always fixes hard violations regardless of the answer; the policy governs soft
/// alignment only.
/// </remarks>
public interface IWorkflowLayoutPositionPolicy
{
    /// <summary>Returns whether the proposed position should be applied.</summary>
    /// <param name="decision">The node, its current position, the proposal and the violation state.</param>
    bool ShouldApply(WorkflowLayoutPositionDecision decision);
}
