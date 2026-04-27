using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class NodeProvider : ITreeElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Children { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Nodes { get; set; }
    [VeloxProperty] public partial string Title { get; set; }

    public NodeProvider()
    {
        Title = "New Page";
        Children = [];
        Nodes = [];
    }

    public static NodeProvider Create(string title, IWikiElement? parent) => new()
    {
        Title = title,
        Parent = parent
    };
}
