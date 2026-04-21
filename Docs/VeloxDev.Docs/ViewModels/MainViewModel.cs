using CommunityToolkit.Mvvm.ComponentModel;

namespace VeloxDev.Docs.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";
    }
}
