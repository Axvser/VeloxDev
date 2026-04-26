using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class Tree : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial ObservableCollection<IWikiElement> Children { get; set; }
}
