using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Data-exchange contract for the workflow grid decorator: the surface behavior pushes the
/// ScrollViewer's scroll offsets and the canvas content offset into it before each render pass,
/// so the grid/ruler lines align with the panned/overscrolled content.
///
/// Unified in Core so all 7 GUI adapters share one definition (previously each adapter carried an
/// identical local copy under <c>VeloxDev.WorkflowSystem.AttachedBehaviors</c>).
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流画布网格装饰器的数据交换契约：接收滚动偏移与内容偏移，用于绘制网格与标尺")]
[AgentContext(AgentLanguages.English, "Workflow grid-decorator data-exchange contract: receives scroll and content offsets for grid + ruler painting")]
public interface IWorkflowGridDecorator
{
    /// <summary>Horizontal scroll offset of the surface ScrollViewer.</summary>
    double ScrollOffsetX { get; set; }

    /// <summary>Vertical scroll offset of the surface ScrollViewer.</summary>
    double ScrollOffsetY { get; set; }

    /// <summary>Horizontal content offset of the canvas (<c>CanvasLayout.ActualOffset.Horizontal</c>).</summary>
    double ContentOffsetX { get; set; }

    /// <summary>Vertical content offset of the canvas (<c>CanvasLayout.ActualOffset.Vertical</c>).</summary>
    double ContentOffsetY { get; set; }
}
