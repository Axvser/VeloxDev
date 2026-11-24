using System.Drawing;
using System.Windows.Forms;
using Easel;
using Easel.Animation;
using Easel.Visuals;
using Easel.Visuals.Controls;
using VeloxDev.WinForms.XamlAnimation;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

/// <summary>The adapter drives Easel dependency properties through the real transition engine.</summary>
[TestClass]
public sealed class EaselAnimationBridgeTests
{
    [TestMethod]
    public void RegisteredAdapter_AnimatesAnEaselOpacityPropertyAndHoldsTheEndValue()
    {
        EaselAnimationBootstrap.EnsureRegistered();

        using var host = new VisualHost { Size = new Size(240, 120) };
        var border = new Border { Width = 120, Height = 60, Opacity = 1d };
        host.Root = border;

        VisualTransition.Animate(
            border,
            VisualElement.OpacityProperty,
            0.25d,
            new AnimationSpec { Duration = TimeSpan.FromMilliseconds(120) });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && Math.Abs(border.Opacity - 0.25d) > 0.01d)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.AreEqual(0.25d, border.Opacity, 0.02d, "the transition reaches its end value");
    }

    [TestMethod]
    public void RegisteredAdapter_AnimatesAStructuralBrush()
    {
        EaselAnimationBootstrap.EnsureRegistered();

        using var host = new VisualHost { Size = new Size(240, 120) };
        var start = new LinearGradientBrush();
        start.GradientStops.Add(new GradientStop(Color.Red, 0d));
        start.GradientStops.Add(new GradientStop(Color.Blue, 1d));

        var end = new LinearGradientBrush();
        end.GradientStops.Add(new GradientStop(Color.Lime, 0d));
        end.GradientStops.Add(new GradientStop(Color.Yellow, 1d));

        var border = new Border { Width = 120, Height = 60, Background = start };
        host.Root = border;

        VisualTransition.Animate(
            border,
            VisualElement.BackgroundProperty,
            end,
            new AnimationSpec { Duration = TimeSpan.FromMilliseconds(120) });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && border.Background is not LinearGradientBrush)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        var reached = border.Background as LinearGradientBrush;
        Assert.IsNotNull(reached, "the brush endpoint stays a structural gradient");
        Assert.AreEqual(2, reached!.GradientStops.Count);

        var deadlineColor = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadlineColor && reached.GradientStops[0].Color != Color.Lime)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.AreEqual(Color.Lime.ToArgb(), ((LinearGradientBrush)border.Background!).GradientStops[0].Color.ToArgb());
    }
}
