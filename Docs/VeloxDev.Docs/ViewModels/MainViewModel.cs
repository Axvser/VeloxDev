using CommunityToolkit.Mvvm.ComponentModel;

namespace VeloxDev.Docs.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private DocumentViewModel _document = new();
}
