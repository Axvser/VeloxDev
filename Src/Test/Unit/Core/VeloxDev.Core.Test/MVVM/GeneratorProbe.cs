using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

// 驱动源生成器的公用机械：内存编译 + 直接跑生成器。
// 走编译器是看不到被拒声明的 —— 它根本不会出现在产物里，只能这样拿到诊断。
internal static class GeneratorProbe
{
    internal static (IReadOnlyList<Diagnostic> Diagnostics, string Generated) Run(
        IIncrementalGenerator generator, string source, string assemblyName)
    {
        var result = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(Build(source, assemblyName))
            .GetRunResult();

        var generated = string.Join(
            "\n",
            result.Results.SelectMany(static r => r.GeneratedSources)
                          .Select(static s => s.SourceText.ToString()));

        return (result.Diagnostics, generated);
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
