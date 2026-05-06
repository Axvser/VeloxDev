using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using VeloxDev.Docs.Translation;
using VeloxDev.Docs.ViewModels;
using VeloxDev.Docs.Views;

namespace VeloxDev.Docs;

public partial class DocumentView : UserControl
{
    public DocumentView()
    {
        InitializeComponent();

        // Re-evaluate IsTranslationSupported whenever the API key is changed via the settings panel
        WikiTranslatorSettings.ApiKeyChanged += OnApiKeyChanged;
    }

    private void OnApiKeyChanged()
    {
        if (DataContext is DocumentProvider doc)
            doc.NotifyTranslationSupportedChanged();
    }

    private void OnTranslationSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var settingsView = new TranslatorSettingsView();
        var flyout = new Flyout
        {
            Content = settingsView,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway
        };

        settingsView.Applied += (_, _) =>
        {
            flyout.Hide();
            if (DataContext is DocumentProvider doc)
                doc.NotifyTranslationSupportedChanged();
        };
        settingsView.Cancelled += (_, _) => flyout.Hide();

        flyout.ShowAt(button);
    }
}
