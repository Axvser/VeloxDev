using System;
using System.Collections.Generic;

namespace VeloxDev.Serialization;

/// <summary>
/// Marks a type as taking part in the archive format, and can name further types that must take part with it.
/// </summary>
/// <remarks>
/// <para>
/// A ViewModel joins the format on its own: it is a component, or it carries a
/// <see cref="VeloxDev.MVVM.VeloxPropertyAttribute"/> field. A plain data type — a document the framework writes
/// and reads, like an execution checkpoint — has neither, and this is how it says so.
/// </para>
/// <para>
/// Every type named in <see cref="AdditionalRoots"/> joins for the same reason and by the same route: the
/// generator treats it as a root, so a type nothing else reaches can be declared from the one place that knows
/// it is needed. A named type may name further types of its own, and naming the same type twice costs nothing —
/// the generator keeps one entry per type.
/// </para>
/// <para>
/// The named types must be declared in the same assembly. The generated readers and writers are emitted into the
/// assembly that declares the type, so a type from another assembly can only take part when it is a closed
/// generic the current assembly has seen. A name the generator cannot honour is reported as an error, never
/// dropped in silence.
/// </para>
/// <para>
/// This attribute answers only "is this type in". Which members reach the document is decided by the default
/// member rules and by <c>[Archive]</c> on the member.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ArchivableAttribute : Attribute
{
    /// <summary>Marks the type, optionally naming further types that must take part with it.</summary>
    /// <param name="additionalRoots">Further types that must take part in the archive format.</param>
    public ArchivableAttribute(params Type[] additionalRoots)
        => AdditionalRoots = additionalRoots ?? Type.EmptyTypes;

    /// <summary>The further types this declaration puts into the format.</summary>
    public IReadOnlyList<Type> AdditionalRoots { get; }
}
