using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using VeloxDev.Generators.Base;
using VeloxDev.Generators.Writers;

namespace VeloxDev.Generators
{
    /// <summary>
    /// Emits the archive serializer: one writer per type a document can contain, and the registration that puts
    /// them in <c>VeloxDev.Serialization.VeloxJsonRegistry</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This generator walks the whole compilation rather than subscribing to <see cref="Analizer.Filters.Targets"/>.
    /// That pipeline admits only partial class declarations that carry a trigger attribute, while a type takes
    /// part in the archive format because it is a workflow component or because a component reaches it — and the
    /// set of those has to be closed transitively before anything can be emitted.
    /// </para>
    /// <para>
    /// The serializer must be generated in the assembly that declares the types: a member's declared type is what
    /// decides whether a value is written with its type name and whether it is a collection, and that is a
    /// compile-time fact about this compilation alone.
    /// </para>
    /// <para>
    /// Output is inert where there is no workflow surface, and <c>VeloxJsonSerialization=false</c> turns it off
    /// outright.
    /// </para>
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public class VeloxJson : IIncrementalGenerator
    {
        private const string EnabledProperty = "build_property.VeloxJsonSerialization";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var enabled = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(EnabledProperty, out var value);
                return !string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase);
            });

            context.RegisterSourceOutput(
                context.CompilationProvider.Combine(enabled),
                static (output, input) => Generate(output, input.Left, input.Right));
        }

        private static void Generate(SourceProductionContext context, Compilation compilation, bool enabled)
        {
            if (!enabled) return;

            // 没有引用 VeloxDev.MVVM 的工程没有 ViewModel 可写，连遍历都不做。
            if (!VeloxJsonModelBuilder.Applies(compilation)) return;

            var assembly = VeloxJsonModelBuilder.Build(compilation);
            if (assembly is null) return;

            foreach (var notice in assembly.Notices)
            {
                context.ReportDiagnostic(notice);
            }

            var source = VeloxJsonCodeWriter.Write(assembly, Sanitize(assembly.AssemblyName));

            context.AddSource(
                $"{Sanitize(assembly.AssemblyName)}_VeloxJson.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
