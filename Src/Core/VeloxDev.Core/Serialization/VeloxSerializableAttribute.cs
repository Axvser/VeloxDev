using System;

namespace VeloxDev.Serialization;

/// <summary>
/// Marks a type as taking part in the archive format even though nothing else would put it there.
/// </summary>
/// <remarks>
/// <para>
/// A ViewModel joins the format on its own: it is a component, or it carries a
/// <see cref="VeloxDev.MVVM.VeloxPropertyAttribute"/> field. A plain data type — a document the framework writes
/// and reads, like an execution checkpoint — has neither, and this is how it says so.
/// </para>
/// <para>
/// What gets written is still decided the same way: the type's public read/write properties, in declaration
/// order. The attribute only answers "is this type in", never "which members".
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class VeloxSerializableAttribute : Attribute
{
}
