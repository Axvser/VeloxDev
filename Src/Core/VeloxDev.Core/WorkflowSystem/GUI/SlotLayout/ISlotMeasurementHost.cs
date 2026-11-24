namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform half of slot measurement that touches the model and the UI thread: reads and writes anchors, reports
/// the node view's measured size, requests a layout pass, and posts deferred work.
/// </summary>
public interface ISlotMeasurementHost
{
    /// <summary>Reads the node view's current measured size.</summary>
    /// <returns>The measured width and height in the platform's device-independent units.</returns>
    (double Width, double Height) ReadNodeSize();

    /// <summary>Reads a slot's current anchor.</summary>
    /// <param name="slot">The slot to read.</param>
    Anchor ReadAnchor(ISlotComponent slot);

    /// <summary>Writes a slot's anchor.</summary>
    /// <param name="slot">The slot to write.</param>
    /// <param name="anchor">The measured anchor in canvas-local space.</param>
    void WriteAnchor(ISlotComponent slot, Anchor anchor);

    /// <summary>Requests a measure/arrange pass for the node view after a configuration change.</summary>
    void InvalidateMeasure();

    /// <summary>Posts <paramref name="flush"/> to the host's UI thread at render priority.</summary>
    /// <param name="flush">The batch flush to run on the UI thread.</param>
    /// <returns>
    /// <see langword="true"/> when the platform accepted the work; <see langword="false"/> when it could not queue
    /// it, in which case the pipeline retries on the next request.
    /// </returns>
    bool Post(Action flush);
}
