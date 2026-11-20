using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Implicit styles: type-keyed resources found by exact runtime type, explicit styles winning, and resource/tree
/// changes applying or unapplying them.
/// </summary>
[STATestClass]
public sealed class ImplicitStyleTests
{
    [TestMethod]
    public void ImplicitStyle_AppliesToAControlAddedToTheTree()
    {
        using var view = new GeneratedView();
        var explicitCard = view.Controls.OfType<GeneratedCard>().Single();
        Assert.AreEqual(3, explicitCard.StyleValue, "the explicit style wins over the implicit style");

        using var implicitCard = new GeneratedCard();
        view.Controls.Add(implicitCard);

        Assert.IsNotNull(implicitCard.Style);
        Assert.AreSame(view.Resources[typeof(GeneratedCard)], implicitCard.Style);
        Assert.AreEqual(11, implicitCard.StyleValue);
    }

    [TestMethod]
    public void ImplicitStyle_ExplicitStyleWinsAndFallsBackWhenCleared()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);
        Assert.AreEqual(11, card.StyleValue);

        card.Style = (Style)view.Resources["CardStyle"]!;
        Assert.AreEqual(2, card.StyleValue);

        card.Style = null;
        Assert.AreEqual(11, card.StyleValue, "clearing the explicit style falls back to the implicit style");
    }

    [TestMethod]
    public void ImplicitStyle_ReplacingOrRemovingTheResourceReapplies()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);
        Assert.AreEqual(11, card.StyleValue);

        var replacement = new Style { TargetType = typeof(GeneratedCard) };
        replacement.Setters.Add(new Setter(GeneratedCard.StyleValueProperty, 21));

        view.Resources[typeof(GeneratedCard)] = replacement;

        Assert.AreSame(replacement, card.Style);
        Assert.AreEqual(21, card.StyleValue);

        view.Resources.Remove(typeof(GeneratedCard));

        Assert.IsNull(card.Style);
        Assert.AreEqual(0, card.StyleValue);
    }

    [TestMethod]
    public void ImplicitStyle_RemovingTheControlFromTheTreeUnapplies()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);
        Assert.AreEqual(11, card.StyleValue);

        view.Controls.Remove(card);

        Assert.IsNull(card.Style);
        Assert.AreEqual(0, card.StyleValue);
    }

    [TestMethod]
    public void ImplicitStyle_MatchesTheExactRuntimeType()
    {
        using var view = new GeneratedView();
        using var derived = new DerivedCard();
        view.Controls.Add(derived);

        Assert.IsNull(derived.Style, "a style keyed by the base type must not apply implicitly to a derived type");
        Assert.AreEqual(0, derived.StyleValue);
    }

    [TestMethod]
    public void ImplicitStyle_DataAndMultiTriggersFollowTheDataContext()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);
        var model = new GeneratedModel();
        card.DataContext = model;

        Assert.AreEqual(11, card.StyleValue);
        Assert.AreEqual("data", card.Note, "the DataTrigger on Title is active");

        card.IsHot = true;
        card.IsPinned = true;
        Assert.AreEqual("multi", card.Note, "the later MultiTrigger wins while both conditions hold");

        card.IsPinned = false;
        Assert.AreEqual("data", card.Note, "exiting the MultiTrigger reveals the DataTrigger");

        model.Column = 5;
        Assert.AreEqual(15, card.StyleValue, "DataTrigger Value=\"5\" converts to the bound int");

        model.Column = 6;
        Assert.AreEqual(11, card.StyleValue);

        model.Title = "other";
        Assert.IsNull(card.Note, "the DataTrigger on Title exits when the bound value changes");
    }

    [TestMethod]
    public void ImplicitStyle_RemovalExitsActiveDataTriggers()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);
        card.DataContext = new GeneratedModel();
        Assert.AreEqual("data", card.Note);

        view.Resources.Remove(typeof(GeneratedCard));

        Assert.IsNull(card.Style);
        Assert.IsNull(card.Note);
        Assert.AreEqual(0, card.StyleValue);
    }
}
