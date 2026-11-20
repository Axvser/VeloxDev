using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Custom <see cref="TriggerAction"/> types declared in XAML: construction, property assignment, invocation on a
/// control trigger and on a style trigger.
/// </summary>
[STATestClass]
public sealed class CustomActionTests
{
    [TestMethod]
    public void CustomActionOnAControlTrigger_RunsEnterAndExit()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();

        var trigger = card.Triggers.Single(
            candidate => ReferenceEquals(candidate.Property, GeneratedCard.IsPinnedProperty));

        var enter = (ConfigurableRecordingAction)trigger.EnterAction!;
        var exit = (ConfigurableRecordingAction)trigger.ExitAction!;

        Assert.AreEqual("entered", enter.Tag);
        Assert.AreEqual(3, enter.Amount);
        Assert.AreEqual("exited", exit.Tag);
        Assert.AreEqual(4, exit.Amount);

        card.IsPinned = true;
        Assert.AreEqual(1, enter.Invocations);
        Assert.AreSame(card, enter.LastControl);

        card.IsPinned = false;
        Assert.AreEqual(1, exit.Invocations);

        card.IsPinned = true;
        Assert.AreEqual(2, enter.Invocations);
    }

    [TestMethod]
    public void CustomActionOnAStyleTrigger_RunsAndGetsAStaticResourceProperty()
    {
        using var view = new GeneratedView();
        using var card = new GeneratedCard();
        view.Controls.Add(card);

        var style = card.Style;
        Assert.IsNotNull(style);

        var trigger = style.Triggers.Single(
            candidate => ReferenceEquals(candidate.Property, GeneratedCard.IsPinnedProperty));
        var enter = (ConfigurableRecordingAction)trigger.EnterAction!;

        Assert.AreEqual("style-resource", enter.Tag);
        Assert.AreEqual(5, enter.Amount);

        card.IsPinned = true;

        Assert.AreEqual(1, enter.Invocations);
        Assert.AreSame(card, enter.LastControl);
    }
}
