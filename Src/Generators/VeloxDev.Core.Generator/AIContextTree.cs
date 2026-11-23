using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Generators.Base;
using VeloxDev.Generators.Writers;

namespace VeloxDev.Generators
{
    /// <summary>
    /// Emits the agent context tree: a fragment of literal data describing the assembly's agent surface, plus one
    /// accessor per type to act on it without reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This generator does not subscribe to <see cref="Analizer.Filters.Targets"/>. That pipeline admits only
    /// partial class declarations, while the tree's first-class entries are interfaces and enums; and it is driven
    /// by attribute triggers, while a workflow component is recognised by the interface it implements. The walk
    /// here is over the whole compilation instead, guarded so that a project with no agent surface gets nothing.
    /// </para>
    /// <para>
    /// The fragment must be generated in the assembly that declares the types, not in a consumer. Reflection over
    /// another assembly's types cannot see private members through a reference assembly — and a
    /// <c>[VeloxProperty]</c> field is private by construction.
    /// </para>
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public class AIContextTree : IIncrementalGenerator
    {
        private const string RootProperty = "build_property.VeloxAgentContextTreeRoot";
        private const string EnabledProperty = "build_property.VeloxAgentContextTree";

        private const string DefaultRoot = "Customer";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var settings = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(RootProperty, out var root);
                provider.GlobalOptions.TryGetValue(EnabledProperty, out var enabled);

                return (
                    Root: string.IsNullOrWhiteSpace(root) ? DefaultRoot : root!,
                    Enabled: !string.Equals(enabled, "false", System.StringComparison.OrdinalIgnoreCase));
            });

            // 资源文件是编译期输入，不是运行期内嵌资源：生成器读进来看，再把文字写进目录。
            // 只订阅约定后缀，工程里别的 AdditionalFiles（XAML、AnalyzerReleases…）不会触发重跑。
            var resources = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(
                    AIContextXml.FileSuffix,
                    System.StringComparison.OrdinalIgnoreCase))
                .Collect();

            context.RegisterSourceOutput(
                context.CompilationProvider.Combine(settings).Combine(resources),
                static (output, input) => Generate(
                    output,
                    input.Left.Left,
                    input.Left.Right.Root,
                    input.Left.Right.Enabled,
                    input.Right));
        }

        private static void Generate(
            SourceProductionContext context,
            Compilation compilation,
            string root,
            bool enabled,
            IEnumerable<AdditionalText> resources)
        {
            if (!enabled) return;

            // 没有引用 VeloxDev.AI 的工程不参与 Agent 面，连遍历都不做。
            if (!AIContextModelBuilder.Applies(compilation)) return;

            var xml = AIContextXml.Parse(resources, compilation, context.CancellationToken);

            foreach (var diagnostic in xml.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            // 诊断经列表返回：资源一条都没对上时 Build 返回 null，而那正是最该报出来的情况。
            var notices = new List<Diagnostic>();
            var assembly = AIContextModelBuilder.Build(compilation, root, xml, notices);

            foreach (var notice in notices)
            {
                context.ReportDiagnostic(notice);
            }

            if (assembly is null) return;

            var source = AIContextTreeWriter.Write(assembly, root, context.CancellationToken);

            context.AddSource(
                $"{Sanitize(assembly.AssemblyName)}_AIContextTree.g.cs",
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
