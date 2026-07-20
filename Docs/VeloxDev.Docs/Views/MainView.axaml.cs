using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using VeloxDev.Docs.ViewModels;
using VeloxDev.DynamicTheme;

namespace VeloxDev.Docs.Views
{
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            InitializeTheme();

            MarkdownPreview.OnReady += async (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.Document.RenderMarkdownAsync = async md =>
                    {
                        await MarkdownPreview.RenderMarkdownAsync(md);
                    };
                    vm.Document.MarkdownViewReady();
                }
            };

            // Wire translation settings button programmatically (avoids compiled binding issues)
            TranslationSettingsButton.Click += OnTranslationSettingsClicked;

            Loaded += (s, e) =>
            {
                // Browser z‑index workaround: hide MarkdownView when ComboBox dropdown opens
                ListenToComboBoxPopups();

                var settings = this.GetPlatformSettings();

                if (settings?.GetColorValues() is PlatformColorValues colors)
                {
                    UpdateTheme(colors);
                }

                settings?.ColorValuesChanged += (sender, values) =>
                {
                    if (settings.GetColorValues() is PlatformColorValues colors)
                    {
                        UpdateTheme(colors);
                    }
                };
            };
        }

        private void OnTranslationSettingsClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var settingsView = new TranslationSettingsView();
            var flyout = new Flyout
            {
                Content = settingsView,
                Placement = PlacementMode.BottomEdgeAlignedRight,
                ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway
            };

            // Hide MarkdownView while flyout is open (browser WebView z-index workaround)
            MarkdownPreview.IsVisible = false;
            flyout.Closed += (_, _) => MarkdownPreview.IsVisible = true;

            settingsView.Applied += () =>
            {
                flyout.Hide();
                if (DataContext is MainViewModel vm)
                    vm.Document.NotifyTranslationSupportedChanged();
            };
            settingsView.Cancelled += () => flyout.Hide();

            flyout.ShowAt(button);
        }

        /// <summary>
        /// Browser WebView iframe covers ComboBox popups.
        /// Hide the MarkdownView whenever a ComboBox dropdown opens and restore it when closed.
        /// </summary>
        private void ListenToComboBoxPopups()
        {
            var comboBoxes = this.GetVisualDescendants().OfType<ComboBox>().ToList();
            foreach (var cb in comboBoxes)
            {
                cb.DropDownOpened += (_, _) => MarkdownPreview.IsVisible = false;
                cb.DropDownClosed += (_, _) => MarkdownPreview.IsVisible = true;
            }
        }

        private static void UpdateTheme(PlatformColorValues colors)
        {
            if ((ThemeVariant?)colors?.ThemeVariant == ThemeVariant.Dark)
            {
                ThemeManager.Jump<Dark>();
            }
            else
            {
                ThemeManager.Jump<Light>();
            }
        }
    }
}
