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
/// Two constructors are provided:
/// <list type="bullet">
///   <item><c>params Type[]</c> — compile-time safe, used when enum types are known at build time.</item>
///   <item><c>params string[]</c> — serialization-friendly, stores fully-qualified type names for
///   scenarios where <see cref="Type"/> references are unavailable (e.g. cross-assembly serialization,
///   JSON config, or dynamically loaded plugins).</item>
/// </list>
/// </para>
/// <para>
/// Usage examples:
/// <code>
/// // Type-based (compile-time):
/// [SlotsEnumType(nameof(OutputSlots), typeof(MyEnum), typeof(OtherEnum))]
/// public Type? EnumType { get; set; }
///
/// // String-based (serialization-friendly):
/// [SlotsEnumType(nameof(OutputSlots), "MyNamespace.MyEnum", "MyNamespace.OtherEnum")]
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
    /// When empty and <see cref="AllowedEnumTypeNames"/> is also empty, any enum type is accepted.
    /// Populated by the <c>params Type[]</c> constructor.
    /// </summary>
    public Type[] AllowedEnumTypes { get; }

    /// <summary>
    /// Fully-qualified type names of the allowed enum types, suitable for serialization.
    /// When empty and <see cref="AllowedEnumTypes"/> is also empty, any enum type is accepted.
    /// Populated automatically by both constructors.
    /// </summary>
    public string[] AllowedEnumTypeNames { get; }

    /// <summary>
    /// Initializes with compile-time <see cref="Type"/> references.
    /// <see cref="AllowedEnumTypeNames"/> is automatically derived from <paramref name="allowedEnumTypes"/>.
    /// </summary>
    public SlotsEnumTypeAttribute(string collectionPropertyName, params Type[] allowedEnumTypes)
    {
        CollectionPropertyName = collectionPropertyName;
        AllowedEnumTypes = allowedEnumTypes ?? Array.Empty<Type>();
        AllowedEnumTypeNames = new string[AllowedEnumTypes.Length];
        for (int i = 0; i < AllowedEnumTypes.Length; i++)
            AllowedEnumTypeNames[i] = AllowedEnumTypes[i].FullName;
    }

    /// <summary>
    /// Initializes with fully-qualified type name strings for serialization scenarios.
    /// <see cref="AllowedEnumTypes"/> remains empty; runtime resolution is deferred to the
    /// Agent toolkit (<c>SetEnumSlotCollection</c>) which resolves names via <c>TypeIntrospector</c>.
    /// </summary>
    public SlotsEnumTypeAttribute(string collectionPropertyName, params string[] allowedEnumTypeNames)
    {
        CollectionPropertyName = collectionPropertyName;
        AllowedEnumTypes = Array.Empty<Type>();
        AllowedEnumTypeNames = allowedEnumTypeNames ?? Array.Empty<string>();
    }
}
