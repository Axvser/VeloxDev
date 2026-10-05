using Microsoft.CodeAnalysis;
using VeloxDev.Generators.Base;

namespace VeloxDev.Generators
{
    /// <summary>
    /// Reports when the project's <c>LangVersion</c> is below what the generated code is written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named <c>LanguageVersionCheck</c> rather than <c>LanguageVersion</c>: a type declared in this namespace
    /// shadows the BCL's <c>Microsoft.CodeAnalysis.CSharp.LanguageVersion</c> for every file under it — silently,
    /// and with an error that points at the enum's members rather than at this class.
    /// </para>
    /// <para>
    /// Its own generator rather than a call inside each of the others. There are nine of them, every writer among
    /// them emits modern syntax, and a tenth would otherwise have to remember to ask — so the check lives where it
    /// cannot be forgotten instead. This is the same shape as the package's <c>VeloxDev.Core.Generator.targets</c>,
    /// which warns about an old Roslyn rather than silently generating nothing: the requirement belongs to the
    /// package, not to any one generator.
    /// </para>
    /// <para>
    /// Unlike the old Roslyn check this one has to be a generator rather than MSBuild, because
    /// <c>LangVersion</c> in MSBuild may be <c>latest</c>, <c>preview</c> or <c>default</c>, and telling those
    /// apart from a low version by string comparison is guesswork. Roslyn already knows what they mean.
    /// </para>
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public class LanguageVersionCheck : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.CompilationProvider,
                static (ctx, compilation) => LanguageVersionGuard.Report(ctx, compilation, location: null));
        }
    }
}
