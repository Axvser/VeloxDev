namespace VeloxDev.WorkflowSystem;

/// <summary>One materialized slot element a node view exposes, plus the platform's way to measure it.</summary>
/// <remarks>
/// The part is a handle onto a live visual: it is valid only until the next layout pass rebuilds the template.
/// The pipeline resolves a fresh part every time it measures.
/// </remarks>
public interface ISlotPart
{
    /// <summary>The slot model this element renders, or <see langword="null"/> when it is not bound to a slot.</summary>
    ISlotComponent? Slot { get; }

    /// <summary>Whether the element has a positive measured size and can carry a center this pass.</summary>
    bool IsMeasured { get; }

    /// <summary>Measures the element's center relative to <paramref name="relativeTo"/>.</summary>
    /// <param name="relativeTo">
    /// The coordinate host returned by <see cref="ISlotPartLocator.ResolveCoordinateHost"/>, or
    /// <see langword="null"/> to measure relative to the node view — the fallback used when the view has no
    /// coordinate host or the host cannot be reached.
    /// </param>
    /// <returns>
    /// The measured center, or <see langword="null"/> when the platform cannot transform the element into
    /// <paramref name="relativeTo"/>.
    /// </returns>
    SlotCenter? MeasureCenter(object? relativeTo);
}
