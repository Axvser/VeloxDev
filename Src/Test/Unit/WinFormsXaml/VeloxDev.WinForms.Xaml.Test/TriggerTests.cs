using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The trigger engine: enter/exit setters, restoration of the prior value, actions and what happens when a trigger
/// is removed while it is active.
/// </summary>
[STATestClass]
public sealed class TriggerTests
{
    [TestMethod]
    public void EnterAppliesSetters_ExitRestoresThePreviousLocalValue()
    {
        using var control = new ProbeControl { BorderWidth = 2 };
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        control.IsHot = false;
        Assert.AreEqual(2, control.BorderWidth);
        Assert.IsTrue(control.HasLocalValue(ProbeControl.BorderWidthProperty));
    }

    [TestMethod]
    public void ExitClearsTheSetterWhenThereWasNoPreviousLocalValue()
    {
        using var control = new ProbeControl();
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        control.IsHot = false;
        Assert.AreEqual(0, control.BorderWidth);
        Assert.IsFalse(control.HasLocalValue(ProbeControl.BorderWidthProperty));
    }

    [TestMethod]
    public void EnterAndExitActions_RunOnceWithTheOwningControl()
    {
        using var control = new ProbeControl();
        var enter = new RecordingTriggerAction();
        var exit = new RecordingTriggerAction();
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            EnterAction = enter,
            ExitAction = exit,
        });

        control.IsHot = true;
        control.IsHot = false;
        control.IsHot = false;

        Assert.AreEqual(1, enter.Invocations);
        Assert.AreEqual(1, exit.Invocations);
        Assert.AreSame(control, enter.LastControl);
        Assert.AreSame(control, exit.LastControl);
    }

    [TestMethod]
    public void EnterActionRunsAfterTheSetters_ExitActionRunsBeforeTheRestore()
    {
        using var control = new ProbeControl();
        var enter = new RecordingTriggerAction();
        var exit = new RecordingTriggerAction();
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
            EnterAction = enter,
            ExitAction = exit,
        });

        control.IsHot = true;
        Assert.AreEqual(4, enter.BorderWidthAtInvocation);

        control.IsHot = false;
        Assert.AreEqual(4, exit.BorderWidthAtInvocation, "exit runs while the setter value is still applied");
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void AddingATriggerWhoseConditionAlreadyHolds_AppliesItImmediately()
    {
        using var control = new ProbeControl { IsHot = true };

        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        Assert.AreEqual(4, control.BorderWidth);
    }

    [TestMethod]
    public void AChangedCondition_ExitsAndReenters()
    {
        using var control = new ProbeControl();
        control.Triggers.Add(new Trigger(ProbeControl.NumberProperty, 5)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });

        control.Number = 5;
        Assert.AreEqual(4, control.BorderWidth);

        control.Number = 6;
        Assert.AreEqual(0, control.BorderWidth);

        control.Number = 5;
        Assert.AreEqual(4, control.BorderWidth);
    }

    [TestMethod]
    public void IndependentTriggers_ApplyAndRestoreWithoutDisturbingEachOther()
    {
        using var control = new ProbeControl();
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        });
        control.Triggers.Add(new Trigger(ProbeControl.NumberProperty, 5)
        {
            Setters = { new Setter(ProbeControl.CaptionProperty, "five") },
        });

        control.IsHot = true;
        control.Number = 5;
        Assert.AreEqual(4, control.BorderWidth);
        Assert.AreEqual("five", control.Caption);

        control.IsHot = false;
        Assert.AreEqual(0, control.BorderWidth);
        Assert.AreEqual("five", control.Caption);

        control.Number = 6;
        Assert.AreEqual("caption", control.Caption);
    }

    [TestMethod]
    public void AnUnconfiguredTrigger_IsIgnored()
    {
        using var control = new ProbeControl();

        control.Triggers.Add(new Trigger());
        control.RefreshTriggers();

        Assert.IsFalse(control.HasLocalValue(ProbeControl.BorderWidthProperty));
    }

    [TestMethod]
    public void RemovingAnActiveTrigger_RestoresItsAppliedValues()
    {
        using var control = new ProbeControl();
        var trigger = new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
        };
        control.Triggers.Add(trigger);

        control.IsHot = true;
        Assert.AreEqual(4, control.BorderWidth);

        control.Triggers.Remove(trigger);

        Assert.AreEqual(0, control.BorderWidth, "removing an active trigger must run its exit path");
        Assert.IsFalse(control.HasLocalValue(ProbeControl.BorderWidthProperty));

        control.IsHot = false;
        Assert.AreEqual(0, control.BorderWidth, "the removed trigger is no longer evaluated at all");
    }
}
