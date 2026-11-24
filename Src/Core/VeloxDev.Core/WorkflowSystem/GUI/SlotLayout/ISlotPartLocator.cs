namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform half of slot measurement: finds the slot elements a node view materialized and resolves the
/// coordinate host their centers are measured against.
/// </summary>
/// <remarks>
/// <see cref="SlotLayoutPipelineCore"/> owns which parts are measured, their order, and the anchor write-back; an
/// adapter supplies only the tree walk and the platform transform. The visual tree is read live, so every returned
/// part is a handle valid only until the next layout pass.
/// </remarks>
public interface ISlotPartLocator
{
    /// <summary>The space <see cref="ISlotPart.MeasureCenter"/> reports when a coordinate host is present.</summary>
    SlotCoordinateSpace CoordinateSpace { get; }

    /// <summary>Finds the element named for a single slot part.</summary>
    /// <param name="name">The control or part name declared by the template.</param>
    /// <returns>The part, or <see langword="null"/> when the template did not materialize it.</returns>
    ISlotPart? FindNamedSlot(string name);

    /// <summary>Finds the slot parts realized by a named enumerator part, in item order.</summary>
    /// <param name="name">The control or part name declared by the template.</param>
    /// <returns>
    /// The realized parts, or an empty list when the enumerator is missing, has no items, or has not generated its
    /// containers yet.
    /// </returns>
    IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name);

    /// <summary>Resolves the coordinate host slot centers are measured against.</summary>
    /// <param name="hostName">The named host declared by the template; may be <see langword="null"/> or whitespace.</param>
    /// <param name="hostType">
    /// The host element type to search for when <paramref name="hostName"/> does not resolve; may be
    /// <see langword="null"/> for the platform default.
    /// </param>
    /// <returns>The resolved host, or <see langword="null"/> when the node view is not attached to one.</returns>
    object? ResolveCoordinateHost(string? hostName, Type? hostType);

    /// <summary>Counts the slot parts the template currently exposes, for the re-measure stamp.</summary>
    /// <param name="slotNames">The configured single-slot part names.</param>
    /// <param name="enumeratorNames">The configured enumerator part names.</param>
    /// <returns>
    /// The number of materialized single-slot elements plus the realized items of every enumerator. A change means
    /// the template rebuilt itself, so the slots must be measured even when the node's outer size did not change.
    /// </returns>
    int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames);
}
