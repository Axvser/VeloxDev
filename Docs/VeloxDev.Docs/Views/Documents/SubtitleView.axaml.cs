using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class SubtitleView : WikiElementViewBase
{
    private SubtitleProvider? _provider;

    public SubtitleView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= OnProviderChanged;

        _provider = DataContext as SubtitleProvider;
        if (_provider is not null)
            _provider.PropertyChanged += OnProviderChanged;

        Refresh();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubtitleProvider.Text))
            Refresh();
    }

    private void Refresh() => SetTextViaRun(DisplayText, _provider?.Text);
}
