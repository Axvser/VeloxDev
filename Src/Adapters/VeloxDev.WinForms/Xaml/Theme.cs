using System.Drawing;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Which palette the default control styles use.</summary>
public enum ThemeMode
{
    /// <summary>The Visual Studio Code Light+ palette.</summary>
    Light,

    /// <summary>The Visual Studio Code Dark+ palette.</summary>
    Dark,
}

/// <summary>The colours and metrics a default control style reads.</summary>
/// <remarks>
/// The built-in palettes follow Visual Studio Code's Dark+ and Light+ themes: near-grey surfaces, a single blue
/// accent, and four-pixel corners. A host may replace any entry by setting an explicit value or an implicit style.
/// </remarks>
public sealed class ThemePalette
{
    /// <summary>The application background behind panels.</summary>
    public Color WindowBackground { get; set; }

    /// <summary>A raised surface such as a panel or card.</summary>
    public Color Surface { get; set; }

    /// <summary>A second surface level for headers, tracks and inset areas.</summary>
    public Color SurfaceAlt { get; set; }

    /// <summary>The default background of an interactive control.</summary>
    public Color ControlBackground { get; set; }

    /// <summary>The default border of an interactive control.</summary>
    public Color ControlBorder { get; set; }

    /// <summary>The hover background of an interactive control.</summary>
    public Color ControlHover { get; set; }

    /// <summary>The pressed background of an interactive control.</summary>
    public Color ControlPressed { get; set; }

    /// <summary>The primary text and glyph colour.</summary>
    public Color Foreground { get; set; }

    /// <summary>A secondary text colour for labels and placeholders.</summary>
    public Color MutedForeground { get; set; }

    /// <summary>The accent used by checked, selected and primary states.</summary>
    public Color Accent { get; set; }

    /// <summary>The text or glyph drawn on <see cref="Accent"/>.</summary>
    public Color AccentForeground { get; set; }

    /// <summary>The focus ring colour.</summary>
    public Color FocusRing { get; set; }

    /// <summary>The text colour of a disabled control.</summary>
    public Color DisabledForeground { get; set; }

    /// <summary>The background of a disabled control.</summary>
    public Color DisabledBackground { get; set; }

    /// <summary>The row or item selection background.</summary>
    public Color Selection { get; set; }

    /// <summary>The default corner radius in pixels.</summary>
    public float CornerRadius { get; set; } = 4f;

    /// <summary>The default interactive control height in pixels.</summary>
    public int ControlHeight { get; set; } = 28;
}

/// <summary>The active default palette for controls that do not set an explicit colour.</summary>
public static class Theme
{
    private static ThemeMode _mode = ThemeMode.Dark;
    private static ThemePalette _current = CreateDark();

    /// <summary>Raised after <see cref="Mode"/> or <see cref="Current"/> changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>The active mode; setting it swaps in the matching built-in palette.</summary>
    public static ThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            _current = value == ThemeMode.Dark ? CreateDark() : CreateLight();
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The active palette. Assigning a custom palette raises <see cref="Changed"/>.</summary>
    public static ThemePalette Current
    {
        get => _current;
        set
        {
            _current = value ?? throw new ArgumentNullException(nameof(value));
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The built-in dark palette, matching the VS Code workbench and the Scribalix tokens.</summary>
    public static ThemePalette CreateDark() => new()
    {
        WindowBackground = Color.FromArgb(0xFF, 0x14, 0x14, 0x14),
        Surface = Color.FromArgb(0xFF, 0x25, 0x25, 0x26),
        SurfaceAlt = Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D),
        ControlBackground = Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A),
        ControlBorder = Color.FromArgb(0xFF, 0x33, 0x33, 0x33),
        ControlHover = Color.FromArgb(0xFF, 0x30, 0x30, 0x30),
        ControlPressed = Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A),
        Foreground = Color.FromArgb(0xFF, 0xE7, 0xE7, 0xE7),
        MutedForeground = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0),
        Accent = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
        AccentForeground = Color.White,
        FocusRing = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
        DisabledForeground = Color.FromArgb(0xFF, 0x58, 0x58, 0x58),
        DisabledBackground = Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F),
        Selection = Color.FromArgb(0xFF, 0x09, 0x47, 0x71),
        CornerRadius = 4f,
        ControlHeight = 28,
    };

    /// <summary>The built-in light palette, matching Visual Studio Code Light+.</summary>
    public static ThemePalette CreateLight() => new()
    {
        WindowBackground = Color.White,
        Surface = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
        SurfaceAlt = Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD),
        ControlBackground = Color.White,
        ControlBorder = Color.FromArgb(0xFF, 0xCE, 0xCE, 0xCE),
        ControlHover = Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8),
        ControlPressed = Color.FromArgb(0xFF, 0x00, 0x62, 0xA3),
        Foreground = Color.FromArgb(0xFF, 0x33, 0x33, 0x33),
        MutedForeground = Color.FromArgb(0xFF, 0x6F, 0x6F, 0x6F),
        Accent = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
        AccentForeground = Color.White,
        FocusRing = Color.FromArgb(0xFF, 0x00, 0x90, 0xF1),
        DisabledForeground = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0),
        DisabledBackground = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
        Selection = Color.FromArgb(0xFF, 0xAD, 0xD6, 0xFF),
        CornerRadius = 4f,
        ControlHeight = 28,
    };
}
