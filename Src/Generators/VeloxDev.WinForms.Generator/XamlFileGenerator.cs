using Microsoft.CodeAnalysis;

namespace VeloxDev.WinForms.Generator;

/// <summary>Compiles every <c>.xaml</c> additional file into a private non-partial <c>InitializeComponent</c>.</summary>
/// <remarks>
/// The compiler is intentionally an interpreter over a small XAML subset, not a WPF markup loader. It reads the
/// tree with <c>XDocument</c>, resolves each element against the compilation, and writes ordinary C# construction
/// statements. No runtime XAML parser is involved.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class XamlFileGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => new XamlAdditionalFile(
                file.Path,
                file.GetText(cancellationToken)?.ToString()));

        var inputs = files.Combine(context.CompilationProvider);
        context.RegisterSourceOutput(
            inputs,
            static (production, pair) => new XamlCompiler(production, pair.Left.Path, pair.Left.Text, pair.Right).Run());
    }
}

/// <summary>One XAML additional file and its text.</summary>
internal sealed class XamlAdditionalFile
{
    public XamlAdditionalFile(string path, string? text)
    {
        Path = path;
        Text = text;
    }

    public string Path { get; }

    public string? Text { get; }
}
