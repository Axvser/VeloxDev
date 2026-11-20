using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Explicit <c>Style="{StaticResource Key}"</c> assignments follow their resource: replacement reapplies, removal
/// clears the explicit style, and a later direct <c>Style</c> assignment stops the tracking.
/// </summary>
[STATestClass]
public sealed class ExplicitStyleResourceTests
{
    [TestMethod]
    public void ReplacingAnExplicitStyleResourceReapplies()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        Assert.AreEqual(3, card.StyleValue);

        var replacement = new Style { TargetType = typeof(GeneratedCard) };
        replacement.Setters.Add(new Setter(GeneratedCard.StyleValueProperty, 21));

        view.Resources["ChildCardStyle"] = replacement;

        Assert.AreSame(replacement, card.Style);
        Assert.AreEqual(21, card.StyleValue);
    }

    [TestMethod]
    public void RemovingAnExplicitStyleResourceClearsTheExplicitStyle()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        Assert.AreEqual(3, card.StyleValue);

        view.Resources.Remove("ChildCardStyle");

        Assert.AreEqual(11, card.StyleValue, "the implicit style takes over");
        Assert.AreSame(view.Resources[typeof(GeneratedCard)], card.Style);
    }

    [TestMethod]
    public void ReAddingAnExplicitStyleResourceReapplies()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        var original = (Style)view.Resources["ChildCardStyle"]!;

        view.Resources.Remove("ChildCardStyle");
        Assert.AreEqual(11, card.StyleValue);

        view.Resources["ChildCardStyle"] = original;

        Assert.AreSame(original, card.Style);
        Assert.AreEqual(3, card.StyleValue);
    }

    [TestMethod]
    public void ADirectStyleAssignmentStopsTrackingTheResource()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();

        var direct = new Style { TargetType = typeof(GeneratedCard) };
        direct.Setters.Add(new Setter(GeneratedCard.StyleValueProperty, 31));
        card.Style = direct;
        Assert.AreEqual(31, card.StyleValue);

        var replacement = new Style { TargetType = typeof(GeneratedCard) };
        replacement.Setters.Add(new Setter(GeneratedCard.StyleValueProperty, 41));
        view.Resources["ChildCardStyle"] = replacement;

        Assert.AreSame(direct, card.Style);
        Assert.AreEqual(31, card.StyleValue, "a direct Style assignment detaches the resource tracking");
    }

    [TestMethod]
    public void NativeExplicitStyleResourceReplacementAndRemoval()
    {
        using var view = new GeneratedView();
        var button = view.Controls.OfType<Button>().Single();
        Assert.AreEqual("Styled probe", button.Text);

        var replacement = new Style { TargetType = typeof(Button) };
        replacement.Setters.Add(new Setter("Text", "replacement"));
        view.Resources["NativeButtonStyle"] = replacement;
        Assert.AreEqual("replacement", button.Text);

        view.Resources.Remove("NativeButtonStyle");
        Assert.AreEqual("Probe", button.Text);
    }
}
