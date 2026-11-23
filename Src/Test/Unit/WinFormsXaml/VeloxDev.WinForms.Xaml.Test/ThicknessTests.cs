using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The shared <see cref="Thickness"/> value type: parsing, equality, uniform conversions and the dependency properties
/// the visual controls carry it on.
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
    public void VisualBorder_CarriesPaddingAndBorderThicknessAsDependencyProperties()
    {
        using var border = new VC.Border();

        Assert.AreEqual(Thickness.Empty, border.BorderThickness);

        border.Padding = new Thickness(1f, 2f, 3f, 4f);
        border.BorderThickness = new Thickness(1f, 2f, 3f, 4f);

        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), border.Padding);
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), border.BorderThickness);
        Assert.AreEqual(
            new Thickness(1f, 2f, 3f, 4f),
            (Thickness)border.GetValue(VC.Border.BorderThicknessProperty)!);

        border.ClearValue(VC.Border.BorderThicknessProperty);

        Assert.AreEqual(Thickness.Empty, border.BorderThickness);
        Assert.AreEqual(new Thickness(1f, 2f, 3f, 4f), border.Padding, "clearing one property leaves the other alone");
    }
}
