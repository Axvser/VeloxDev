using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;

namespace VeloxDev.Docs.ViewModels;

public sealed record LanguageOption(string Code, string DisplayName);

public partial class DocumentProvider : IWikiElement
{
    public const string DefaultLanguage = "en";

    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("en", "🌐 English"),
        new("zh", "🌐 中文"),
        new("ja", "🌐 日本語"),
        new("fr", "🌐 Français"),
        new("de", "🌐 Deutsch"),
        new("es", "🌐 Español"),
        new("it", "🌐 Italiano"),
        new("pt", "🌐 Português"),
        new("ru", "🌐 Русский"),
    ];

    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Children { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Nodes { get; set; }
    [VeloxProperty] public partial NodeProvider? SelectedNode { get; set; }
    [VeloxProperty] public partial bool IsEditMode { get; set; }
    [VeloxProperty] public partial string Language { get; set; }
    [VeloxProperty] public partial LanguageOption? SelectedLanguage { get; set; }

    public IReadOnlyList<LanguageOption> Languages => AvailableLanguages;

    private bool IsEnglish => !string.Equals(Language, "zh", StringComparison.OrdinalIgnoreCase);

    public string EditModeLabel => IsEditMode
        ? (IsEnglish ? "📝 Edit" : "📝 编辑")
        : (IsEnglish ? "👁 Browse" : "👁 浏览");

    public string NavigationLabel => IsEnglish ? "Navigation" : "导航";
    public string SaveLabel => IsEnglish ? "💾 Save" : "💾 保存";
    public string OpenLabel => IsEnglish ? "📂 Open" : "📂 打开";
    public string AddPageLabel => IsEnglish ? "+ Page" : "+ 页面";
    public string AddChildLabel => IsEnglish ? "+ Child" : "+ 子页";
    public string SaveTooltip => IsEnglish ? "Save to JSON" : "保存为 JSON";
    public string OpenTooltip => IsEnglish ? "Open JSON" : "打开 JSON";
    public string AddPageTooltip => IsEnglish ? "Add root page" : "添加根页面";
    public string AddChildTooltip => IsEnglish ? "Add child page" : "添加子页面";
    public string RemovePageTooltip => IsEnglish ? "Remove selected page" : "移除选中页面";
    public string LanguageTooltip => IsEnglish ? "Document language" : "文档语言";

    private bool _suppressReload;
    private static readonly AsyncLocal<int> _reloadSuppressionDepth = new();

    partial void OnIsEditModeChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(EditModeLabel));
    }

    partial void OnSelectedLanguageChanged(LanguageOption? oldValue, LanguageOption? newValue)
    {
        if (newValue is not null && !string.Equals(Language, newValue.Code, StringComparison.OrdinalIgnoreCase))
            Language = newValue.Code;
    }

    partial void OnLanguageChanged(string oldValue, string newValue)
    {
        var match = AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, newValue, StringComparison.OrdinalIgnoreCase))
                    ?? AvailableLanguages[0];
        if (!ReferenceEquals(SelectedLanguage, match))
            SelectedLanguage = match;

        OnPropertyChanged(nameof(EditModeLabel));
        OnPropertyChanged(nameof(NavigationLabel));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(OpenLabel));
        OnPropertyChanged(nameof(AddPageLabel));
        OnPropertyChanged(nameof(AddChildLabel));
        OnPropertyChanged(nameof(SaveTooltip));
        OnPropertyChanged(nameof(OpenTooltip));
        OnPropertyChanged(nameof(AddPageTooltip));
        OnPropertyChanged(nameof(AddChildTooltip));
        OnPropertyChanged(nameof(RemovePageTooltip));
        OnPropertyChanged(nameof(LanguageTooltip));

        // Avoid synchronous recursion: deserialization constructs new DocumentProvider
        // instances whose ctor sets Language, which would otherwise re-enter LoadDefault
        // synchronously (LoadDefault has no await before Deserialize) and overflow the stack.
        if (_suppressReload || _reloadSuppressionDepth.Value > 0)
            return;

        if (string.IsNullOrEmpty(oldValue))
            return;

        if (!string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
            _ = ReloadDefaultAsync(newValue);
    }

    private async Task ReloadDefaultAsync(string language)
    {
        try
        {
            var loaded = await LoadDefault(language).ConfigureAwait(true);
            Nodes = loaded.Nodes;
            SelectedNode = loaded.SelectedNode ?? Nodes.OfType<NodeProvider>().FirstOrDefault();
            Children = SelectedNode?.Children ?? [];
        }
        catch
        {
            // Fall back silently to current document if the localized resource is missing or invalid.
        }
    }

    public DocumentProvider()
    {
        _suppressReload = true;
        try
        {
            Children = [];
            Nodes = [];
            IsEditMode = false;
            Language = DefaultLanguage;
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableLanguages[0];
        }
        finally
        {
            _suppressReload = false;
        }
    }

    public static Task<DocumentProvider> LoadDefault(string language = DefaultLanguage)
        => Task.FromResult(LoadDefaultCore(language));

    private static DocumentProvider LoadDefaultCore(string language)
    {
        var code = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.ToLowerInvariant();
        var uri = new Uri($"avares://VeloxDev.Docs/Assets/Docs/wiki.{code}.json");

        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: false);
            return Deserialize(reader.ReadToEnd());
        }
        catch (FileNotFoundException)
        {
            // Fall back to the default language asset if the requested locale is missing.
            var fallback = new Uri($"avares://VeloxDev.Docs/Assets/Docs/wiki.{DefaultLanguage}.json");
            using var stream = AssetLoader.Open(fallback);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: false);
            return Deserialize(reader.ReadToEnd());
        }
    }

    private static DocumentProvider Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Wiki JSON content is empty.");

        DocumentProvider? document;
        // EnterReloadSuppression: prevents OnLanguageChanged in newly constructed
        //   DocumentProvider instances from re-entering LoadDefault.
        // HydrationScope: tells side-effecting setters (CodeProvider syntax
        //   highlighting, ImageProvider HTTP loads) to defer until hydration
        //   completes, removing the dominant cost of large document loads.
        using (EnterReloadSuppression())
        using (HydrationScope.Enter())
        {
            if (!json.TryDeserialize<DocumentProvider>(out document) || document is null)
                throw new InvalidDataException("Wiki JSON content could not be deserialized.");

            RepairParents(document);
            document.SelectedNode ??= document.Nodes.OfType<NodeProvider>().FirstOrDefault();
            document.Children = document.SelectedNode?.Children ?? [];
        }

        // Now that hydration is complete, run the deferred work outside the
        // hydration scope so syntax highlighting actually executes. Image loads
        // remain lazy and are triggered by the view when it attaches.
        FlushDeferredHighlighting(document);

        return document;
    }

    private static void FlushDeferredHighlighting(DocumentProvider document)
    {
        foreach (var element in EnumerateElements(document))
        {
            if (element is CodeProvider code)
                code.EnsureHighlighted();
        }
    }

    private static IEnumerable<IWikiElement> EnumerateElements(DocumentProvider document)
    {
        foreach (var element in document.Children)
            yield return element;

        foreach (var node in document.Nodes.OfType<NodeProvider>())
        {
            foreach (var element in EnumerateNode(node))
                yield return element;
        }
    }

    private static IEnumerable<IWikiElement> EnumerateNode(NodeProvider node)
    {
        foreach (var element in node.Children)
            yield return element;

        foreach (var child in node.Nodes.OfType<NodeProvider>())
        {
            foreach (var element in EnumerateNode(child))
                yield return element;
        }
    }

    private static IDisposable EnterReloadSuppression()
    {
        _reloadSuppressionDepth.Value++;
        return new ReloadSuppressionScope();
    }

    private sealed class ReloadSuppressionScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_reloadSuppressionDepth.Value > 0)
                _reloadSuppressionDepth.Value--;
        }
    }

    [VeloxCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    partial void OnSelectedNodeChanged(NodeProvider? oldValue, NodeProvider? newValue)
    {
        Children = newValue?.Children ?? [];
    }

    [VeloxCommand]
    private void AddRoot()
    {
        var node = NodeProvider.Create("New Page", this);
        Nodes.Add(node);
        SelectedNode = node;
    }

    [VeloxCommand]
    private void AddChild()
    {
        if (SelectedNode is not { } parent)
        {
            AddRoot();
            return;
        }

        var node = NodeProvider.Create("New Child", parent);
        parent.Nodes.Add(node);
        SelectedNode = node;
    }

    [VeloxCommand]
    private void RemoveNode()
    {
        if (SelectedNode is not { } node)
            return;

        if (node.Parent is NodeProvider parent)
            parent.Nodes.Remove(node);
        else
            Nodes.Remove(node);

        SelectedNode = null;
    }

    [VeloxCommand]
    private void AddTitle(object? parameter)
    {
        var level = parameter as string ?? "1";
        AddElement(new TitleProvider { Parent = this, Level = level, Text = "New title" });
    }

    [VeloxCommand]
    private void AddParagraph() =>
        AddElement(new ParagraphProvider { Parent = this, Text = "New paragraph" });

    [VeloxCommand]
    private void AddSubtitle() =>
        AddElement(new SubtitleProvider { Parent = this, Text = "New subtitle" });

    [VeloxCommand]
    private void AddLink() =>
        AddElement(new LinkProvider { Parent = this, Text = "VeloxDev", Url = "https://github.com/Axvser/VeloxDev" });

    [VeloxCommand]
    private void AddCode() =>
        AddElement(new CodeProvider { Parent = this, Language = "csharp", Code = "Console.WriteLine(\"Hello VeloxDev\");" });

    [VeloxCommand]
    private void AddMarkdown() =>
        AddElement(new MarkdownProvider
        {
            Parent = this,
            Text = "# Markdown\n\nWrite **Markdown** here.\n\n```csharp\nConsole.WriteLine(\"Hello VeloxDev\");\n```"
        });

    [VeloxCommand]
    private void AddTable() =>
        AddElement(TableProvider.CreateDefault(this));

    [VeloxCommand]
    private void AddImage() =>
        AddElement(new ImageProvider { Parent = this });

    [VeloxCommand]
    private void RemoveElement(object? parameter)
    {
        if (parameter is IWikiElement element)
            Children.Remove(element);
    }

    public void InsertEmptyAround(IWikiElement anchor, bool below)
    {
        var owner = FindOwnerCollection(anchor);
        if (owner is null)
            return;

        var index = owner.IndexOf(anchor);
        if (index < 0)
            return;

        var empty = new EmptyProvider { Parent = anchor.Parent };
        owner.Insert(below ? index + 1 : index, empty);
    }

    public void ReplaceElement(IWikiElement source, Type targetType)
    {
        var owner = FindOwnerCollection(source);
        if (owner is null)
            return;

        var index = owner.IndexOf(source);
        if (index < 0)
            return;

        owner[index] = EmptyProvider.CreateDefault(targetType, source.Parent);
    }

    public void ReplaceElement(IWikiElement source, IWikiElement replacement)
    {
        var owner = FindOwnerCollection(source);
        if (owner is null)
            return;

        var index = owner.IndexOf(source);
        if (index < 0)
            return;

        replacement.Parent = source.Parent;
        owner[index] = replacement;
    }

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        var storage = GetStorageProvider(parameter);
        if (storage is null)
            return;

        // Show the picker first so the user-gesture is consumed immediately.
        // Doing serialization before the picker can throw and silently swallow the dialog.
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Wiki",
            SuggestedFileName = "wiki.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }).ConfigureAwait(true);

        if (file is null)
            return;

        // Use SerializeToStreamAsync which handles StreamWriter + Flush internally.
        // On desktop: resolve the local path and use FileMode.Create to truncate any
        // existing content before writing. On browser: OpenWriteAsync always returns a
        // fresh download stream, so write directly without calling SetLength (browser
        // streams are write-only and do not support seeking or resizing).
        var path = file.TryGetLocalPath();
        if (path is not null)
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            try
            {
                await this.SerializeToStreamAsync(fs).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Save] SerializeToStreamAsync failed: {ex}");
                throw;
            }
        }
        else
        {
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            try
            {
                await this.SerializeToStreamAsync(stream).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Save] SerializeToStreamAsync (browser) failed: {ex}");
                throw;
            }
        }
    }

    [VeloxCommand]
    private async Task Load(object? parameter)
    {
        var storage = GetStorageProvider(parameter);
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Wiki",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }).ConfigureAwait(true);

        if (files.Count == 0)
            return;

        // In Avalonia.Browser the file picker stream is a JS ReadableStream wrapper.
        // StreamReader.ReadToEndAsync may fall back to synchronous reads on that stream,
        // blocking the UI thread. CopyToAsync pumps data asynchronously into a
        // MemoryStream, after which all reads are in-memory and never block.
        string json;
        await using (var fileStream = await files[0].OpenReadAsync().ConfigureAwait(true))
        {
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms).ConfigureAwait(true);
            ms.Position = 0;
            using var reader = new StreamReader(ms, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            json = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(json))
            return;

        // Deserialization (Newtonsoft + reflection over many polymorphic
        // elements) is CPU-bound. Move it off the UI thread on platforms that
        // support real threads. On WASM Task.Run executes inline on the UI
        // thread; the explicit Task.Yield above still gives the file picker
        // dialog a chance to dismiss before the parse begins.
        await Task.Yield();

        DocumentProvider document;
        try
        {
            document = await Task.Run(() => Deserialize(json)).ConfigureAwait(true);
        }
        catch (InvalidDataException)
        {
            return;
        }

        // Apply the loaded document atomically to minimize UI churn. Suppress
        // the language-change reload so that adopting the document's persisted
        // language does not race a fresh asset load on top of the user's file.
        _suppressReload = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(document.Language))
                Language = document.Language;
        }
        finally
        {
            _suppressReload = false;
        }

        Nodes = document.Nodes;
        SelectedNode = document.SelectedNode ?? Nodes.OfType<NodeProvider>().FirstOrDefault();
        Children = SelectedNode?.Children ?? [];
    }

    private static IStorageProvider? GetStorageProvider(object? parameter) => parameter switch
    {
        IStorageProvider storage => storage,
        Visual visual => TopLevel.GetTopLevel(visual)?.StorageProvider,
        _ => null
    };

    private void AddElement(IWikiElement element)
    {
        if (SelectedNode is null)
            AddRoot();

        SelectedNode?.Children.Add(element);
        Children = SelectedNode?.Children ?? [];
    }

    private ObservableCollection<IWikiElement>? FindOwnerCollection(IWikiElement element)
    {
        if (Children.Contains(element))
            return Children;

        return FindOwnerCollection(Nodes.OfType<NodeProvider>(), element);
    }

    private static ObservableCollection<IWikiElement>? FindOwnerCollection(IEnumerable<NodeProvider> nodes, IWikiElement element)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Contains(element))
                return node.Children;

            var nested = FindOwnerCollection(node.Nodes.OfType<NodeProvider>(), element);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void RepairParents(DocumentProvider document)
    {
        foreach (var node in document.Nodes.OfType<NodeProvider>())
            RepairNode(node, document);
    }

    private static void RepairNode(NodeProvider node, IWikiElement parent)
    {
        node.Parent = parent;

        foreach (var child in node.Children)
            child.Parent = node;

        foreach (var childNode in node.Nodes.OfType<NodeProvider>())
            RepairNode(childNode, node);
    }
}
