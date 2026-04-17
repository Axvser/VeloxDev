using System;

namespace VeloxDev.AI;

/// <summary>
/// Marks a <see cref="Type"/> property (or backing field) as the enum-type driver for
/// the specified <c>[EnumSlotCollection]</c>-annotated slot collection.
/// <para>
/// The Agent tools (<c>ListSlotProperties</c>, <c>SetEnumSlotCollection</c>) use this
/// attribute to discover which property holds the <see cref="Type"/> driving a given
/// enum slot collection, and which enum types are permitted.
/// </para>
/// <para>
/// The property is automatically rejected by <c>PatchNodeProperties</c> — the only
/// valid mutation path is the <c>SetEnumSlotCollection</c> tool.
/// </para>
/// <para>
/// Usage example:
/// <code>
/// [SlotsEnumType(nameof(OutputSlots), typeof(MyEnum), typeof(OtherEnum))]
/// public Type? EnumType { get; set; }
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SlotsEnumTypeAttribute : Attribute
{
    /// <summary>
    /// The name of the slot collection property this enum-type property drives.
    /// </summary>
    public string CollectionPropertyName { get; }

    /// <summary>
    /// The set of enum types that are allowed for this slot collection.
    /// When empty, any enum type is accepted.
    /// </summary>
    public Type[] AllowedEnumTypes { get; }

    public SlotsEnumTypeAttribute(string collectionPropertyName, params Type[] allowedEnumTypes)
    {
        CollectionPropertyName = collectionPropertyName;
        AllowedEnumTypes = allowedEnumTypes ?? Array.Empty<Type>();
    }
}
