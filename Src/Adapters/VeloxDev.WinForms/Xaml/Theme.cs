using System.Drawing;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Which palette the default control styles use.</summary>
public enum ThemeMode
{
    /// <summary>A light neutral palette.</summary>
    Light,

    /// <summary>A dark neutral palette.</summary>
    Dark,
}

/// <summary>The colours and metrics a default control style reads.</summary>
/// <remarks>
/// The palette is deliberately neutral: surfaces are near-grey, the accent is the only saturated colour, and
/// corners are gently rounded. A host may replace any entry by setting an explicit value or an implicit style.
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
    public float CornerRadius { get; set; } = 8f;

    /// <summary>The default interactive control height in pixels.</summary>
    public int ControlHeight { get; set; } = 34;
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
            ThemeStyles.Invalidate();
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
            ThemeStyles.Invalidate();
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Re-applies the active implicit styles over a control tree after a theme change.</summary>
    /// <param name="root">The view or window root.</param>
    public static void ApplyTo(System.Windows.Forms.Control root) => ThemeStyles.ApplyTo(root);

    /// <summary>The built-in dark palette.</summary>
    public static ThemePalette CreateDark() => new()
    {
        WindowBackground = Color.FromArgb(0xFF, 0x0F, 0x11, 0x15),
        Surface = Color.FromArgb(0xFF, 0x16, 0x19, 0x1F),
        SurfaceAlt = Color.FromArgb(0xFF, 0x1D, 0x21, 0x29),
        ControlBackground = Color.FromArgb(0xFF, 0x20, 0x24, 0x2D),
        ControlBorder = Color.FromArgb(0xFF, 0x30, 0x37, 0x45),
        ControlHover = Color.FromArgb(0xFF, 0x27, 0x2C, 0x36),
        ControlPressed = Color.FromArgb(0xFF, 0x32, 0x39, 0x47),
        Foreground = Color.FromArgb(0xFF, 0xE9, 0xEC, 0xF2),
        MutedForeground = Color.FromArgb(0xFF, 0x98, 0xA2, 0xB3),
        Accent = Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF),
        AccentForeground = Color.White,
        FocusRing = Color.FromArgb(0xFF, 0x7F, 0xA6, 0xFF),
        DisabledForeground = Color.FromArgb(0xFF, 0x6B, 0x72, 0x80),
        DisabledBackground = Color.FromArgb(0xFF, 0x1B, 0x1F, 0x27),
        Selection = Color.FromArgb(0xFF, 0x2B, 0x3F, 0x66),
        CornerRadius = 8f,
        ControlHeight = 34,
    };

    /// <summary>The built-in light palette.</summary>
    public static ThemePalette CreateLight() => new()
    {
        WindowBackground = Color.FromArgb(0xFF, 0xF5, 0xF7, 0xFB),
        Surface = Color.White,
        SurfaceAlt = Color.FromArgb(0xFF, 0xEE, 0xF2, 0xF8),
        ControlBackground = Color.White,
        ControlBorder = Color.FromArgb(0xFF, 0xD6, 0xDC, 0xE6),
        ControlHover = Color.FromArgb(0xFF, 0xF1, 0xF4, 0xF9),
        ControlPressed = Color.FromArgb(0xFF, 0xE4, 0xE9, 0xF2),
        Foreground = Color.FromArgb(0xFF, 0x14, 0x18, 0x21),
        MutedForeground = Color.FromArgb(0xFF, 0x5A, 0x64, 0x73),
        Accent = Color.FromArgb(0xFF, 0x0B, 0x63, 0xCE),
        AccentForeground = Color.White,
        FocusRing = Color.FromArgb(0xFF, 0x0B, 0x63, 0xCE),
        DisabledForeground = Color.FromArgb(0xFF, 0x9A, 0xA3, 0xB2),
        DisabledBackground = Color.FromArgb(0xFF, 0xF0, 0xF3, 0xF8),
        Selection = Color.FromArgb(0xFF, 0xD7, 0xE6, 0xFA),
        CornerRadius = 8f,
        ControlHeight = 34,
    };
}
