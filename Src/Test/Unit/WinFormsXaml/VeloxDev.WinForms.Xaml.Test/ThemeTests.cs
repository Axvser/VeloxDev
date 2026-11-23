using System.Drawing;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The built-in Dark+, Light+ and high-contrast palettes and the metrics the visual controls take from them.</summary>
[STATestClass]
public sealed class ThemeTests
{
    [TestMethod]
    public void BuiltInPalettes_ExposeTheVSCodeDarkAndLightAnchors()
    {
        var dark = VeloxDev.WinForms.Xaml.Theme.CreateDark();
        var light = VeloxDev.WinForms.Xaml.Theme.CreateLight();

        Assert.AreEqual(Color.FromArgb(0xFF, 0x14, 0x14, 0x14), dark.WindowBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x25, 0x25, 0x26), dark.Surface);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D), dark.SurfaceAlt);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A), dark.ControlBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x33, 0x33, 0x33), dark.ControlBorder);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x30, 0x30, 0x30), dark.ControlHover);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A), dark.ControlPressed);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xE7, 0xE7, 0xE7), dark.Foreground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0), dark.MutedForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), dark.Accent);
        Assert.AreEqual(Color.White, dark.AccentForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), dark.FocusRing);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x09, 0x47, 0x71), dark.Selection);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x58, 0x58, 0x58), dark.DisabledForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F), dark.DisabledBackground);
        Assert.AreEqual(4f, dark.CornerRadius);
        Assert.AreEqual(28, dark.ControlHeight);

        Assert.AreEqual(Color.White, light.WindowBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3), light.Surface);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD), light.SurfaceAlt);
        Assert.AreEqual(Color.White, light.ControlBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xCE, 0xCE, 0xCE), light.ControlBorder);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8), light.ControlHover);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x62, 0xA3), light.ControlPressed);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x33, 0x33, 0x33), light.Foreground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x6F, 0x6F, 0x6F), light.MutedForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), light.Accent);
        Assert.AreEqual(Color.White, light.AccentForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x90, 0xF1), light.FocusRing);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xAD, 0xD6, 0xFF), light.Selection);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0), light.DisabledForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3), light.DisabledBackground);
        Assert.AreEqual(4f, light.CornerRadius);
        Assert.AreEqual(28, light.ControlHeight);
    }

    [TestMethod]
    public void BuiltInPalettes_DifferWhereTheThemesDiffer()
    {
        var dark = VeloxDev.WinForms.Xaml.Theme.CreateDark();
        var light = VeloxDev.WinForms.Xaml.Theme.CreateLight();

        Assert.AreEqual(dark.Accent, light.Accent, "VS Code uses the same brand blue in both themes");
        Assert.AreEqual(dark.AccentForeground, light.AccentForeground);
        Assert.AreEqual(dark.CornerRadius, light.CornerRadius);
        Assert.AreEqual(dark.ControlHeight, light.ControlHeight);

        Assert.AreNotEqual(dark.WindowBackground, light.WindowBackground);
        Assert.AreNotEqual(dark.Surface, light.Surface);
        Assert.AreNotEqual(dark.SurfaceAlt, light.SurfaceAlt);
        Assert.AreNotEqual(dark.ControlBackground, light.ControlBackground);
        Assert.AreNotEqual(dark.ControlBorder, light.ControlBorder);
        Assert.AreNotEqual(dark.ControlHover, light.ControlHover);
        Assert.AreNotEqual(dark.ControlPressed, light.ControlPressed);
        Assert.AreNotEqual(dark.Foreground, light.Foreground);
        Assert.AreNotEqual(dark.MutedForeground, light.MutedForeground);
        Assert.AreNotEqual(dark.FocusRing, light.FocusRing);
        Assert.AreNotEqual(dark.Selection, light.Selection);
        Assert.AreNotEqual(dark.DisabledForeground, light.DisabledForeground);
        Assert.AreNotEqual(dark.DisabledBackground, light.DisabledBackground);

        Assert.IsTrue(dark.WindowBackground.GetBrightness() < light.WindowBackground.GetBrightness());
        Assert.IsTrue(dark.WindowBackground.GetBrightness() < dark.Surface.GetBrightness(),
            "the Dark+ side surface is lighter than the editor background");
        Assert.IsTrue(light.Surface.GetBrightness() < light.WindowBackground.GetBrightness(),
            "the Light+ side surface is darker than the editor background");
        Assert.AreNotEqual(dark.Surface, dark.WindowBackground);
        Assert.AreNotEqual(light.Surface, light.WindowBackground);
    }

    [TestMethod]
    public void BuiltInPalettes_ParseFromXamlHexLiterals()
    {
        var dark = VeloxDev.WinForms.Xaml.Theme.CreateDark();
        var light = VeloxDev.WinForms.Xaml.Theme.CreateLight();

        Assert.AreEqual(dark.WindowBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#141414").ToArgb());
        Assert.AreEqual(dark.Surface.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#252526").ToArgb());
        Assert.AreEqual(dark.ControlBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#2A2A2A").ToArgb());
        Assert.AreEqual(dark.ControlPressed.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#3A3A3A").ToArgb());
        Assert.AreEqual(dark.Selection.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#FF094771").ToArgb());
        Assert.AreEqual(light.WindowBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#FFFFFF").ToArgb());
        Assert.AreEqual(light.Selection.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#ADD6FF").ToArgb());
    }

    [TestMethod]
    public void HighContrastPalette_IsBlackWithAnAmberAccent()
    {
        var contrast = VeloxDev.WinForms.Xaml.Theme.CreateHighContrast();

        Assert.AreEqual(Color.Black, contrast.WindowBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0A), contrast.Surface);
        Assert.AreEqual(Color.White, contrast.Foreground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xCF, 0xCF, 0xCF), contrast.MutedForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00), contrast.Accent);
        Assert.AreEqual(Color.Black, contrast.AccentForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00), contrast.FocusRing);
        Assert.AreEqual(2f, contrast.CornerRadius);
        Assert.AreEqual(30, contrast.ControlHeight);
    }

    [TestMethod]
    public void ThemeModeChange_HighContrastSwapsThePalette()
    {
        var original = VeloxDev.WinForms.Xaml.Theme.Mode;
        try
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = VeloxDev.WinForms.Xaml.ThemeMode.HighContrast;

            Assert.AreEqual(Color.Black, VeloxDev.WinForms.Xaml.Theme.Current.WindowBackground);
            Assert.AreEqual(Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00), VeloxDev.WinForms.Xaml.Theme.Current.Accent);

            using var button = new VC.Button();
            Assert.AreEqual(30, button.MinHeight, "controls pick up the high-contrast metrics");
        }
        finally
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = original;
        }
    }

    [TestMethod]
    public void VisualControls_TakeTheirMetricsFromTheActivePalette()
    {
        var palette = VeloxDev.WinForms.Xaml.Theme.Current;
        using var button = new VC.Button();
        using var textBox = new VC.TextBox();
        using var border = new VC.Border();

        Assert.AreEqual(palette.ControlHeight, button.MinHeight);
        Assert.AreEqual(palette.CornerRadius, button.CornerRadius);
        Assert.AreEqual(palette.ControlHeight, textBox.MinHeight);
        Assert.AreEqual(palette.CornerRadius, textBox.CornerRadius);
        Assert.AreEqual(palette.CornerRadius, border.CornerRadius);
    }

    [TestMethod]
    public void ThemeModeChange_SwapsThePaletteForNewControls()
    {
        var original = VeloxDev.WinForms.Xaml.Theme.Mode;
        try
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = VeloxDev.WinForms.Xaml.ThemeMode.Light;

            Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), VeloxDev.WinForms.Xaml.Theme.Current.Accent);
            Assert.AreEqual(Color.FromArgb(0xFF, 0x33, 0x33, 0x33), VeloxDev.WinForms.Xaml.Theme.Current.Foreground);
            Assert.AreEqual(4f, VeloxDev.WinForms.Xaml.Theme.Current.CornerRadius);
            Assert.AreEqual(28, VeloxDev.WinForms.Xaml.Theme.Current.ControlHeight);

            using var button = new VC.Button();
            Assert.AreEqual(VeloxDev.WinForms.Xaml.Theme.Current.ControlHeight, button.MinHeight);
        }
        finally
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = original;
        }
    }
}
