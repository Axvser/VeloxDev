using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using Demo.ViewModels;

namespace Demo.Views;

/// <summary>A XAML board bound to a generated view model and its generated commands.</summary>
public sealed partial class ViewModelBoard : StackPanel
{
    /// <summary>Creates the board and binds it to a fresh view model.</summary>
    public ViewModelBoard()
    {
        var theme = Theme.Current;
        Resources["Text"] = theme.Foreground;
        Resources["Muted"] = theme.MutedForeground;
        DataContext = new TaskBoardViewModel();
        InitializeComponent();
    }
}
