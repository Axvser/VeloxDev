using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using Thickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The bridge from a <see cref="DependencyProperty"/> to the WinForms transition system. These tests need a real
/// control handle and a pumped message queue: the host posts every frame through <c>Control.BeginInvoke</c>.
/// </summary>
[STATestClass]
public sealed class AnimationTests
{
    private static TransitionEffect ShortEffect(int milliseconds = 180) => new()
    {
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        Ease = Eases.Cubic.Out,
    };

    [TestMethod]
    public void Animate_DrivesTheDependencyPropertyToTheEndValue()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(control, ProbeControl.GlowProperty, 1d, ShortEffect());

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => control.Glow >= 1d - 1e-6),
            $"Glow never reached 1; last value {control.Glow}");

        Assert.AreEqual(1d, control.Glow, 1e-6);
        Assert.IsTrue(control.HasLocalValue(ProbeControl.GlowProperty));
    }

    [TestMethod]
    public void Animate_PassesThroughIntermediateFrames()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(control, ProbeControl.GlowProperty, 1d, ShortEffect(300));

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => control.GlowValues.Any(static value => value > 0.01 && value < 0.99)),
            $"no intermediate frame; recorded: {string.Join(", ", control.GlowValues)}");
    }

    [TestMethod]
    public void Animate_FromALowerStartingValue_DrivesBothEnds()
    {
        using var control = new ProbeControl { Glow = 0.25 };
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(control, ProbeControl.GlowProperty, 0.75d, ShortEffect(160));

        Assert.IsTrue(XamlTestSurface.PumpUntil(() => control.Glow >= 0.75d - 1e-6));
        Assert.AreEqual(0.75d, control.Glow, 1e-6);
    }

    [TestMethod]
    public void AnimateKeyFrames_ReachesTheEndAndPassesThroughTheMiddle()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var frames = new[]
        {
            new KeyFrame { Offset = 0d, Value = 0d },
            new KeyFrame { Offset = 0.5d, Value = 0.5d },
            new KeyFrame { Offset = 1d, Value = 1d },
        };

        XamlTransition.AnimateKeyFrames(
            control,
            ProbeControl.GlowProperty,
            frames,
            TimeSpan.FromMilliseconds(300),
            Eases.Cubic.Out);

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => control.Glow >= 0.999d),
            $"Glow never reached 1; last value {control.Glow}");
        Assert.IsTrue(
            control.GlowValues.Any(static value => value > 0.2d && value < 0.8d),
            "the keyframe chain never produced a middle frame");
    }

    [TestMethod]
    public void KeyFrameAnimation_InvokesTheSameChain()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var action = new KeyFrameAnimation
        {
            Property = ProbeControl.GlowProperty,
            Duration = TimeSpan.FromMilliseconds(240),
            Ease = Eases.Cubic.Out,
        };
        action.KeyFrames.Add(new KeyFrame { Offset = 0d, Value = 0d });
        action.KeyFrames.Add(new KeyFrame { Offset = 0.5d, Value = 0.5d });
        action.KeyFrames.Add(new KeyFrame { Offset = 1d, Value = 1d });

        action.Invoke(control);

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => control.Glow >= 0.999d),
            $"Glow never reached 1; last value {control.Glow}");
    }

    [TestMethod]
    public void Stop_EndsTheRunWhereItStands()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(control, ProbeControl.GlowProperty, 1d, ShortEffect(800));
        Assert.IsTrue(XamlTestSurface.PumpUntil(() => control.Glow > 0.02), "the run never produced a frame");

        var stoppedAt = control.Glow;
        XamlTransition.Stop(control);
        XamlTestSurface.Pump(120);

        Assert.AreEqual(stoppedAt, control.Glow, 1e-6);
        Assert.IsLessThan(1d, control.Glow);
    }

    [TestMethod]
    public void TransitionAction_InvokesTheSameAnimation()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var action = new TransitionAction
        {
            Property = ProbeControl.GlowProperty,
            To = 0.5d,
            Effect = ShortEffect(160),
        };

        action.Invoke(control);

        Assert.IsTrue(XamlTestSurface.PumpUntil(() => control.Glow >= 0.5d - 1e-6));
        Assert.AreEqual(0.5d, control.Glow, 1e-6);
    }

    [TestMethod]
    public void TransitionAction_WithoutAProperty_IsRejected()
    {
        using var control = new ProbeControl();

        Assert.ThrowsExactly<InvalidOperationException>(() => new TransitionAction { To = 1d }.Invoke(control));
    }

    [TestMethod]
    public void TriggerEnterAndExitActions_DriveTheAnimationBothWays()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        control.Triggers.Add(new Trigger(ProbeControl.IsHotProperty, true)
        {
            Setters = { new Setter(ProbeControl.BorderWidthProperty, 4) },
            EnterAction = new TransitionAction
            {
                Property = ProbeControl.GlowProperty,
                To = 1d,
                Effect = ShortEffect(180),
            },
            ExitAction = new TransitionAction
            {
                Property = ProbeControl.GlowProperty,
                To = 0d,
                Effect = ShortEffect(180),
            },
        });

        control.IsHot = true;
        Assert.IsTrue(XamlTestSurface.PumpUntil(() => control.Glow >= 1d - 1e-6), "enter animation did not finish");
        Assert.AreEqual(4, control.BorderWidth);

        control.IsHot = false;
        Assert.IsTrue(XamlTestSurface.PumpUntil(() => control.Glow <= 1e-6), "exit animation did not finish");
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void Animate_Margin_DrivesTheDependencyPropertyAndTheNativeMargin()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(
            control,
            VeloxDev.WinForms.Xaml.Controls.Control.MarginProperty,
            new Thickness(6f, 8f, 10f, 12f),
            ShortEffect(200));

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => ((Thickness)control.GetValue(
                VeloxDev.WinForms.Xaml.Controls.Control.MarginProperty)!) == new Thickness(6f, 8f, 10f, 12f)),
            $"margin never reached its end value; last value {control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.MarginProperty)}");

        Assert.AreEqual(new System.Windows.Forms.Padding(6, 8, 10, 12), ((System.Windows.Forms.Control)control).Margin);
    }

    [TestMethod]
    public void Animate_Padding_DrivesTheDependencyPropertyAndTheNativePadding()
    {
        using var control = new ProbeControl();
        control.Padding = new Thickness(1f);
        XamlTestSurface.EnsureHandle(control);

        XamlTransition.Animate(
            control,
            VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty,
            new Thickness(1f, 2f, 3f, 4f),
            ShortEffect(200));

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => ((Thickness)control.GetValue(
                VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty)!) == new Thickness(1f, 2f, 3f, 4f)),
            $"padding never reached its end value; last value {control.GetValue(VeloxDev.WinForms.Xaml.Controls.Control.PaddingProperty)}");

        Assert.AreEqual(new System.Windows.Forms.Padding(1, 2, 3, 4), ((System.Windows.Forms.ScrollableControl)control).Padding);
    }

    [TestMethod]
    public void Animate_BorderThickness_UsesTheRegisteredThicknessSampler()
    {
        using var border = new VeloxDev.WinForms.Xaml.Controls.Border();
        XamlTestSurface.EnsureHandle(border);

        XamlTransition.Animate(
            border,
            VeloxDev.WinForms.Xaml.Controls.Border.BorderThicknessProperty,
            new Thickness(4f),
            ShortEffect(200));

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => border.BorderThickness == new Thickness(4f)),
            $"border thickness never reached its end value; last value {border.BorderThickness}");

        // 动画结束后 DP 上确实是本地值，而不是被还原成默认值。
        Assert.IsTrue(border.HasLocalValue(VeloxDev.WinForms.Xaml.Controls.Border.BorderThicknessProperty));
    }

    [TestMethod]
    public void Animate_CornerRadius_StaysAnimateable()
    {
        using var border = new VeloxDev.WinForms.Xaml.Controls.Border();
        XamlTestSurface.EnsureHandle(border);

        XamlTransition.Animate(
            border,
            VeloxDev.WinForms.Xaml.Controls.Border.CornerRadiusProperty,
            8f,
            ShortEffect(200));

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => border.CornerRadius >= 8f - 1e-6),
            $"corner radius never reached its end value; last value {border.CornerRadius}");
        Assert.AreEqual(8f, border.CornerRadius, 1e-6);
    }
}
