using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WinForms <see cref="Thickness"/> value type and the dependency properties that carry it: parsing, equality,
/// native conversion, and the one-way sync into the native WinForms layout properties.
/// </summary>
[STATestClass]
public sealed class ThicknessTests
{
    [TestMethod]
    public void Parse_AcceptsOneTwoAndFourNumbers()
    {
        Assert.AreEqual(new Thickness(8f), Thickness.Parse("8"));
        Assert.AreEqual(new Thickness(8f, 4f), Thickness.Parse("8,4"));
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), Thickness.Parse("1,2,3,4"));
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), Thickness.Parse(" 1, 2, 3, 4 "));
    }

    [TestMethod]
    public void Parse_RejectsOtherComponentCountsAndNonNumbers()
    {
        Assert.ThrowsExactly<FormatException>(() => Thickness.Parse("1,2,3"));
        Assert.ThrowsExactly<FormatException>(() => Thickness.Parse(string.Empty));
        Assert.ThrowsExactly<FormatException>(() => Thickness.Parse("wide"));
    }

    [TestMethod]
    public void Equality_ComparesEverySide()
    {
        var thickness = new Thickness(1f, 2f, 3f, 4f);

        Assert.AreEqual(thickness, new Thickness(1f, 2f, 3f, 4f));
        Assert.IsTrue(thickness == new Thickness(1f, 2f, 3f, 4f));
        Assert.IsTrue(thickness != new Thickness(1f, 2f, 3f, 5f));
        Assert.AreNotEqual(thickness, new Thickness(1f));
        Assert.AreEqual(thickness.GetHashCode(), new Thickness(1f, 2f, 3f, 4f).GetHashCode());
        Assert.AreEqual("1,2,3,4", thickness.ToString());
    }

    [TestMethod]
    public void ToPadding_RoundsEachSideAwayFromZero()
    {
        Assert.AreEqual(new Padding(1, 2, 3, 4), new Thickness(1f, 2f, 3f, 4f).ToPadding());
        Assert.AreEqual(new Padding(1, 2, 3, 4), new Thickness(0.5f, 1.5f, 2.5f, 3.5f).ToPadding());
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), Thickness.FromPadding(new Padding(1, 2, 3, 4)));
    }

    [TestMethod]
    public void UniformConversions_CreateAThicknessForAllSides()
    {
        Thickness fromInt = 5;
        Thickness fromFloat = 5f;
        Thickness fromDouble = 5d;

        Assert.AreEqual(new Thickness(5f), fromInt);
        Assert.AreEqual(new Thickness(5f), fromFloat);
        Assert.AreEqual(new Thickness(5f), fromDouble);
    }

    [TestMethod]
    public void PaddingProperty_HasTheEmptyDefaultAndWritesTheNativePadding()
    {
        using var control = new ProbeControl();

        Assert.AreEqual(Thickness.Empty, (Thickness)control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty)!);
        Assert.AreEqual(Padding.Empty, ((System.Windows.Forms.ScrollableControl)control).Padding);

        control.Padding = new Thickness(1f, 2f, 3f, 4f);

        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), (Thickness)control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty)!);
        Assert.AreEqual(new Padding(1, 2, 3, 4), ((System.Windows.Forms.ScrollableControl)control).Padding);

        control.ClearValue(VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty);

        Assert.AreEqual(Thickness.Empty, (Thickness)control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty)!);
        Assert.AreEqual(Padding.Empty, ((System.Windows.Forms.ScrollableControl)control).Padding);
    }

    [TestMethod]
    public void MarginProperty_DefaultsToTheNativeMarginAndWritesTheNativeMargin()
    {
        using var control = new ProbeControl();

        // Control.DefaultMargin 是 3：DP 默认值与原生布局的起点一致。
        Assert.AreEqual(new Thickness(3f), (Thickness)control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.MarginProperty)!);
        Assert.AreEqual(new Padding(3), ((System.Windows.Forms.Control)control).Margin);

        control.Margin = new Thickness(6f, 8f, 10f, 12f);

        Assert.AreEqual(new Padding(6, 8, 10, 12), ((System.Windows.Forms.Control)control).Margin);

        control.ClearValue(VeloxDev.WinForms.Xaml.Controls.Control.MarginProperty);

        Assert.AreEqual(new Padding(3), ((System.Windows.Forms.Control)control).Margin);
    }

    [TestMethod]
    public void UnsetProperties_ReadAValueWrittenThroughTheNativeBase()
    {
        using var control = new ProbeControl();

        ((System.Windows.Forms.Control)control).Margin = new Padding(7);
        ((System.Windows.Forms.ScrollableControl)control).Padding = new Padding(9);

        Assert.AreEqual(new Thickness(7f), control.Margin);
        Assert.AreEqual(new Thickness(9f), control.Padding);
    }

    [TestMethod]
    public void AttachedPadding_AlsoWritesTheNativePaddingOnANativeControl()
    {
        using var label = new Label();

        VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty.SetValue(label, new Thickness(2f, 4f, 6f, 8f));

        Assert.AreEqual(new Padding(2, 4, 6, 8), label.Padding);
        Assert.AreEqual(
            new Thickness(2f, 4f, 6f, 8f),
            (Thickness)VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty.GetValue(label)!);

        VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty.ClearValue(label);

        Assert.AreEqual(Padding.Empty, label.Padding);
    }

    [TestMethod]
    public void BorderThickness_IsAThicknessProperty()
    {
        using var border = new Border();

        Assert.AreEqual(Thickness.Empty, border.BorderThickness);

        border.BorderThickness = new Thickness(1f, 2f, 3f, 4f);

        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), border.BorderThickness);
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), (Thickness)border.GetValue(Border.BorderThicknessProperty)!);
    }
}
