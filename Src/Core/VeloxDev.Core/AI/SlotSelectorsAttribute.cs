namespace VeloxDev.AI;

/// <summary>
/// Declares the Agent-valid selector types for a <see cref="VeloxDev.WorkflowSystem.SlotEnumerator{TSlot}"/> property.
/// <para>
/// Place this attribute directly on the <c>SlotEnumerator&lt;TSlot&gt;</c> property.
/// The Agent tools (<c>ListSlotProperties</c>, <c>SetEnumSlotCollection</c>) read it to
/// discover which selector types are permitted.
/// </para>
/// <para>
/// The property is automatically rejected by <c>PatchNodeProperties</c> — the only
/// valid mutation path is the <c>SetEnumSlotCollection</c> tool (or <c>CreateAndConfigureNode</c>
/// with <c>enumSlotProperty</c> + <c>enumTypeName</c>).
/// </para>
/// <para>
/// Two constructors are provided:
/// <list type="bullet">
///   <item><c>params Type[]</c> — compile-time safe, used when selector types are known at build time.</item>
///   <item><c>params string[]</c> — serialization-friendly, stores fully-qualified type names for
///   scenarios where <see cref="Type"/> references are unavailable (e.g. cross-assembly serialization,
///   JSON config, or dynamically loaded plugins).</item>
/// </list>
/// </para>
/// <para>
/// Usage examples:
/// <code>
/// // Type-based (compile-time):
/// [SlotSelectors(typeof(MyEnum), typeof(OtherEnum))]
/// public partial SlotEnumerator&lt;SlotViewModel&gt; OutputSlots { get; set; }
///
/// // String-based (serialization-friendly):
/// [SlotSelectors("MyNamespace.MyEnum", "MyNamespace.OtherEnum")]
/// public partial SlotEnumerator&lt;SlotViewModel&gt; OutputSlots { get; set; }
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SlotSelectorsAttribute : Attribute
{
    /// <summary>
    /// The set of selector types (enum or bool) that are allowed for this <c>SlotEnumerator</c>.
    /// When empty and <see cref="AllowedEnumTypeNames"/> is also empty, any type is accepted.
    /// Populated by the <c>params Type[]</c> constructor.
    /// </summary>
    public Type[] AllowedEnumTypes { get; }

    /// <summary>
    /// Fully-qualified type names of the allowed selector types, suitable for serialization.
    /// When empty and <see cref="AllowedEnumTypes"/> is also empty, any type is accepted.
    /// Populated automatically by both constructors.
    /// </summary>
    public string[] AllowedEnumTypeNames { get; }

    /// <summary>
    /// Initializes with compile-time <see cref="Type"/> references.
    /// <see cref="AllowedEnumTypeNames"/> is automatically derived from <paramref name="allowedEnumTypes"/>.
    /// </summary>
    public SlotSelectorsAttribute(params Type[] allowedEnumTypes)
    {
        AllowedEnumTypes = allowedEnumTypes ?? [];
        AllowedEnumTypeNames = new string[AllowedEnumTypes.Length];
        for (int i = 0; i < AllowedEnumTypes.Length; i++)
        {
            AllowedEnumTypeNames[i] = AllowedEnumTypes[i]?.FullName ?? AllowedEnumTypes[i]?.Name ?? string.Empty;
        }
    }

    /// <summary>
    /// Initializes with fully-qualified type name strings for serialization scenarios.
    /// <see cref="AllowedEnumTypes"/> remains empty; runtime resolution is deferred to the
    /// Agent toolkit (<c>SetEnumSlotCollection</c>) which resolves names via <c>TypeIntrospector</c>.
    /// </summary>
    public SlotSelectorsAttribute(params string[] allowedEnumTypeNames)
    {
        AllowedEnumTypes = [];
        AllowedEnumTypeNames = allowedEnumTypeNames ?? [];
    }
}
