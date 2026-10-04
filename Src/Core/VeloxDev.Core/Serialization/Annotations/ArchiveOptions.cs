using System;

namespace VeloxDev.Serialization;

/// <summary>
/// What <see cref="ArchiveAttribute"/> changes about one member's part in the archive format.
/// </summary>
/// <remarks>
/// The default rules stand unless one of these is named: a public property with a public setter is written under
/// its own name, in declaration order. Nothing here widens the closed world — a type still has to be archivable
/// before any of its members are considered.
/// </remarks>
[Flags]
public enum ArchiveOptions
{
    /// <summary>No change: the member follows the default rules.</summary>
    None = 0,

    /// <summary>
    /// Includes a computed property — one with no public setter — which the default rules drop.
    /// </summary>
    /// <remarks>
    /// Such a member is written but never read back: there is no setter to assign it through, so the reader steps
    /// over it. A round trip therefore loses it. That is the honest outcome — the value would have to come from
    /// somewhere, and nothing in the document says where.
    /// </remarks>
    KeepProperty = 1,

    /// <summary>
    /// Includes a field that no property corresponds to, which the default rules drop.
    /// </summary>
    KeepField = 2,

    /// <summary>
    /// Leaves the member out of the document entirely, even where the default rules would take it.
    /// </summary>
    /// <remarks>
    /// This is the exclusion the format otherwise has no way to express: a <c>[VeloxProperty]</c> field is
    /// serialized because it is one, and a public property is serialized because it is public. Marking a field
    /// this way drops the member it would have produced.
    /// </remarks>
    IgnoreField = 4,

    /// <summary>
    /// Writes the member under the name in <see cref="ArchiveAttribute.Argument"/> rather than under its own.
    /// </summary>
    ReName = 8,
}
