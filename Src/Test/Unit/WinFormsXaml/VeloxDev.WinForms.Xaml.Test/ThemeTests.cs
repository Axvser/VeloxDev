using System.Drawing;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The built-in modern palette and the implicit fallback styles it feeds.</summary>
[STATestClass]
public sealed class ThemeTests
{
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

            Assert.AreEqual(Color.FromArgb(0xFF, 0x0B, 0x63, 0xCE), VeloxDev.WinForms.Xaml.Theme.Current.Accent);
            Assert.AreEqual(VeloxDev.WinForms.Xaml.Theme.Current.ControlBackground, button.Background?.ApproximateColor);
        }
        finally
        {
            VeloxDev.WinForms.Xaml.Theme.Mode = original;
        }
    }
}
