using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The style value layers: style setters below local values, style triggers above style setters, BasedOn precedence
/// and style removal exiting active triggers.
/// </summary>
[STATestClass]
public sealed class StyleTests
{
    [TestMethod]
    public void StyleSetterApplies_LocalWinsAndClearingRevealsTheStyleValue()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));

        control.Style = style;

        Assert.AreEqual(2, control.BorderWidth);
        Assert.IsFalse(control.HasLocalValue(ProbeControl.BorderWidthProperty));

        control.BorderWidth = 9;
        Assert.AreEqual(9, control.BorderWidth);

        control.ClearValue(ProbeControl.BorderWidthProperty);
        Assert.AreEqual(2, control.BorderWidth);
    }

    [TestMethod]
    public void RemovingTheStyleRestoresTheLocalValueOrDefault()
    {
        using var withLocal = new ProbeControl { BorderWidth = 7 };
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));

        withLocal.Style = style;
        Assert.AreEqual(7, withLocal.BorderWidth, "a style setter must not overwrite a local value");

        withLocal.Style = null;
        Assert.AreEqual(7, withLocal.BorderWidth);

        using var withoutLocal = new ProbeControl();
        withoutLocal.Style = style;
        Assert.AreEqual(2, withoutLocal.BorderWidth);

        withoutLocal.Style = null;
        Assert.AreEqual(0, withoutLocal.BorderWidth);
        Assert.IsFalse(withoutLocal.HasLocalValue(ProbeControl.BorderWidthProperty));
    }

    [TestMethod]
    public void StyleTriggerEntersExitsAndIsExitedWhenTheStyleIsRemoved()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        style.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        control.Style = style;
        Assert.AreEqual(2, control.BorderWidth);

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth, "a style trigger is above a style setter");

        control.IsHot = false;
        Assert.AreEqual(2, control.BorderWidth);

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        control.Style = null;
        Assert.AreEqual(0, control.BorderWidth, "unapplying the style must exit an active style trigger and remove its setter value");
        Assert.IsFalse(control.HasLocalValue(ProbeControl.BorderWidthProperty));
    }

    [TestMethod]
    public void StyleTriggerDoesNotOverrideALocalValue()
    {
        using var control = new ProbeControl { BorderWidth = 9 };
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        control.Style = style;
        control.IsHot = true;

        Assert.AreEqual(9, control.BorderWidth, "local value > style trigger");

        control.ClearValue(ProbeControl.BorderWidthProperty);
        Assert.AreEqual(4, control.BorderWidth, "clearing the local value reveals the active style trigger");
    }

    [TestMethod]
    public void BasedOnAppliesBaseSettersFirst_DerivedOverrides_AndBothTriggerSetsStayActive()
    {
        using var control = new ProbeControl();

        var baseStyle = new Style { TargetType = typeof(ProbeControl) };
        baseStyle.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        baseStyle.Setters.Add(new Setter(ProbeControl.CaptionProperty, "base"));
        baseStyle.Triggers.Add(new Trigger(ProbeControl.NumberProperty, 5)
        {
            Setters = { new Setter(ProbeControl.CaptionProperty, "base-active") },
        });

        var derived = new Style { TargetType = typeof(ProbeControl), BasedOn = baseStyle };
        derived.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 3));
        derived.Triggers.Add(new Trigger(ProbeControl.NumberProperty, 5)
        {
            Setters = { new Setter(ProbeControl.GlowProperty, 0.5d) },
        });

        control.Style = derived;

        Assert.AreEqual(3, control.BorderWidth, "the derived setter overrides the base setter");
        Assert.AreEqual("base", control.Caption);

        control.Number = 5;

        Assert.AreEqual("base-active", control.Caption, "the base trigger is still active");
        Assert.AreEqual(0.5d, control.Glow, "the derived trigger is active too");

        control.Number = 6;

        Assert.AreEqual("base", control.Caption);
        Assert.AreEqual(0d, control.Glow);
        Assert.AreEqual(3, control.BorderWidth);
    }

    [TestMethod]
    public void DerivedStyleTriggerWinsOverABaseStyleTriggerForTheSameProperty()
    {
        using var control = new ProbeControl();

        var baseStyle = new Style { TargetType = typeof(ProbeControl) };
        baseStyle.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        baseStyle.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        var derived = new Style { TargetType = typeof(ProbeControl), BasedOn = baseStyle };
        derived.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 3));
        derived.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 6) },
        });

        control.Style = derived;
        Assert.AreEqual(3, control.BorderWidth);

        control.IsHot = true;
        Assert.AreEqual(6, control.BorderWidth, "the derived style trigger wins for the same property");

        control.IsHot = false;
        Assert.AreEqual(3, control.BorderWidth);
    }

    [TestMethod]
    public void ApplyAndUnapply_AreEquivalentToAssigningTheStyleProperty()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));

        style.Apply(control);

        Assert.AreSame(style, control.Style);
        Assert.AreEqual(2, control.BorderWidth);

        style.Unapply(control);

        Assert.IsNull(control.Style);
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void ExitingADerivedStyleTriggerRevealsTheBaseStyleTrigger()
    {
        using var control = new ProbeControl();

        var baseStyle = new Style { TargetType = typeof(ProbeControl) };
        baseStyle.Triggers.Add(new Trigger(ProbeControl.NumberProperty, 5)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 2) },
        });

        var derived = new Style { TargetType = typeof(ProbeControl), BasedOn = baseStyle };
        derived.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 6) },
        });

        control.Style = derived;

        control.Number = 5;
        Assert.AreEqual(2, control.BorderWidth, "the base style trigger is active");

        control.IsHot = true;
        Assert.AreEqual(6, control.BorderWidth, "the derived style trigger wins");

        control.IsHot = false;
        Assert.AreEqual(2, control.BorderWidth, "exiting the derived trigger reveals the base trigger");

        control.Number = 6;
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void ReplacingAStyle_ExitsTheOldStylesActiveTrigger()
    {
        using var control = new ProbeControl();

        var first = new Style { TargetType = typeof(ProbeControl) };
        first.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));
        first.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        var second = new Style { TargetType = typeof(ProbeControl) };
        second.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 3));

        control.Style = first;
        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        control.Style = second;

        Assert.AreEqual(
            3,
            control.BorderWidth,
            "replacing the style must exit the old trigger before applying the new setter");
    }

    [TestMethod]
    public void AStyleForAnotherControlType_IsRejected()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(System.Windows.Forms.Button) };

        Assert.ThrowsExactly<InvalidOperationException>(() => control.Style = style);
    }

    [TestMethod]
    public void ABasedOnCycle_IsRejected()
    {
        using var control = new ProbeControl();
        var first = new Style { TargetType = typeof(ProbeControl) };
        var second = new Style { TargetType = typeof(ProbeControl), BasedOn = first };
        first.BasedOn = second;

        Assert.ThrowsExactly<InvalidOperationException>(() => control.Style = second);
    }

    [TestMethod]
    public void GeneratedStyle_AppliesBasedOnSettersAndTriggers()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        XamlTestSurface.EnsureHandle(card);

        Assert.AreEqual(3, card.StyleValue, "the derived style setter overrides the base setter");

        card.IsHot = true;
        Assert.AreEqual(5, card.StyleValue, "the derived style trigger wins over the base style trigger");

        card.IsHot = false;
        Assert.AreEqual(3, card.StyleValue);
    }

    [TestMethod]
    public void GeneratedStyle_LocalValueWinsAndClearingRevealsTheStyleTrigger()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        XamlTestSurface.EnsureHandle(card);

        card.StyleValue = 9;
        Assert.AreEqual(9, card.StyleValue);

        card.IsHot = true;
        Assert.AreEqual(9, card.StyleValue, "local value > style trigger");

        card.ClearValue(GeneratedCard.StyleValueProperty);
        Assert.AreEqual(5, card.StyleValue, "clearing the local value reveals the style trigger");

        card.IsHot = false;
        Assert.AreEqual(3, card.StyleValue);
    }

    [TestMethod]
    public void GeneratedStyle_RemovalExitsTriggersAndRestoresDefault()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        XamlTestSurface.EnsureHandle(card);

        card.IsHot = true;
        Assert.AreEqual(5, card.StyleValue);

        card.Style = null;

        Assert.AreEqual(11, card.StyleValue, "removing the explicit style reveals the implicit style");

        view.Resources.Remove(typeof(GeneratedCard));

        Assert.AreEqual(0, card.StyleValue);
        Assert.IsFalse(card.HasLocalValue(GeneratedCard.StyleValueProperty));
    }

    [TestMethod]
    public void GeneratedBaseStyle_CanBeAppliedOnItsOwn()
    {
        using var view = new GeneratedView();
        var style = (Style)view.Resources["CardStyle"]!;
        using var card = new GeneratedCard { Style = style };

        Assert.AreEqual(2, card.StyleValue);

        card.IsHot = true;
        Assert.AreEqual(4, card.StyleValue);
    }
}
