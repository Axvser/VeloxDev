using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The content-template subset: a style's <see cref="ControlTemplate"/> creates content under the target, receives
/// the target's data context and is removed/disposed when the style leaves.
/// </summary>
[STATestClass]
public sealed class ControlTemplateTests
{
    [TestMethod]
    public void TemplatedStyle_CreatesContentAndPropagatesTheDataContext()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        var model = new GeneratedModel { Title = "templated title" };
        card.DataContext = model;

        card.Style = (Style)view.Resources["TemplatedCardStyle"]!;

        Assert.AreEqual(31, card.StyleValue);
        var chrome = card.Controls.OfType<CardChrome>().Single();
        Assert.AreEqual("templated title", chrome.Label);

        model.Title = "changed title";
        Assert.AreEqual("changed title", chrome.Label);
    }

    [TestMethod]
    public void RemovingTheStyleRemovesAndDisposesTheTemplateContent()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        card.Style = (Style)view.Resources["TemplatedCardStyle"]!;
        var chrome = card.Controls.OfType<CardChrome>().Single();

        card.Style = null;

        Assert.IsEmpty(card.Controls.OfType<CardChrome>());
        Assert.IsTrue(chrome.IsDisposed);
        Assert.IsNull(card.Style);
    }

    [TestMethod]
    public void ReplacingTheStyleReplacesTheTemplateContent()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        var templated = (Style)view.Resources["TemplatedCardStyle"]!;
        var plain = (Style)view.Resources["CardStyle"]!;

        card.Style = templated;
        var first = card.Controls.OfType<CardChrome>().Single();

        card.Style = plain;

        Assert.IsEmpty(card.Controls.OfType<CardChrome>());
        Assert.IsTrue(first.IsDisposed);

        card.Style = templated;
        Assert.HasCount(1, card.Controls.OfType<CardChrome>());
    }

    [TestMethod]
    public void TemplateDataContextFollowsTheTargetContext()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        card.Style = (Style)view.Resources["TemplatedCardStyle"]!;
        var chrome = card.Controls.OfType<CardChrome>().Single();

        card.DataContext = new GeneratedModel { Title = "first" };
        Assert.AreEqual("first", chrome.Label);

        card.DataContext = new GeneratedModel { Title = "second" };
        Assert.AreEqual("second", chrome.Label);
    }

    [TestMethod]
    public void TemplateContent_FillsTheTemplatedControl()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        card.Style = (Style)view.Resources["TemplatedCardStyle"]!;

        var chrome = card.Controls.OfType<CardChrome>().Single();

        Assert.AreEqual(System.Windows.Forms.DockStyle.Fill, chrome.Dock);
    }

    [TestMethod]
    public void ControlTemplateResource_CreatesAFreshContentTree()
    {
        using var view = new GeneratedView();
        var template = (ControlTemplate)view.Resources["CardTemplate"]!;

        using var first = template.CreateContent();
        using var second = template.CreateContent();

        Assert.IsInstanceOfType<CardChrome>(first);
        Assert.IsInstanceOfType<CardChrome>(second);
        Assert.AreNotSame(first, second);
    }
}
