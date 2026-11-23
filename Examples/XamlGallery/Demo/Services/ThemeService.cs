using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;

namespace Demo;

/// <summary>Owns the active palette and the three-mode cycle; the shell view rebuilds when it changes.</summary>
internal interface IThemeService
{
    /// <summary>The active palette mode.</summary>
    ThemeMode Mode { get; }

    /// <summary>The label for the mode the next <see cref="Cycle"/> will select.</summary>
    string Label { get; }

    /// <summary>Advances Dark+ → Light+ → high contrast → Dark+.</summary>
    void Cycle();

    /// <summary>Raised after the palette mode changed.</summary>
    event EventHandler? Changed;
}

/// <summary>The gallery's palette service: sets the static theme and announces the switch.</summary>
internal sealed class ThemeService : IThemeService
{
    /// <inheritdoc />
    public ThemeMode Mode => Theme.Mode;

    /// <inheritdoc />
    public string Label => Theme.Mode switch
    {
        ThemeMode.Dark => "Light+ mode",
        ThemeMode.Light => "High contrast",
        _ => "Dark+ mode",
    };

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Cycle()
    {
        Theme.Mode = Theme.Mode switch
        {
            ThemeMode.Dark => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.HighContrast,
            _ => ThemeMode.Dark,
        };
        Application.SetColorMode(Theme.Mode == ThemeMode.Light ? SystemColorMode.Classic : SystemColorMode.Dark);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
