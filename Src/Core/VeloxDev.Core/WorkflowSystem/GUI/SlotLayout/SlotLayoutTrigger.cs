namespace VeloxDev.WorkflowSystem;

/// <summary>The platform notification that asked the pipeline to re-measure.</summary>
/// <remarks>
/// The trigger selects the schedule, not whether the work is needed: <see cref="Layout"/> is filtered by the
/// <see cref="WorkflowLayoutStamp"/> and flushed inside the layout pass, while every other trigger is unconditional
/// and deferred to the render queue.
/// </remarks>
public enum SlotLayoutTrigger
{
    /// <summary>A layout pass finished; unchanged inputs are skipped before the expensive half runs.</summary>
    Layout,

    /// <summary>A watched model property changed; measured unconditionally, deferred to the render queue.</summary>
    Model,

    /// <summary>The attached configuration or data context changed; the view is invalidated and measured.</summary>
    Configuration,

    /// <summary>Zoom or scroll changed the platform transform; measured unconditionally, deferred.</summary>
    Viewport,

    /// <summary>The view loaded, became visible, or was attached; measured unconditionally, deferred.</summary>
    Attachment,
}
