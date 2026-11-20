using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Test.Infrastructure;

// 驱动源生成器的机械：内存编译 + 直接跑生成器。
// 走编译器看不到被拒的 XAML —— 它根本不进产物，只有 driver 拿得到诊断。
internal static class GeneratorProbe
{
    // 被注入的编译必须是一份完整的 BCL + 本运行时/适配器；测试进程自己的 TPA 正好提供 BCL 与 WinForms。
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    internal static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Preview);

    internal static CSharpCompilation Build(string source, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            assemblyName,
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult Run(
        IIncrementalGenerator generator,
        string source,
        string assemblyName,
        params AdditionalText[] additionalTexts)
    {
        var driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            additionalTexts,
            ParseOptions);

        return driver.RunGenerators(Build(source, assemblyName)).GetRunResult();
    }

    internal static (GeneratorDriverRunResult Run, Compilation Output) RunAndCompile(
        IEnumerable<IIncrementalGenerator> generators,
        string source,
        string assemblyName,
        params AdditionalText[] additionalTexts)
    {
        var driver = CSharpGeneratorDriver.Create(
            generators.Select(static generator => generator.AsSourceGenerator()).ToArray(),
            additionalTexts,
            ParseOptions);

        var updated = driver.RunGeneratorsAndUpdateCompilation(
            Build(source, assemblyName),
            out var output,
            out _);

        return (((CSharpGeneratorDriver)updated).GetRunResult(), output);
    }

    internal static string GeneratedText(this GeneratorDriverRunResult result)
        => string.Join(
            "\n",
            result.Results.SelectMany(static run => run.GeneratedSources).Select(static source => source.SourceText.ToString()));

    internal static string Describe(IEnumerable<Diagnostic> diagnostics)
    {
        var materialized = diagnostics.ToArray();
        return materialized.Length == 0
            ? "(no diagnostics)"
            : string.Join(" | ", materialized.Select(static diagnostic => diagnostic.ToString()));
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        // 生成代码直接引用这三个程序集；只给运行时不给它的依赖，符号解析会停在小程序集边界。
        paths.Add(typeof(VeloxDev.WinForms.Xaml.Controls.Control).Assembly.Location);
        paths.Add(typeof(VeloxDev.TransitionSystem.TransitionEffect).Assembly.Location);
        paths.Add(typeof(VeloxDev.Threading.ThreadRef).Assembly.Location);

        return [.. paths.Select(static path => MetadataReference.CreateFromFile(path))];
    }
}

/// <summary>An <see cref="AdditionalText"/> over a string, for the XAML generator's driver tests.</summary>
internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text, Encoding.UTF8);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
