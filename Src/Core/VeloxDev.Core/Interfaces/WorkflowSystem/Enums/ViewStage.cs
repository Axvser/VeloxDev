namespace VeloxDev.WorkflowSystem;

/// <summary>The view-pipeline layer that reported a failure.</summary>
public enum ViewStage
{
    /// <summary>The scheduler refused a materialization batch.</summary>
    Schedule,

    /// <summary>The link layer could not render its links.</summary>
    Link,

    /// <summary>The factory could not create a view.</summary>
    Create,

    /// <summary>The container could not bind or show a view.</summary>
    Activate,

    /// <summary>The container could not attach a view to the host.</summary>
    Attach,

    /// <summary>The arranger could not apply geometry.</summary>
    Arrange,

    /// <summary>The layer policy could not apply a z-order.</summary>
    Layer,

    /// <summary>The measure host could not measure the view.</summary>
    Measure,

    /// <summary>The container could not release or hide a view.</summary>
    Deactivate,

    /// <summary>The container could not detach a view.</summary>
    Detach,

    /// <summary>The container could not destroy a view the pool refused to keep.</summary>
    Destroy,
}
