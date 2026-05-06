using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Media;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using Newtonsoft.Json;
using VeloxDev.MVVM;
using VeloxDev.Docs.Translation;

namespace VeloxDev.Docs.ViewModels;

public partial class CodeProvider : IWikiElement
{
    // TextMate setup is expensive: building RegistryOptions scans embedded
    // theme/grammar manifests, and LoadGrammar parses a sizeable JSON document.
    // Cache them once per process and per language, so reloading documents with
    // many code blocks does not pay this cost N times.
    private static readonly RegistryOptions _registryOptions = new(ThemeName.DarkPlus);
    private static readonly Registry _registry = new(_registryOptions);
    private static readonly object _registryGate = new();
    private static readonly ConcurrentDictionary<string, IGrammar?> _grammarCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _highlightPending;

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

    [VeloxProperty] private IWikiElement? parent = null;
    [TranslateTarget("Source code block. Translate ONLY inline comments (e.g. // ... or /* ... */ or # ...) into the target language. Do NOT translate identifiers, keywords, strings, or any non-comment tokens. Return the full code with only the comment text changed.")]
    [VeloxProperty] public partial string Code { get; set; }
    [VeloxProperty] public partial string Language { get; set; }
    [VeloxProperty] public partial bool AutoHeight { get; set; }
    [VeloxProperty] public partial double MaxHeightValue { get; set; }
    [JsonIgnore] public InlineCollection Inlines { get; } = [];

    public bool HasFixedHeight => !AutoHeight;

    partial void OnCodeChanged(string oldValue, string newValue) => RequestUpdateInlines();
    partial void OnLanguageChanged(string oldValue, string newValue) => RequestUpdateInlines();
    partial void OnAutoHeightChanged(bool oldValue, bool newValue) => OnPropertyChanged(nameof(HasFixedHeight));

    private void RequestUpdateInlines()
    {
        // During JSON hydration both Code and Language are assigned in sequence.
        // Skip the work and let the document trigger a single refresh once
        // hydration completes.
        if (HydrationScope.IsActive)
        {
            _highlightPending = true;
            return;
        }

        UpdateInlines();
    }

    /// <summary>
    /// Re-runs syntax highlighting if a previous update was deferred during
    /// hydration. Safe to call repeatedly; it is a no-op when nothing is pending.
    /// </summary>
    public void EnsureHighlighted()
    {
        if (!_highlightPending)
            return;

        _highlightPending = false;
        UpdateInlines();
    }

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
            var scopeName = _registryOptions.GetScopeByLanguageId(language);
            if (scopeName == null)
            {
                inlines.Clear();
                inlines.Add(new Run(code));
                return;
            }

            var grammar = _grammarCache.GetOrAdd(scopeName, static name =>
            {
                lock (_registryGate)
                {
                    return _registry.LoadGrammar(name);
                }
            });
            if (grammar == null)
            {
                inlines.Clear();
                inlines.Add(new Run(code));
                return;
            }

            inlines.Clear();

            var theme = _registry.GetTheme();
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
    } // Added missing closing brace for Copy method

    public CodeProvider()
    {
        Code = string.Empty;
        Language = "markdown";
        AutoHeight = false;
        MaxHeightValue = 300;
    }
}
