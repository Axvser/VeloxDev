using Microsoft.CodeAnalysis;

namespace VeloxDev.Generators
{
    /// <summary>
    /// The diagnostics the generators report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They exist to move a failure out of the generated file and onto the author's own line. Without one, an
    /// unsupported <c>[VeloxCommand]</c> shape surfaced as <c>CS1503</c>/<c>CS0407</c> inside
    /// <c>*_Commands.g.cs</c>, which names a method group and a constructor the author never wrote.
    /// </para>
    /// <para>
    /// IDs are prefixed by module and then by kind: <c>VELOX_MVVM_CMD…</c> for commands, <c>VELOX_MVVM_PROP…</c>
    /// for properties. This deliberately exceeds two of the guidelines in "Choose diagnostic IDs" — the IDs run
    /// longer than 15 characters and carry an underscore — because the module and kind segments are what make a
    /// diagnostic identifiable at a glance. Renaming the earlier <c>VELOXCMD001</c> was a source-breaking change:
    /// any existing <c>#pragma warning disable VELOXCMD001</c> no longer suppresses it.
    /// </para>
    /// </remarks>
    public static class Diagnostics
    {
        private const string Category = "VeloxDev.MVVM";

        /// <summary>
        /// A <c>[VeloxCommand]</c> method whose shape cannot be turned into a command.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: the generated file would not have compiled anyway, so the only question is
        /// which message the author gets.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnsupportedCommandSignature = new(
            id: "VELOX_MVVM_CMD001",
            title: "Unsupported [VeloxCommand] signature",
            messageFormat: "'{0}' cannot be turned into a command: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxProperty]</c> declaration that cannot produce the property it asks for.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: every cause — a broken pair, an ambiguous field name, or a field that
        /// cannot back a writable property — means the generated member is not emitted at all. A warning would
        /// leave a silent gap where the author expected a property.
        /// </remarks>
        public static readonly DiagnosticDescriptor ConflictingPropertyDeclaration = new(
            id: "VELOX_MVVM_PROP001",
            title: "Conflicting [VeloxProperty] declaration",
            messageFormat: "'{0}' cannot be generated: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxProperty]</c> name the generator cannot derive a valid counterpart from.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the name itself is legal C#, it just yields nothing usable — for example a
        /// field named <c>_</c> or one starting with a digit. Skipping it keeps an uncompilable member out of the
        /// generated file.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnusablePropertyName = new(
            id: "VELOX_MVVM_PROP002",
            title: "Unusable [VeloxProperty] name",
            messageFormat: "'{0}' cannot be generated: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
