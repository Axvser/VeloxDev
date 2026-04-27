using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class CodeProvider : IWikiElement
{
    public IReadOnlyList<string> Languages { get; } =
    [
        "csharp",
        "xml",
        "json",
        "xaml",
        "markdown",
        "javascript",
        "typescript",
        "python",
        "java",
        "cpp",
        "c",
        "rust",
        "go",
        "sql",
        "yaml",
        "powershell",
        "shellscript",
        "html",
        "css"
    ];

    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Code { get; set; }
    [VeloxProperty] public partial string Language { get; set; }
    public InlineCollection Inlines { get; } = [];

    partial void OnCodeChanged(string oldValue, string newValue) => UpdateInlines();
    partial void OnLanguageChanged(string oldValue, string newValue) => UpdateInlines();

    private void UpdateInlines()
    {
        var code = Code;
        var language = Language;
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(language))
            return;

        var inlines = Inlines;
        if (inlines == null)
            return;

        try
        {
            var options = new RegistryOptions(ThemeName.DarkPlus);
            var registry = new Registry(options);
            var scopeName = options.GetScopeByLanguageId(language);
            if (scopeName == null)
            {
                inlines.Clear();
                inlines.Add(new Run(code));
                return;
            }

            var grammar = registry.LoadGrammar(scopeName);
            if (grammar == null)
            {
                inlines.Clear();
                inlines.Add(new Run(code));
                return;
            }

            inlines.Clear();

            var theme = registry.GetTheme();
            var lines = code.Split('\n');
            IStateStack? ruleStack = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);
                ruleStack = result.RuleStack;

                foreach (var token in result.Tokens)
                {
                    int startIndex = token.StartIndex;
                    int endIndex = Math.Min(token.EndIndex, line.Length);
                    if (startIndex >= endIndex)
                        continue;

                    var text = line[startIndex..endIndex];
                    var run = new Run(text);

                    var rules = theme.Match(token.Scopes);
                    foreach (var rule in rules)
                    {
                        if (rule.foreground > 0)
                        {
                            var hex = theme.GetColor(rule.foreground);
                            if (!string.IsNullOrEmpty(hex) && Color.TryParse(hex, out var color))
                            {
                                run.Foreground = new SolidColorBrush(color);
                            }
                            break;
                        }
                    }

                    inlines.Add(run);
                }

                if (i < lines.Length - 1)
                    inlines.Add(new LineBreak());
            }
        }
        catch
        {
            inlines.Clear();
            inlines.Add(new Run(code));
        }
    }

    [VeloxCommand]
    private async Task Copy(object? parameter)
    {
        IClipboard? clipboard = parameter switch
        {
            IClipboard c => c,
            Avalonia.Visual v => Avalonia.Controls.TopLevel.GetTopLevel(v)?.Clipboard,
            _ => null
        };
        if (clipboard != null)
            await clipboard.SetTextAsync(Code).ConfigureAwait(false);
    }
}
