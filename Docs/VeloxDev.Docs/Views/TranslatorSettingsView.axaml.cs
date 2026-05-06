using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs.Views;

public partial class TranslatorSettingsView : UserControl
{
    /// <summary>Raised when the user clicks Apply so the parent flyout can close.</summary>
    public event EventHandler? Applied;

    /// <summary>Raised when the user clicks Cancel so the parent flyout can close.</summary>
    public event EventHandler? Cancelled;

    public TranslatorSettingsView()
    {
        InitializeComponent();
        DataContext = new TranslatorSettingsViewModel();
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorSettingsViewModel vm)
            vm.Apply();
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorSettingsViewModel vm)
            vm.Reload();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
