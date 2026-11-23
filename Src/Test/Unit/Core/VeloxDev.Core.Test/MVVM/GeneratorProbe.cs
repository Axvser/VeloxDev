using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

// 驱动源生成器的公用机械：内存编译 + 直接跑生成器。
// 走编译器是看不到被拒声明的 —— 它根本不会出现在产物里，只能这样拿到诊断。
internal static class GeneratorProbe
{
    internal static (IReadOnlyList<Diagnostic> Diagnostics, string Generated) Run(
        IIncrementalGenerator generator,
        string source,
        string assemblyName,
        IEnumerable<(string Path, string Text)>? additionalFiles = null)
    {
        var result = CSharpGeneratorDriver.Create(
                [generator.AsSourceGenerator()],
                additionalFiles?.Select(static file =>
                    (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text)))
            .RunGenerators(Build(source, assemblyName))
            .GetRunResult();

        var generated = string.Join(
            "\n",
            result.Results.SelectMany(static r => r.GeneratedSources)
                          .Select(static s => s.SourceText.ToString()));

        return (result.Diagnostics, generated);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);

        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    internal static CSharpCompilation Build(string source, string assemblyName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(VeloxCommand).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static string Describe(IReadOnlyList<Diagnostic> diagnostics) =>
        diagnostics.Count == 0
            ? "(no diagnostics)"
            : string.Join(" | ", diagnostics.Select(static d => d.ToString()));
}
