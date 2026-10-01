using Microsoft.CodeAnalysis;

namespace VeloxDev.Generators
{
    /// <summary>
    /// The diagnostics the generators report.
    /// </summary>
    /// <remarks>
    /// They exist to move a failure out of the generated file and onto the author's own line. Without one, an
    /// unsupported <c>[VeloxCommand]</c> shape surfaced as <c>CS1503</c>/<c>CS0407</c> inside
    /// <c>*_Commands.g.cs</c>, which names a method group and a constructor the author never wrote.
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
            id: "VELOXCMD001",
            title: "Unsupported [VeloxCommand] signature",
            messageFormat: "'{0}' cannot be turned into a command: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
