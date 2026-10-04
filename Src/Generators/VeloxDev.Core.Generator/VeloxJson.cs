using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
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

            var notices = new List<Diagnostic>();
            var assembly = VeloxJsonModelBuilder.Build(compilation, notices);

            // 先报诊断再判空：一个被拒的声明往往正是「这个程序集什么也没产出」的原因，
            // 而那样的情况恰恰是最需要出声的时候。
            foreach (var notice in notices)
            {
                context.ReportDiagnostic(notice);
            }

            if (assembly is null) return;

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
