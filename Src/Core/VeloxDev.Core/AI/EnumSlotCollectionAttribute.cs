namespace VeloxDev.AI;

/// <summary>
/// Marks a slot collection property as enum-driven: its items correspond 1:1 to
/// values of an enum type that the Agent sets at runtime via <c>SetEnumSlotCollection</c>.
/// <para>
/// Usage example:
/// <code>
/// [VeloxProperty]
/// [EnumSlotCollection]
/// public partial ObservableCollection&lt;SlotViewModel&gt; OutputSlots { get; set; }
/// </code>
/// The Agent discovers this attribute via <c>ListSlotProperties</c> and uses
/// <c>SetEnumSlotCollection</c> to populate/rebuild the collection with an enum type.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class EnumSlotCollectionAttribute : Attribute
{
}
