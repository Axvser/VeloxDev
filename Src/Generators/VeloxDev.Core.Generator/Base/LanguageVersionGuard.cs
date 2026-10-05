using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    /// <summary>
    /// Tells the author when the project's <c>LangVersion</c> is below what the generated code is written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated code is C# the author never wrote, so a syntax error in it names a file they did not open and
    /// a construct they did not type. Raising this as a diagnostic on their own type, before any of it is emitted,
    /// is what turns "CS8652 in VeloxDev.Generators.Workflow.Foo.g.cs" into a one-line instruction.
    /// </para>
    /// <para>
    /// Only reported when a generator is actually about to emit something for this compilation — a project that
    /// never touches VeloxDev should not be told about its language version.
    /// </para>
    /// </remarks>
    internal static class LanguageVersionGuard
    {
        /// <summary>The version the generated code is written in.</summary>
        /// <remarks>
        /// Collection expressions are what the generator needs today; when that moves, this moves with it, and the
        /// message follows because it prints this value.
        /// </remarks>
        internal const LanguageVersion Required = LanguageVersion.CSharp12;

        /// <summary>Reports the diagnostic when <paramref name="compilation"/>'s language version is too low.</summary>
        /// <param name="context">Where to report.</param>
        /// <param name="compilation">The compilation the generated code would go into.</param>
        /// <param name="location">The author's own syntax, so the squiggle lands on their type.</param>
        internal static void Report(SourceProductionContext context, Compilation compilation, SyntaxNode? location)
        {
            if (compilation.SyntaxTrees.FirstOrDefault()?.Options is not CSharpParseOptions options)
            {
                return;
            }

            // `Default` / `Latest` / `Preview` are not low versions — map them to the concrete version they mean
            // first, or every project that set LangVersion=latest gets told to raise it.
            var effective = LanguageVersionFacts.MapSpecifiedToEffectiveVersion(options.LanguageVersion);
            if (effective >= LanguageVersionFacts.MapSpecifiedToEffectiveVersion(Required))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.LanguageVersionTooLow,
                location?.GetLocation() ?? Location.None,
                LanguageVersionFacts.ToDisplayString(Required),
                LanguageVersionFacts.ToDisplayString(effective)));
        }
    }
}
