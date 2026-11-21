using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Controls;

namespace Demo.Views.Sections;

/// <summary>The page journal and detached window examples.</summary>
public sealed partial class NavigationSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section and seeds the frame.</summary>
    public NavigationSection()
    {
        InitializeComponent();
        frame.Navigate(CreatePage("Overview", "The first page in the frame journal.", "Frame owns a small journal: Navigate, GoBack and GoForward."));
        frame.Navigate(CreatePage("Details", "Go back to see the first page again.", "Pages are ordinary XAML content controls; the frame only tracks their history."));
    }

    private void OnBackClick(object? sender, EventArgs e) => frame.GoBack();

    private void OnForwardClick(object? sender, EventArgs e) => frame.GoForward();

    private void OnOpenWindowClick(object? sender, EventArgs e)
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(24) };
        panel.Controls.Add(new TextBlock
        {
            Text = "Detached XAML Window",
            Font = GalleryTypography.SectionTitle,
            Foreground = new SolidColorBrush(Theme.Current.Foreground),
        });
        panel.Controls.Add(new TextBlock
        {
            Text = "This content is hosted by Window's own borderless Form.",
            Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
        });

        var border = new Border
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Theme.Current.Surface),
        };
        border.Controls.Add(panel);

        var window = new Window
        {
            Title = "Detached XAML Window",
            Size = new Size(480, 260),
            Content = border,
        };

        window.Show();
    }

    private void OnOpenNavigationWindowClick(object? sender, EventArgs e)
    {
        var window = new NavigationWindow
        {
            Title = "NavigationWindow · journal",
            Size = new Size(640, 440),
        };

        window.Navigate(CreatePage("Journal", "This page lives in the NavigationWindow's own frame.", "Back and forward work exactly like the inline frame."));
        window.Navigate(CreatePage("Second page", "Navigate back to the first page with the journal.", "NavigationWindow exposes Navigate, GoBack and GoForward."));
        window.Show();
    }

    private static Page CreatePage(string title, string text, string detail)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(24) };
        panel.Controls.Add(new TextBlock
        {
            Text = title,
            Font = GalleryTypography.SectionTitle,
            Foreground = new SolidColorBrush(Theme.Current.Foreground),
        });
        panel.Controls.Add(new TextBlock
        {
            Text = text,
            Font = GalleryTypography.Body,
            Foreground = new SolidColorBrush(Theme.Current.Foreground),
        });
        panel.Controls.Add(new TextBlock
        {
            Text = detail,
            Font = GalleryTypography.Caption,
            Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
        });

        return new Page
        {
            Title = title,
            Content = panel,
        };
    }
}
