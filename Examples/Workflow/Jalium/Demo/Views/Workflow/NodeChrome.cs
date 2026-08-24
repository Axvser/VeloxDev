using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Demo.Views.Workflow;

/// <summary>Shared dark palette + card chrome for the full-demo node views. Every node card is a
/// rounded dark border (accent-colored edge) with a header strip carrying the title + status badge
/// and a content area. Ports are NOT part of the card — <see cref="NodeViewBase.OnPostRender"/>
/// draws them on top from <see cref="NodePorts"/> so they always match the surface's hit-testing.</summary>
internal static class NodeChrome
{
    public static readonly SolidColorBrush CardBg = new(Color.FromRgb(0x25, 0x25, 0x25));
    public static readonly SolidColorBrush HeaderBg = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static readonly SolidColorBrush BodyBg = new(Color.FromRgb(0x1F, 0x1F, 0x1F));
    public static readonly SolidColorBrush EditorBg = new(Color.FromRgb(0x0D, 0x11, 0x17));
    public static readonly SolidColorBrush DefaultBorder = new(Color.FromRgb(0x4B, 0x4B, 0x4B));
    public static readonly SolidColorBrush SubBorder = new(Color.FromRgb(0x30, 0x36, 0x3D));
    public static readonly SolidColorBrush AccentBlue = new(Color.FromRgb(0x6E, 0xC6, 0xFF));
    public static readonly SolidColorBrush AccentGreen = new(Color.FromRgb(0x7B, 0xD9, 0x7B));
    public static readonly SolidColorBrush AccentYellow = new(Color.FromRgb(0xFF, 0xD5, 0x4A));
    public static readonly SolidColorBrush AccentViolet = new(Color.FromRgb(0xB7, 0x99, 0xFF));
    public static readonly SolidColorBrush TitleFg = new(Colors.White);
    public static readonly SolidColorBrush SubFg = new(Color.FromRgb(0x8B, 0x94, 0x9E));
    public static readonly SolidColorBrush StatusFg = new(Color.FromRgb(0xE4, 0xD8, 0xFF));

    public const double HeaderHeight = 36;

    /// <summary>Builds a rounded card whose body is a two-row grid: the header strip (title + status
    /// badge) in row 0 and the caller's <paramref name="content"/> grid in row 1.</summary>
    public static Border Card(double width, double height, Brush accent, string title, string? status,
        out Border header, out TextBlock titleText, out TextBlock statusText, out Grid content)
    {
        titleText = new TextBlock
        {
            Text = title,
            Foreground = TitleFg,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusText = new TextBlock
        {
            Text = status ?? string.Empty,
            Foreground = StatusFg,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(statusText);

        header = new Border
        {
            Height = HeaderHeight,
            Background = HeaderBg,
            BorderBrush = accent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            Child = headerPanel,
        };
        content = new Grid();
        Grid.SetRow(content, 1);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(HeaderHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(header, 0);
        body.Children.Add(header);
        body.Children.Add(content);

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = accent,
            Background = CardBg,
            Child = body,
        };
    }

    /// <summary>Creates a styled push button.</summary>
    public static Button MakeButton(string text, Brush background, Brush foreground, Brush border, double fontSize = 12)
        => new()
        {
            Content = text,
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontSize = fontSize,
            Padding = new Thickness(8, 5),
        };
}
