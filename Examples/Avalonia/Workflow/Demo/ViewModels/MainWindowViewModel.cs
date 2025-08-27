using VeloxDev.Core.MVVM;

namespace Demo.ViewModels;

public partial class MainWindowViewModel
{
    [VeloxProperty] private string _greeting = "Welcome to Avalonia!";
}