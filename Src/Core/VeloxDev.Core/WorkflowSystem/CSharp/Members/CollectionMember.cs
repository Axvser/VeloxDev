using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class CollectionMember
{
    [VeloxProperty] private string path = string.Empty;
    [VeloxProperty] private string valueName = string.Empty;
    [VeloxProperty] private string collectionType = string.Empty;
    [VeloxProperty] private string valueType = string.Empty;
    [VeloxProperty] private bool isEnabled;
    [VeloxProperty] private ObservableCollection<CollectionItem> items = null!;

    internal CSharpObject? Parent { get; set; }

    public CollectionMember()
    {
        Items = [];
    }

    [VeloxCommand]
    private void AddItem()
    {
        var nextIndex = Items.Count == 0
            ? 0
            : Items.Max(item => item.Index) + 1;
        Items.Add(new CollectionItem { Index = nextIndex });
    }

    partial void OnItemAddedToItems(IEnumerable<CollectionItem> items)
    {
        foreach (var item in items) item.Parent = this;
        IsEnabled = true;
    }

    partial void OnItemRemovedFromItems(IEnumerable<CollectionItem> items)
    {
        foreach (var item in items)
        {
            if (!Items.Contains(item)) item.Parent = null;
        }
    }
}
