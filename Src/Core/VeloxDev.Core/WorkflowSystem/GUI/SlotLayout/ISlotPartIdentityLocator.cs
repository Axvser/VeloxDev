namespace VeloxDev.WorkflowSystem;

/// <summary>
/// An optional extension of <see cref="ISlotPartLocator"/> that reports the identity of the realized slot controls,
/// not only how many there are.
/// </summary>
/// <remarks>
/// The re-measure stamp already covers the node geometry, the measured view size, the slot count and the model slot
/// instances. A template or item source that rebuilds its containers for the same model slots changes none of those;
/// hashing the realized control instances makes such a rebuild visible without allocating. Adapters that cannot
/// name a stable control identity can omit this interface, and the pipeline falls back to the count alone.
/// </remarks>
public interface ISlotPartIdentityLocator
{
    /// <summary>Hashes the realized slot controls and counts them in one pass.</summary>
    /// <param name="slotNames">The configured single-slot part names.</param>
    /// <param name="enumeratorNames">The configured enumerator part names.</param>
    /// <param name="slotCount">The number of realized parts, with the same meaning as <see cref="ISlotPartLocator.CountSlots"/>.</param>
    /// <returns>
    /// A hash of the realized control elements. It must change when those elements are recreated, and must not
    /// depend on the model slot instances.
    /// </returns>
    int ComputeSlotIdentity(
        IReadOnlyList<string> slotNames,
        IReadOnlyList<string> enumeratorNames,
        out int slotCount);
}
