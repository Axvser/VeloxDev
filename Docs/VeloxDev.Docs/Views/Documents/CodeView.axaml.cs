using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class CodeView : WikiElementViewBase
{
    private CodeProvider? _provider;

    public CodeView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        PreferOwnScrolling(DisplayScrollViewer);
        PreferOwnScrolling(EditScrollViewer);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= ProviderPropertyChanged;

        _provider = DataContext as CodeProvider;
        if (_provider is not null)
            _provider.PropertyChanged += ProviderPropertyChanged;

        ApplyHeightMode();
    }

    private void ProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeProvider.AutoHeight) ||
            e.PropertyName == nameof(CodeProvider.MaxHeightValue))
        {
            ApplyHeightMode();
        }
    }

    private void ApplyHeightMode()
    {
        var autoHeight = _provider?.AutoHeight ?? false;
        var maxHeight = System.Math.Max(120, _provider?.MaxHeightValue ?? 300);

        DisplayScrollViewer.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight;
        EditScrollViewer.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight;
        RootPanel.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight + 60;
        DisplayPanel.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight + 30;
        EditPanel.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight + 30;
    }
}