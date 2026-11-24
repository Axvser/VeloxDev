namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that reports pointer state and owns pointer capture. The pipeline uses
/// <see cref="PointerPosition"/> and <see cref="ClampRect"/> for hit-testing and drag navigation;
/// <see cref="Capture"/> / <see cref="Release"/> keep the drag alive outside the minimap rectangle.
/// </summary>
public interface IMinimapInputHost
{
    /// <summary>The pointer position in overlay-local coordinates.</summary>
    (double X, double Y) PointerPosition { get; }

    /// <summary>Whether the primary pointer button is currently held.</summary>
    bool IsPressed { get; }

    /// <summary>Whether the host currently captures the pointer.</summary>
    bool IsCaptured { get; }

    /// <summary>
    /// The overlay-local rectangle that receives minimap interaction, or an empty rectangle to use the
    /// pipeline's own placed minimap rectangle.
    /// </summary>
    WorkflowBounds ClampRect { get; }

    /// <summary>Captures the pointer for a drag.</summary>
    /// <returns><see langword="true"/> when the platform captured the pointer.</returns>
    bool Capture();

    /// <summary>Releases a pointer captured by <see cref="Capture"/>.</summary>
    void Release();
}
