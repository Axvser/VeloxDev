using System.Collections.ObjectModel;

namespace VeloxDev.Docs.ViewModels;

public interface ITreeElement : IWikiElement
{
    public ObservableCollection<IWikiElement> Children { get; set; }
    public ObservableCollection<IWikiElement> Nodes { get; set; }
    public string Title { get; set; }
}
