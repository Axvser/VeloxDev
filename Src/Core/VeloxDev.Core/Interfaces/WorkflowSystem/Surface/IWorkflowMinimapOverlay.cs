using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Data-exchange contract for the workflow minimap overlay. Derives from <see cref="IWorkflowGridDecorator"/>
/// (the minimap also needs the scroll/content offsets), plus the viewport size and the tree to render.
///
/// The surface behavior pushes all members before each render pass; the minimap draws node/link thumbnails
/// and a viewport indicator, and drag navigation resolves back into world scroll offsets.
///
/// Unified in Core so all 7 GUI adapters share one definition (previously 6 adapters carried an identical
/// flat copy and Jalium a derived copy under <c>VeloxDev.WorkflowSystem.AttachedBehaviors</c>; the derived
/// shape is the normalized form).
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流小地图覆盖层的数据交换契约：接收视口尺寸与树模型，用于缩略图渲染与拖拽导航")]
[AgentContext(AgentLanguages.English, "Workflow minimap-overlay data-exchange contract: receives viewport size + tree for thumbnail rendering and drag navigation")]
public interface IWorkflowMinimapOverlay : IWorkflowGridDecorator
{
    /// <summary>Viewport width in screen pixels, used to scale the viewport indicator and resolve drags.</summary>
    double ViewportWidth { get; set; }

    /// <summary>Viewport height in screen pixels.</summary>
    double ViewportHeight { get; set; }

    /// <summary>The workflow tree whose nodes/links the minimap renders.</summary>
    IWorkflowTreeViewModel? WorkflowTree { get; set; }

    /// <summary>Whether the minimap overlay should render.</summary>
    bool IsMinimapVisible { get; set; }
}
