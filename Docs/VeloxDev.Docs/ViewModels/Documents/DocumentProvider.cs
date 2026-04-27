using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System;
using System.Threading.Tasks;
using VeloxDev.MVVM.Serialization;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class DocumentProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Children { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Nodes { get; set; }
    [VeloxProperty] public partial NodeProvider? SelectedNode { get; set; }

    public DocumentProvider()
    {
        Children = [];
        Nodes = [];
    }

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

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        var storage = GetStorageProvider(parameter);
        if (storage is null)
            return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Wiki",
            SuggestedFileName = "wiki.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }).ConfigureAwait(true);

        if (file is null)
            return;

        var json = await this.SerializeAsync().ConfigureAwait(true);
        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);

        if (stream.CanSeek)
        {
            stream.SetLength(0);
            stream.Position = 0;
        }

        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json).ConfigureAwait(true);
        await writer.FlushAsync().ConfigureAwait(true);
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

        await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync().ConfigureAwait(true);
        if (json.TryDeserialize<DocumentProvider>(out var document) && document is not null)
        {
            RepairParents(document);
            Nodes = document.Nodes;
            SelectedNode = Nodes.OfType<NodeProvider>().FirstOrDefault();
            Children = SelectedNode?.Children ?? [];
        }
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
