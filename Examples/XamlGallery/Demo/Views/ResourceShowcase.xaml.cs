using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace Demo.Views;

/// <summary>A card whose colours, styles, trigger and template all come from XAML resources.</summary>
public sealed partial class ResourceShowcase : StackPanel
{
    /// <summary>Creates the view and seeds its resource dictionary with the active palette.</summary>
    public ResourceShowcase()
    {
        // XAML 里的 {StaticResource} 在构造期解析，所以调色板必须在 InitializeComponent 之前进字典。
        var theme = Theme.Current;
        Resources["SurfaceAlt"] = theme.SurfaceAlt;
        Resources["Accent"] = theme.Accent;
        Resources["AccentText"] = theme.AccentForeground;
        Resources["CardBackground"] = theme.Surface;
        Resources["CardBorder"] = theme.ControlBorder;
        Resources["Text"] = theme.Foreground;
        Resources["MutedText"] = theme.MutedForeground;
        Resources["DisabledBackground"] = theme.DisabledBackground;

        InitializeComponent();

        rows.ItemsSource = new[]
        {
            "Row one · StaticResource brush",
            "Row two · keyed DataTemplate",
            "Row three · current palette",
        };

        primaryButton.SetBinding(
            VisualElement.IsEnabledProperty,
            new Binding("IsChecked") { Source = enableSwitch, Mode = BindingMode.OneWay });
    }
}
