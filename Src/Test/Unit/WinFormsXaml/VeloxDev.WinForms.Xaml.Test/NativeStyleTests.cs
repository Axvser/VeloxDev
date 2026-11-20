using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Native-control styles: native CLR property setters through TypeDescriptor, restore on removal, BasedOn and
/// mutation reapplication. Triggers/templates are a Control feature and are not used here.
/// </summary>
[STATestClass]
public sealed class NativeStyleTests
{
    [TestMethod]
    public void GeneratedNativeStyle_AppliesAndUnapplyRestores()
    {
        using var view = new GeneratedView();
        var button = view.Controls.OfType<Button>().Single();

        Assert.AreEqual("Styled probe", button.Text);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x20, 0x30, 0x40), button.BackColor);

        XamlRuntime.ApplyStyle(button, null);

        Assert.AreEqual("Probe", button.Text);
        Assert.AreEqual(System.Windows.Forms.Control.DefaultBackColor, button.BackColor);
    }

    [TestMethod]
    public void NativeStyle_BasedOnOverridesSetters()
    {
        using var button = new Button { Text = "original" };

        var baseStyle = new Style { TargetType = typeof(Button) };
        baseStyle.Setters.Add(new Setter("Text", "base"));

        var derived = new Style { TargetType = typeof(Button), BasedOn = baseStyle };
        derived.Setters.Add(new Setter("Text", "derived"));

        XamlRuntime.ApplyStyle(button, derived);
        Assert.AreEqual("derived", button.Text);

        XamlRuntime.ApplyStyle(button, null);
        Assert.AreEqual("original", button.Text);
    }

    [TestMethod]
    public void NativeStyle_MutationReapplies()
    {
        using var button = new Button { Text = "original" };
        var style = new Style { TargetType = typeof(Button) };
        style.Setters.Add(new Setter("Text", "first"));

        XamlRuntime.ApplyStyle(button, style);
        Assert.AreEqual("first", button.Text);

        style.Setters[0] = new Setter("Text", "second");
        Assert.AreEqual("second", button.Text);

        style.Setters.Add(new Setter("BackColor", Color.Red));
        Assert.AreEqual(Color.Red, button.BackColor);

        style.Setters.RemoveAt(1);
        Assert.AreEqual(System.Windows.Forms.Control.DefaultBackColor, button.BackColor);

        XamlRuntime.ApplyStyle(button, null);
        Assert.AreEqual("original", button.Text);
    }

    [TestMethod]
    public void NativeStyle_WithAMissingProperty_Throws()
    {
        using var button = new Button();
        var style = new Style { TargetType = typeof(Button) };
        style.Setters.Add(new Setter("DoesNotExist", 1));

        Assert.ThrowsExactly<InvalidOperationException>(() => XamlRuntime.ApplyStyle(button, style));
    }

    [TestMethod]
    public void NativeStyle_TypeMismatch_Throws()
    {
        using var button = new Button();
        var style = new Style { TargetType = typeof(Label) };

        Assert.ThrowsExactly<InvalidOperationException>(() => XamlRuntime.ApplyStyle(button, style));
    }
}
