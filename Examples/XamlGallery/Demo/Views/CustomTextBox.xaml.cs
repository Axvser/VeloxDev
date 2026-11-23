using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace Demo.Views;

/// <summary>A fully visual, XAML-templated text field with the native editor island turned off.</summary>
public sealed partial class CustomTextBox : StackPanel
{
    /// <summary>Creates the view and seeds the field colours from the active palette.</summary>
    public CustomTextBox()
    {
        var theme = Theme.Current;
        Resources["FieldBackground"] = theme.SurfaceAlt;
        Resources["FieldBorder"] = theme.ControlBorder;
        Resources["AccentFieldBackground"] = theme.Surface;
        Resources["Accent"] = theme.Accent;
        Resources["Text"] = theme.Foreground;
        Resources["Muted"] = theme.MutedForeground;

        InitializeComponent();
    }
}
