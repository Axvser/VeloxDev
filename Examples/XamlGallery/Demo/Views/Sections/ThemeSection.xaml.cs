using VeloxDev.WinForms.Xaml;

namespace Demo.Views.Sections;

/// <summary>The palette and brush examples.</summary>
public sealed partial class ThemeSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section.</summary>
    public ThemeSection()
    {
        InitializeComponent();
    }

    private void OnLightClick(object? sender, System.EventArgs e) => SetMode(ThemeMode.Light);

    private void OnDarkClick(object? sender, System.EventArgs e) => SetMode(ThemeMode.Dark);

    private void SetMode(ThemeMode mode)
    {
        Theme.Mode = mode;
        Theme.ApplyTo(this);
        ThemeStyles.ApplyTo(this);
        Invalidate(true);
    }
}
