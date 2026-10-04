using System;

namespace VeloxDev.Serialization;

/// <summary>
/// Changes how one member takes part in the archive format.
/// </summary>
/// <remarks>
/// <para>
/// Without it the default rules decide: a public property with a public setter is written under its own name, in
/// declaration order, and everything else is left out. <see cref="ArchiveOptions"/> names the exceptions one
/// member at a time — a computed property, a field with no property beside it, a member written under a
/// different name.
/// </para>
/// <para>
/// A member has to be reachable from generated code: <c>public</c>, or <c>internal</c> when the declaring type is
/// serialized by its own assembly. Marking one the generator cannot reach is reported rather than skipped in
/// silence — same rule as the callbacks, and for the same reason: dropping a member changes the document without
/// any other symptom.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class ArchiveAttribute : Attribute
{
    /// <summary>Changes how the member takes part in the archive format.</summary>
    /// <param name="options">What to change.</param>
    /// <param name="argument">
    /// The value <paramref name="options"/> needs, if any — a <see cref="string"/> for
    /// <see cref="ArchiveOptions.ReName"/>.
    /// </param>
    public ArchiveAttribute(ArchiveOptions options, object? argument = null)
    {
        Options = options;
        Argument = argument;
    }

    /// <summary>What to change about the member's part in the format.</summary>
    public ArchiveOptions Options { get; }

    /// <summary>The value <see cref="Options"/> needs, or <see langword="null"/> when none is needed.</summary>
    public object? Argument { get; }
}
