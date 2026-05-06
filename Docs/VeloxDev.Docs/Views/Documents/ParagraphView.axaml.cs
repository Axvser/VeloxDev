using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class ParagraphView : WikiElementViewBase
{
    public ParagraphView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    private ParagraphProvider? _provider;

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= OnProviderChanged;

        _provider = DataContext as ParagraphProvider;
        if (_provider is not null)
            _provider.PropertyChanged += OnProviderChanged;

        Refresh();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParagraphProvider.Text))
            Refresh();
    }

    private void Refresh() => SetTextViaRun(DisplayPanel, _provider?.Text);
}
