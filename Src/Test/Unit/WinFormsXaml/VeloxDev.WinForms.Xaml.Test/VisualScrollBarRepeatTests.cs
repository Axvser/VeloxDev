using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The auto-repeating page gesture of <see cref="VC.ScrollBar"/>: the initial page plus WPF-style repeat,
/// its stop conditions, and the template repeat-button path.
/// </summary>
[STATestClass]
public sealed class VisualScrollBarRepeatTests
{
    [TestMethod]
    public void ScrollBar_TrackPressPagesOnceThenRepeats()
    {
        using var surface = CreateSurface(8, 100);
        var bar = CreateBar();
        surface.Root = bar;
        Layout(surface, 8, 100);

        var events = new List<VC.ScrollBarScrollEventArgs>();
        bar.Scroll += (_, e) => events.Add(e);

        var press = bar.PointToSurface(new PointF(3f, 80f));
        surface.ProcessPointerDown(press, MouseButtons.Left);

        Assert.AreEqual(100d, bar.Value, 0.6d, "the initial press pages by LargeChange");
        Assert.IsTrue(IsRepeatActive(bar), "the press starts the repeat timer");
        Assert.AreEqual(1, events.Count);

        Assert.IsTrue(RepeatOnce(bar), "a repeat tick pages again");
        Assert.AreEqual(200d, bar.Value, 0.6d);
        Assert.AreEqual(2, events.Count, "every repeated page raises Scroll");
        Assert.AreEqual(VC.ScrollBarScrollEventType.LargeIncrement, events[^1].ScrollEventType);
    }

    [TestMethod]
    public void ScrollBar_RepeatStopsAtTheBound()
    {
        using var surface = CreateSurface(8, 100);
        var bar = CreateBar();
        bar.Value = 600;
        surface.Root = bar;
        Layout(surface, 8, 100);

        var press = bar.PointToSurface(new PointF(3f, 95f));
        surface.ProcessPointerDown(press, MouseButtons.Left);

        Assert.AreEqual(700d, bar.Value, 0.6d, "the first page clamps to Maximum - ViewportSize");
        Assert.IsTrue(IsRepeatActive(bar), "the clamped page still started the timer");

        Assert.IsFalse(RepeatOnce(bar), "a repeat at the bound produces no new value");
        Assert.IsFalse(IsRepeatActive(bar), "the timer stops once the bound is reached");
        Assert.AreEqual(700d, bar.Value, 0.001d);
    }

    [TestMethod]
    public void ScrollBar_ReleaseStopsTheRepeatTimer()
    {
        using var surface = CreateSurface(8, 100);
        var bar = CreateBar();
        surface.Root = bar;
        Layout(surface, 8, 100);

        var press = bar.PointToSurface(new PointF(3f, 80f));
        surface.ProcessPointerDown(press, MouseButtons.Left);
        Assert.IsTrue(IsRepeatActive(bar));

        surface.ProcessPointerUp(press, MouseButtons.Left);

        Assert.IsFalse(IsRepeatActive(bar), "release disposes the repeat timer");
        Assert.IsFalse(RepeatOnce(bar), "no dead tick can page after the release");
        Assert.AreEqual(100d, bar.Value, 0.6d);
    }

    [TestMethod]
    public void ScrollBar_ThumbPressDoesNotRepeat()
    {
        using var surface = CreateSurface(8, 100);
        var bar = CreateBar();
        surface.Root = bar;
        Layout(surface, 8, 100);

        var press = bar.PointToSurface(new PointF(3f, 10f));
        surface.ProcessPointerDown(press, MouseButtons.Left);
        Assert.IsFalse(IsRepeatActive(bar), "a thumb press is a drag, not a repeat gesture");

        var move = bar.PointToSurface(new PointF(3f, 45f));
        surface.ProcessPointerMove(move);
        surface.ProcessPointerUp(move, MouseButtons.Left);

        Assert.AreEqual(350d, bar.Value, 0.6d, "the thumb drag still maps the movement to the value");
        Assert.IsFalse(IsRepeatActive(bar));
    }

    [TestMethod]
    public void ScrollBar_TemplateRepeatButtonUsesTheSamePagePath()
    {
        using var surface = CreateSurface(8, 100);
        var bar = CreateBar();
        var clicks = 0;
        VC.Button? increase = null;

        var template = new VisualControlTemplate(scope =>
        {
            var track = new VC.Border { Width = 8, Height = 100, Padding = XamlThickness.Empty };
            var decrease = new VC.Button { Width = 8, Height = 20 };
            increase = new VC.Button { Width = 8, Height = 20 };
            VC.Canvas.SetTop(decrease, 0);
            VC.Canvas.SetTop(increase, 80);
            decrease.Click += (_, _) => clicks++;
            increase.Click += (_, _) => clicks++;
            scope.Register("PART_Track", track);
            scope.Register("PART_DecreaseRepeatButton", decrease);
            scope.Register("PART_IncreaseRepeatButton", increase);

            var root = new VC.Canvas();
            root.Add(track);
            root.Add(decrease);
            root.Add(increase);
            return root;
        })
        {
            TargetType = typeof(VC.ScrollBar),
        };

        // Value 300 时拇指在 30..60，下端的重复按钮 80..100 是干净的命中区。
        bar.Template = template;
        bar.Value = 300;
        surface.Root = bar;
        Layout(surface, 8, 100);

        var events = new List<VC.ScrollBarScrollEventArgs>();
        bar.Scroll += (_, e) => events.Add(e);

        var press = bar.PointToSurface(new PointF(4f, 90f));
        Assert.AreSame(increase, surface.FindElementAt(press), "the template button is the hit target");

        surface.ProcessPointerDown(press, MouseButtons.Left);

        Assert.AreEqual(400d, bar.Value, 0.6d, "the button press pages once, not twice");
        Assert.AreEqual(1, events.Count, "only the bar's paging raises Scroll");
        Assert.AreEqual(VC.ScrollBarScrollEventType.LargeIncrement, events[^1].ScrollEventType);
        Assert.IsTrue(IsRepeatActive(bar), "the template button press starts the same repeat");

        Assert.IsTrue(RepeatOnce(bar));
        Assert.AreEqual(500d, bar.Value, 0.6d);

        surface.ProcessPointerUp(press, MouseButtons.Left);
        Assert.IsFalse(IsRepeatActive(bar));
        Assert.AreEqual(0, clicks, "the bar owns the press, so the template button never activates");
    }

    private static VC.ScrollBar CreateBar() => new()
    {
        Width = 8,
        Height = 100,
        Maximum = 1000,
        ViewportSize = 300,
        LargeChange = 100,
    };

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    // 连发定时器的初始延迟是 400 ms，测试直接调用内部 tick，不等待真实时间。
    private static bool RepeatOnce(VC.ScrollBar bar)
        => (bool)typeof(VC.ScrollBar)
            .GetMethod("RepeatPageForTest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, null)!;

    private static bool IsRepeatActive(VC.ScrollBar bar)
        => (bool)typeof(VC.ScrollBar)
            .GetProperty("IsRepeatActive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(bar)!;
}
