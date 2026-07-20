using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenAI.Chat;
using VeloxDev.Docs.Models;

namespace VeloxDev.Docs.ViewModels;

public sealed record LanguageOption(string Code, string DisplayName);

public partial class DocumentViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public const string DefaultLanguage = "en";

    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        // ── Top-5 by global speaker count / web usage (also shown in the left ComboBox) ──
        new("en", "🌐 English"),
        new("zh", "🌐 中文"),
        new("es", "🌐 Español"),
        new("fr", "🌐 Français"),
        new("de", "🌐 Deutsch"),
        // ── Additional mainstream languages ──────────────────────────────────
        new("ar", "🌐 العربية"),
        new("pt", "🌐 Português"),
        new("ru", "🌐 Русский"),
        new("ja", "🌐 日本語"),
        new("ko", "🌐 한국어"),
        new("it", "🌐 Italiano"),
        new("nl", "🌐 Nederlands"),
        new("pl", "🌐 Polski"),
        new("tr", "🌐 Türkçe"),
        new("vi", "🌐 Tiếng Việt"),
        new("id", "🌐 Bahasa Indonesia"),
        new("th", "🌐 ภาษาไทย"),
        new("hi", "🌐 हिन्दी"),
        new("sv", "🌐 Svenska"),
        new("cs", "🌐 Čeština"),
        new("ro", "🌐 Română"),
        new("hu", "🌐 Magyar"),
        new("uk", "🌐 Українська"),
        new("da", "🌐 Dansk"),
        new("fi", "🌐 Suomi"),
        new("no", "🌐 Norsk"),
        new("el", "🌐 Ελληνικά"),
        new("he", "🌐 עברית"),
        new("fa", "🌐 فارسی"),
    ];

    /// <summary>Top 2 languages shown in the document-language selector on the left toolbar.</summary>
    public static IReadOnlyList<LanguageOption> TopLanguages { get; } =
        [.. AvailableLanguages.Take(2)];

    /// <summary>Full language list for the translation target selector.</summary>
    public IReadOnlyList<LanguageOption> AllLanguages => AvailableLanguages;

    public bool CanTranslate => !IsTranslating;

    private bool _markdownViewReady;
    private bool _isTranslating;
    private CancellationTokenSource? _translationCts;

    [ObservableProperty]
    private ObservableCollection<PageNode> _nodes = [];

    [ObservableProperty]
    private PageNode? _selectedNode;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _language = DefaultLanguage;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = AvailableLanguages[0];

    [ObservableProperty]
    private string _title = "VeloxDev Docs";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private LanguageOption _translationTargetLanguage = AvailableLanguages.First(l => l.Code == "en");

    [ObservableProperty]
    private string _translationStatus = string.Empty;

    [ObservableProperty]
    private double _translationProgress;

    public bool IsTranslating
    {
        get => _isTranslating;
        set
        {
            if (SetProperty(ref _isTranslating, value))
                OnPropertyChanged(nameof(CanTranslate));
        }
    }

    public DocumentViewModel()
    {
        _ = LoadTreeAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!string.Equals(Language, value.Code, StringComparison.OrdinalIgnoreCase))
        {
            Language = value.Code;
            _ = ReloadAsync();
        }
    }

    partial void OnSelectedNodeChanged(PageNode? value)
    {
        if (value is not null)
            _ = LoadContentAsync(value);
    }

    /// <summary>
    public void MarkdownViewReady()
    {
        _markdownViewReady = true;
        if (SelectedNode is not null)
            _ = LoadContentAsync(SelectedNode);
    }

    /// <summary>Called by the view to render the current content when ready.</summary>
    public Func<string, Task>? RenderMarkdownAsync { get; set; }

    private async Task LoadTreeAsync()
    {
        IsLoading = true;
        try
        {
            var code = string.IsNullOrWhiteSpace(Language) ? DefaultLanguage : Language.ToLowerInvariant();
            var uri = new Uri($"avares://VeloxDev.Docs/Assets/Docs/{code}/tree.json");

            string json;
            try
            {
                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                json = await reader.ReadToEndAsync();
            }
            catch (FileNotFoundException)
            {
                // Fall back to default language
                var fallback = new Uri($"avares://VeloxDev.Docs/Assets/Docs/{DefaultLanguage}/tree.json");
                using var stream = AssetLoader.Open(fallback);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                json = await reader.ReadToEndAsync();
            }

            var tree = JsonSerializer.Deserialize<TreeRoot>(json, JsonOptions);
            Nodes = BuildTree(tree?.Pages ?? []);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load tree: {ex.Message}");
            Nodes = [];
        }
        finally
        {
            IsLoading = false;
        }

        // Auto-select first node
        if (Nodes.Count > 0)
            SelectedNode = Nodes[0];
    }

    private async Task ReloadAsync()
    {
        _markdownViewReady = false;
        Content = string.Empty;
        SelectedNode = null;
        await LoadTreeAsync();
        // Restore ready state: the MarkdownView WebView was not destroyed,
        // only the tree/content was reloaded.
        _markdownViewReady = true;
        // LoadTreeAsync auto-selected the first node, but OnSelectedNodeChanged
        // fired while _markdownViewReady was still false and returned early.
        // Re-trigger content loading for the now-selected node.
        if (SelectedNode is not null)
            await LoadContentAsync(SelectedNode);
    }

    private async Task LoadContentAsync(PageNode node)
    {
        if (!_markdownViewReady || RenderMarkdownAsync is null)
            return;

        IsLoading = true;
        try
        {
            var code = string.IsNullOrWhiteSpace(Language) ? DefaultLanguage : Language.ToLowerInvariant();
            var mdPath = $"{node.Path}/index.md";
            var uri = new Uri($"avares://VeloxDev.Docs/Assets/Docs/{code}/{mdPath.Replace('\\', '/')}");

            string markdown;
            try
            {
                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                markdown = await reader.ReadToEndAsync();
            }
            catch (FileNotFoundException)
            {
                markdown = $"# {node.Title}\n\n*Content not available in this language.*";
            }

            Content = markdown;
            await RenderMarkdownAsync(markdown);
        }
        catch (Exception ex)
        {
            Content = $"# Error\n\nFailed to load content: {ex.Message}";
            await RenderMarkdownAsync(Content);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static ObservableCollection<PageNode> BuildTree(List<TreePage>? pages)
    {
        var result = new ObservableCollection<PageNode>();
        if (pages is null) return result;

        foreach (var page in pages)
        {
            var node = new PageNode
            {
                Title = page.Title,
                Path = page.Path,
                Children = BuildTree(page.Children)
            };
            result.Add(node);
        }
        return result;
    }

    /// <summary>Triggered by the UI to notify the ViewModel that property changes should be re-evaluated.</summary>
    public void NotifyTranslationSupportedChanged()
    {
        OnPropertyChanged(nameof(CanTranslate));
    }

    // ── Translation commands ────────────────────────────────────────────

    [RelayCommand]
    private async Task TranslateCurrentPageAsync()
    {
        if (SelectedNode is null || TranslationTargetLanguage is null)
            return;

        var target = TranslationTargetLanguage.Code;
        if (string.Equals(Language, target, StringComparison.OrdinalIgnoreCase))
        {
            TranslationStatus = "Target language matches current language.";
            return;
        }

        IsTranslating = true;
        TranslationProgress = 0;
        TranslationStatus = "Translating...";
        _translationCts = new CancellationTokenSource();

        try
        {
            // Read the current markdown content from the asset file
            var code = Language.ToLowerInvariant();
            var mdPath = $"{SelectedNode.Path}/index.md";
            var uri = new Uri($"avares://VeloxDev.Docs/Assets/Docs/{code}/{mdPath.Replace('\\', '/')}");

            string sourceMarkdown;
            try
            {
                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                sourceMarkdown = await reader.ReadToEndAsync();
            }
            catch (FileNotFoundException)
            {
                TranslationStatus = "Source content not found.";
                return;
            }

            TranslationProgress = 0.3;

            // Attempt translation via OpenAI-like provider
            var translated = await TranslateWithLLMAsync(sourceMarkdown, code, target, _translationCts.Token);

            TranslationProgress = 0.8;

            if (translated is not null)
            {
                // Display translated content in the MarkdownView
                Content = translated;
                if (RenderMarkdownAsync is not null)
                    await RenderMarkdownAsync(translated);

                TranslationStatus = $"Translated to {TranslationTargetLanguage.DisplayName} ✓";
            }
            else
            {
                TranslationStatus = "Translation unavailable (no API key configured).";
            }

            TranslationProgress = 1;
        }
        catch (OperationCanceledException)
        {
            TranslationStatus = "Translation cancelled.";
        }
        catch (Exception ex)
        {
            TranslationStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translationCts?.Dispose();
            _translationCts = null;
        }
    }

    [RelayCommand]
    private void CancelTranslation()
    {
        _translationCts?.Cancel();
    }

    /// <summary>
    /// Translates markdown content using an LLM (OpenAI-compatible).
    /// Returns the translated markdown, or null if no API key is configured.
    /// </summary>
    private static async Task<string?> TranslateWithLLMAsync(
        string markdown, string sourceLang, string targetLang, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY")
                  ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        try
        {
            var client = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAI.OpenAIClientOptions
                {
                    Endpoint = new Uri(
                        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
                        ?? "https://dashscope.aliyuncs.com/compatible-mode/v1")
                });

            var chatClient = client.GetChatClient(
                Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "qwen-max");

            var prompt = $"Translate the following Markdown document from {sourceLang} to {targetLang}. " +
                         "Preserve all Markdown formatting, code blocks, tables, and links exactly. " +
                         "Only translate the visible text content.\n\n" + markdown;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a professional translator. Translate the given Markdown document accurately while preserving all formatting."),
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: ct);
            return response.Value.Content[0].Text;
        }
        catch
        {
            return null;
        }
    }

    // ── JSON deserialization types ──────────────────────────────────────

    private sealed class TreeRoot
    {
        public List<TreePage> Pages { get; set; } = [];
    }

    private sealed class TreePage
    {
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<TreePage> Children { get; set; } = [];
    }
}
