using System.Drawing;
using System.Reflection;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The Brush-shaped visual properties: gradient painting, the Brush/Color aliases and brush animation.
/// </summary>
[STATestClass]
public sealed class BrushPropertyTests
{
    /// <summary>A gradient <see cref="VeloxDev.WinForms.Xaml.Controls.Border"/> background paints both of its stops.</summary>
    [TestMethod]
    public void Border_BackgroundBrush_PaintsAGradient()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new PointF(0f, 0f),
            EndPoint = new PointF(1f, 0f),
        };
        gradient.GradientStops.Add(new GradientStop(Color.Red, 0d));
        gradient.GradientStops.Add(new GradientStop(Color.Blue, 1d));

        using var border = new V.Border { Size = new Size(32, 16), Background = gradient };
        using var bitmap = new Bitmap(32, 16);
        PaintBackground(border, bitmap);

        var left = bitmap.GetPixel(2, 8);
        var right = bitmap.GetPixel(29, 8);
        Assert.IsTrue(left.R > left.B, $"the left edge should be red, got {left}");
        Assert.IsTrue(right.B > right.R, $"the right edge should be blue, got {right}");
    }

    /// <summary>A shape fills its geometry with the brush assigned to <see cref="VeloxDev.WinForms.Xaml.Controls.Shapes.Shape.Fill"/>.</summary>
    [TestMethod]
    public void Shape_FillBrush_PaintsTheInterior()
    {
        using var rectangle = new V.Shapes.Rectangle
        {
            Size = new Size(24, 24),
            Fill = new SolidColorBrush(Color.Lime),
            Stroke = null,
        };

        using var bitmap = new Bitmap(24, 24);
        Paint(rectangle, bitmap);

        Assert.AreEqual(Color.Lime.ToArgb(), bitmap.GetPixel(12, 12).ToArgb());
    }

    /// <summary>A button paints its background brush in the resting state.</summary>
    [TestMethod]
    public void Button_BackgroundBrush_PaintsTheBody()
    {
        using var button = new V.Button
        {
            Size = new Size(40, 24),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 90, 200)),
        };

        using var bitmap = new Bitmap(40, 24);
        Paint(button, bitmap);

        Assert.AreEqual(
            Color.FromArgb(255, 0, 90, 200).ToArgb(),
            bitmap.GetPixel(20, 12).ToArgb());
    }

    /// <summary>The registered <see cref="VeloxDev.Adapters.NativeSamplers.BrushSampler"/> drives a brush dependency property to its end value.</summary>
    [TestMethod]
    public void XamlTransition_AnimatesABrushDependencyProperty()
    {
        using var control = new V.Control();
        XamlTestSurface.EnsureHandle(control);
        control.SetValue(V.Control.BackgroundProperty, new SolidColorBrush(Color.Red));

        XamlTransition.Animate(
            control,
            V.Control.BackgroundProperty,
            new SolidColorBrush(Color.Blue),
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(180), Ease = Eases.Cubic.Out });

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(
                () => control.Background is SolidColorBrush brush && brush.Color.B > 245 && brush.Color.R < 10),
            $"the brush never reached blue; last colour {(control.Background as SolidColorBrush)?.Color}");
    }

    private static void PaintBackground(V.Border border, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        var method = typeof(V.Border).GetMethod(
            "OnPaintBackground",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Border.OnPaintBackground moved; update this test.");

        method.Invoke(border, [new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height))]);
    }

    private static void Paint(V.Control control, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        for (var type = control.GetType(); type is not null; type = type.BaseType)
        {
            var method = type.GetMethod(
                "OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                method.Invoke(control, [new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height))]);
                return;
            }
        }

        throw new InvalidOperationException($"{control.GetType().Name} has no OnPaint; update this test.");
    }
}
