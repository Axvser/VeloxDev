using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Style mutation auto-refresh: changing setters, triggers, BasedOn or Template re-applies the style to every live
/// control it is applied to, using weak target references.
/// </summary>
[STATestClass]
public sealed class StyleMutationTests
{
    [TestMethod]
    public void MutatingSettersReappliesTheStyle()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        control.Style = style;
        Assert.AreEqual(2, control.BorderWidth);

        style.Setters.Add(new Setter(ProbeControl.CaptionProperty, "added"));
        Assert.AreEqual("added", control.Caption);

        style.Setters[0] = new Setter(ProbeControl.BorderWidthProperty, 3);
        Assert.AreEqual(3, control.BorderWidth);

        style.Setters.RemoveAt(1);
        Assert.AreEqual("caption", control.Caption);
    }

    [TestMethod]
    public void MutatingTriggersReappliesAndEntersOrExits()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        control.Style = style;
        control.IsHot = true;
        Assert.AreEqual(2, control.BorderWidth);

        style.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        Assert.AreEqual(4, control.BorderWidth, "adding an already-active style trigger enters it");

        style.Triggers.Clear();

        Assert.AreEqual(2, control.BorderWidth, "clearing the trigger exits it and reveals the style setter");
    }

    [TestMethod]
    public void MutatingBasedOnReappliesTheStyle()
    {
        using var control = new ProbeControl();
        var baseStyle = new Style { TargetType = typeof(ProbeControl) };
        baseStyle.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));

        var derived = new Style { TargetType = typeof(ProbeControl) };
        control.Style = derived;
        Assert.AreEqual(0, control.BorderWidth);

        derived.BasedOn = baseStyle;

        Assert.AreEqual(2, control.BorderWidth);
    }

    [TestMethod]
    public void MutatingTargetTypeToAMismatchedType_IsRejected()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        control.Style = style;

        Assert.ThrowsExactly<InvalidOperationException>(() => style.TargetType = typeof(Button));

        Assert.AreEqual(2, control.BorderWidth, "the previous style stays applied when validation fails");
    }

    [TestMethod]
    public void MutatingTemplateReappliesAndDisposesContent()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        control.Style = style;

        style.Template = new ControlTemplate(() => new CardChrome());

        var chrome = control.Controls.OfType<CardChrome>().Single();

        style.Template = null;

        Assert.IsEmpty(control.Controls.OfType<CardChrome>());
        Assert.IsTrue(chrome.IsDisposed);
    }

    [TestMethod]
    public void ADisposedControlDoesNotReceiveMutationUpdates()
    {
        var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        control.Style = style;
        control.Dispose();

        style.Setters.Add(new Setter(ProbeControl.CaptionProperty, "after dispose"));

        Assert.IsTrue(control.IsDisposed);
    }
}
