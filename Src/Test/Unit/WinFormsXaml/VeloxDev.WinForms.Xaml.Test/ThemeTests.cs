using System.Drawing;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The built-in VS Code Dark+/Light+ palettes and the implicit fallback styles they feed.</summary>
[STATestClass]
public sealed class ThemeTests
{
    [TestMethod]
    public void BuiltInPalettes_ExposeTheVSCodeDarkAndLightAnchors()
    {
        var dark = VeloxDev.WinForms.Xaml.Theme.CreateDark();
        var light = VeloxDev.WinForms.Xaml.Theme.CreateLight();

        Assert.AreEqual(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E), dark.WindowBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x25, 0x25, 0x26), dark.Surface);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x3C, 0x3C, 0x3C), dark.SurfaceAlt);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x3C, 0x3C, 0x3C), dark.ControlBackground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x3C, 0x3C, 0x3C), dark.ControlBorder);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x4A, 0x4A, 0x4A), dark.ControlHover);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x0E, 0x63, 0x9C), dark.ControlPressed);
        Assert.AreEqual(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC), dark.Foreground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x85, 0x85, 0x85), dark.MutedForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC), dark.Accent);
        Assert.AreEqual(Color.White, dark.AccentForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x7F, 0xD4), dark.FocusRing);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x26, 0x4F, 0x78), dark.Selection);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A), dark.DisabledForeground);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D), dark.DisabledBackground);
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
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC), light.Accent);
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

        Assert.AreEqual(dark.WindowBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#1E1E1E").ToArgb());
        Assert.AreEqual(dark.Surface.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#252526").ToArgb());
        Assert.AreEqual(dark.ControlBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#3C3C3C").ToArgb());
        Assert.AreEqual(dark.ControlPressed.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#0E639C").ToArgb());
        Assert.AreEqual(dark.Selection.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#FF264F78").ToArgb());
        Assert.AreEqual(light.WindowBackground.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#FFFFFF").ToArgb());
        Assert.AreEqual(light.Selection.ToArgb(), VeloxDev.WinForms.Xaml.XamlRuntime.ParseColor("#ADD6FF").ToArgb());
    }

    [TestMethod]
    public void LibraryControls_GetTheThemeFallbackButDerivedControlsDoNot()
    {
        using var panel = new V.Panel();
        using var button = new V.Button();

        panel.Controls.Add(button);

        Assert.IsNotNull(button.Style);
        Assert.AreEqual(VeloxDev.WinForms.Xaml.Theme.Current.ControlBackground, button.Background?.ApproximateColor);
    }

    [TestMethod]
    public void ThemeModeChange_SwapsThePaletteAndReappliesTheTree()
    {
        var original = VeloxDev.WinForms.Xaml.Theme.Mode;
        using var panel = new V.Panel();
        using var button = new V.Button();
        panel.Controls.Add(button);

        try
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = VeloxDev.WinForms.Xaml.ThemeMode.Light;
            VeloxDev.WinForms.Xaml.Theme.ApplyTo(panel);

            Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC), VeloxDev.WinForms.Xaml.Theme.Current.Accent);
            Assert.AreEqual(Color.FromArgb(0xFF, 0x33, 0x33, 0x33), VeloxDev.WinForms.Xaml.Theme.Current.Foreground);
            Assert.AreEqual(4f, VeloxDev.WinForms.Xaml.Theme.Current.CornerRadius);
            Assert.AreEqual(28, VeloxDev.WinForms.Xaml.Theme.Current.ControlHeight);
            Assert.AreEqual(VeloxDev.WinForms.Xaml.Theme.Current.ControlBackground, button.Background?.ApproximateColor);
        }
        finally
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = original;
        }
    }

    [TestMethod]
    public void ThemeStyles_HandThePaletteTokensToInteractiveControls()
    {
        WithMode(VeloxDev.WinForms.Xaml.ThemeMode.Dark, () =>
        {
            var palette = VeloxDev.WinForms.Xaml.Theme.Current;
            using var panel = new V.Panel();
            using var button = new V.Button();
            using var textBox = new V.TextBox();
            using var richTextBox = new V.RichTextBox();
            using var slider = new V.Slider();
            using var progress = new V.ProgressBar();
            using var scrollBar = new V.ScrollBar();

            panel.Controls.AddRange([button, textBox, richTextBox, slider, progress, scrollBar]);

            Assert.AreEqual(palette.ControlBackground, button.Background?.ApproximateColor);
            Assert.AreEqual(palette.Foreground, button.Foreground?.ApproximateColor);
            Assert.AreEqual(palette.ControlBorder, button.BorderBrush?.ApproximateColor);
            Assert.AreEqual(palette.CornerRadius, button.CornerRadius);

            Assert.AreEqual(palette.ControlBackground, textBox.Background?.ApproximateColor);
            Assert.AreEqual(palette.Foreground, textBox.Foreground?.ApproximateColor);
            Assert.AreEqual(palette.ControlBorder, textBox.BorderBrush?.ApproximateColor);

            Assert.AreEqual(palette.ControlBackground, richTextBox.Background?.ApproximateColor);
            Assert.AreEqual(palette.Foreground, richTextBox.Foreground?.ApproximateColor);
            Assert.AreEqual(palette.ControlBorder, richTextBox.BorderBrush);

            Assert.AreEqual(palette.Accent, slider.Foreground?.ApproximateColor);
            Assert.AreEqual(palette.SurfaceAlt, slider.Background?.ApproximateColor);
            Assert.AreEqual(palette.Accent, slider.ThumbBrush?.ApproximateColor);

            Assert.AreEqual(palette.Accent, progress.Foreground?.ApproximateColor);
            Assert.AreEqual(palette.SurfaceAlt, progress.Background?.ApproximateColor);

            Assert.AreEqual(palette.SurfaceAlt, scrollBar.TrackBrush?.ApproximateColor);
            Assert.AreEqual(palette.MutedForeground, scrollBar.ThumbBrush?.ApproximateColor);
            Assert.AreEqual(palette.Foreground, scrollBar.ThumbHoverBrush?.ApproximateColor);
            Assert.AreEqual(palette.Surface, scrollBar.ThumbBorderBrush?.ApproximateColor);
            Assert.AreEqual(palette.CornerRadius, scrollBar.ThumbCornerRadius);
        });
    }

    private static void WithMode(VeloxDev.WinForms.Xaml.ThemeMode mode, Action action)
    {
        var original = VeloxDev.WinForms.Xaml.Theme.Mode;
        try
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = mode;
            VeloxDev.WinForms.Xaml.ThemeStyles.Invalidate();
            action();
        }
        finally
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = original;
            VeloxDev.WinForms.Xaml.ThemeStyles.Invalidate();
        }
    }
}
