namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Implemented by objects that drive a <see cref="SlotEnumerator{TSlot}"/> with an
/// instance-based slot list instead of an enum or bool type.
/// <para>
/// Pass an instance of this type to <c>SlotEnumerator.SetSelector</c> and the enumerator
/// will call <see cref="GetSlots"/> to build its <c>Items</c> collection.
/// </para>
/// <para>
/// This interface is intentionally minimal so that JSON-serializable classes can implement it
/// without any special infrastructure — the Agent deserializes the concrete class from JSON and
/// passes the instance directly to <c>SetSelector</c>.
/// </para>
/// </summary>
public interface ISlotProvider
{
    /// <summary>
    /// Returns the ordered list of slot definitions for the enumerator.
    /// Each entry produces one <c>ConditionalSlot&lt;TSlot&gt;</c> in <c>Items</c>.
    /// </summary>
    IEnumerable<SlotDefinition> GetSlots();
}
