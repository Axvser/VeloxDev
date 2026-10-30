namespace VeloxDev.WorkflowSystem;

/// <summary>The default position policy: fix hard violations, then respect the original position.</summary>
/// <remarks>
/// <para>A pinned node never moves. A node that currently overlaps or under-spaces a neighbour always moves,
/// whatever the tolerance. Otherwise the proposal is applied only when it is farther away than the
/// tolerance.</para>
/// <para><c>0</c> makes <see cref="TileSeparationStage"/> repair hard violations only, so a compliant node is
/// not pushed by a separation chain. Canonical placement is decided by
/// <see cref="HierarchyAlignmentStage"/>, which writes its target directly and does not consult this
/// policy.</para>
/// </remarks>
public sealed class TolerancePositionPolicy : IWorkflowLayoutPositionPolicy
{
    /// <inheritdoc />
    public bool ShouldApply(WorkflowLayoutPositionDecision decision)
    {
        if (decision.IsPinned) return false;
        if (decision.HasHardViolation) return true;

        // 0 只修硬违规：合规节点不因分离链的推动而移动。
        if (decision.Tolerance <= 0d) return false;

        return decision.AxisDistance > decision.Tolerance;
    }
}
