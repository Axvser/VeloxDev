using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Nested trigger mutations: changing a trigger's setters, a condition's value or a MultiTrigger's condition set
/// while the trigger is active re-applies the new state instead of leaving stale values behind.
/// </summary>
[STATestClass]
public sealed class NestedTriggerMutationTests
{
    [TestMethod]
    public void MutatingADirectTriggersSettersReappliesWhileActive()
    {
        using var control = new ProbeControl();
        var trigger = new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        };
        control.Triggers.Add(trigger);
        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        trigger.Setters[0].Value = 6;
        Assert.AreEqual(6, control.BorderWidth, "the old setter value must be restored before the new one is applied");

        trigger.Setters.Add(new Setter(ProbeControl.CaptionProperty, "mutated"));
        Assert.AreEqual("mutated", control.Caption);

        trigger.Setters.RemoveAt(0);
        Assert.AreEqual(0, control.BorderWidth);

        control.IsHot = false;
        Assert.AreEqual("caption", control.Caption);
    }

    [TestMethod]
    public void MutatingADirectMultiTriggersConditionsReapplies()
    {
        using var control = new ProbeControl();
        var trigger = new MultiTrigger();
        var numberCondition = new Condition(ProbeControl.NumberProperty, 7);
        trigger.Conditions.Add(new Condition(ProbeControl.IsHotProperty, true));
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        trigger.Conditions.Add(numberCondition);
        Assert.AreEqual(4, control.BorderWidth, "the new condition holds (Number defaults to 7)");

        numberCondition.Value = 8;
        Assert.AreEqual(0, control.BorderWidth, "a failing condition exits the trigger");

        numberCondition.Value = 7;
        Assert.AreEqual(4, control.BorderWidth, "a passing condition re-enters the trigger");

        trigger.Conditions.Remove(numberCondition);
        Assert.AreEqual(4, control.BorderWidth);

        control.IsHot = false;
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void MutatingAStyleTriggersNestedStateReapplies()
    {
        using var control = new ProbeControl();
        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 2));

        var trigger = new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        };
        style.Triggers.Add(trigger);

        control.Style = style;
        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        trigger.Setters[0] = new Setter(ProbeControl.BorderWidthProperty, 6);
        Assert.AreEqual(6, control.BorderWidth, "the style re-applies the mutated nested setter");

        trigger.Value = false;
        Assert.AreEqual(2, control.BorderWidth, "a mutated condition exits the style trigger");

        trigger.Value = true;
        Assert.AreEqual(6, control.BorderWidth);

        trigger.Setters.Clear();
        Assert.AreEqual(2, control.BorderWidth, "the style trigger is active but has no setter left");

        control.IsHot = false;
        Assert.AreEqual(2, control.BorderWidth);
    }
}
