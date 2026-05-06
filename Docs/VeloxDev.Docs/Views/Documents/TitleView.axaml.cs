using Avalonia.Media;
using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class TitleView : WikiElementViewBase
{
    private TitleProvider? _provider;

    public TitleView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    protected override void ExitEdit()
    {
        base.ExitEdit();
        Refresh();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= OnProviderChanged;

        _provider = DataContext as TitleProvider;
        if (_provider is not null)
            _provider.PropertyChanged += OnProviderChanged;

        Refresh();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TitleProvider.Text) or nameof(TitleProvider.Level))
            Refresh();
    }

    private void Refresh()
    {
        if (_provider is null) return;

        var fontSize = _provider.Level switch
        {
            "1" => 30.0,
            "2" => 24.0,
            "3" => 20.0,
            "4" => 16.0,
            _ => 24.0
        };

        // Match MarkdownView headings: only adjust size, never weight.
        // Skia's CJK fallback fails to find a Bold/SemiBold CJK face on many
        // systems and falls back to .notdef glyphs ("tofu" / mojibake).
        DisplayPanel.FontSize = fontSize;
        DisplayPanel.FontWeight = FontWeight.Normal;

        SetTextViaRun(DisplayPanel, _provider.Text);
    }
}
