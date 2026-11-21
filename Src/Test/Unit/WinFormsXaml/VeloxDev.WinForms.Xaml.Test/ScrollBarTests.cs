using System.Drawing;
using System.Reflection;
using VeloxDev.WinForms.Xaml.Controls;
using XamlScrollBar = VeloxDev.WinForms.Xaml.Controls.ScrollBar;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Runtime coverage for the styleable <see cref="XamlScrollBar"/> and the <see cref="ScrollViewer"/> that hosts it.
/// </summary>
[STATestClass]
public sealed class ScrollBarTests
{
    [TestMethod]
    public void Value_IsClampedToTheNativeScrollableRange()
    {
        using var bar = new XamlScrollBar
        {
            Minimum = 10,
            Maximum = 109,
            LargeChange = 10,
        };

        bar.Value = 500;
        Assert.AreEqual(100, bar.Value, "the top of the range is Maximum - LargeChange + 1");

        bar.Value = -1;
        Assert.AreEqual(10, bar.Value, "the bottom of the range is Minimum");

        bar.Value = 55;
        Assert.AreEqual(55, bar.Value);
    }

    [TestMethod]
    public void ShrinkingTheRange_ReclampsTheValueAndRaisesValueChanged()
    {
        using var bar = new XamlScrollBar
        {
            Minimum = 10,
            Maximum = 109,
            LargeChange = 10,
        };
        bar.Value = 100;

        var changes = 0;
        bar.ValueChanged += (_, _) => changes++;

        bar.Maximum = 59;

        Assert.AreEqual(50, bar.Value, "the value must not outlive the new range");
        Assert.AreEqual(1, changes, "a clamping write is a real value change");
    }

    [TestMethod]
    public void ValueChanged_FiresOncePerEffectiveChange()
    {
        using var bar = new XamlScrollBar();
        var changes = 0;
        bar.ValueChanged += (_, _) => changes++;

        bar.Value = 20;
        bar.Value = 20;

        Assert.AreEqual(1, changes, "writing the same value twice must not raise a second event");
    }

    [TestMethod]
    public void Orientation_DefaultsToHorizontalAndRoundTrips()
    {
        using var bar = new XamlScrollBar();
        Assert.AreEqual(ScrollBarOrientation.Horizontal, bar.Orientation);

        bar.Orientation = ScrollBarOrientation.Vertical;
        Assert.AreEqual(ScrollBarOrientation.Vertical, bar.Orientation);
    }

    [TestMethod]
    public void ADarkDefault_IsVisibleWithoutAnyStyle()
    {
        using var bar = new XamlScrollBar();

        Assert.AreEqual(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), bar.TrackBrush?.ApproximateColor);
        Assert.AreEqual(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), bar.ThumbBrush?.ApproximateColor);
        Assert.AreEqual(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF), bar.ThumbHoverBrush?.ApproximateColor);
        Assert.AreEqual(12, bar.BarThickness);
    }

    [TestMethod]
    public void OnPaint_FillsTheThumbWithThumbBrush()
    {
        using var bar = new XamlScrollBar
        {
            Size = new Size(200, 12),
            BarThickness = 12,
            ThumbCornerRadius = 0,
            Maximum = 100,
            LargeChange = 10,
            TrackBrush = Color.Transparent,
            ThumbBrush = Color.Red,
        };

        using var bitmap = new Bitmap(200, 12);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);

        var onPaint = typeof(XamlScrollBar).GetMethod(
            "OnPaint",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        onPaint.Invoke(bar, [new PaintEventArgs(graphics, new Rectangle(0, 0, 200, 12))]);

        // Value 0 puts the thumb at the start; 10 px in is inside the ~20 px thumb.
        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(10, 6).ToArgb());
    }

    [TestMethod]
    public void ScrollViewer_AppliesBarStylesToBothCreatedBars()
    {
        var horizontal = BarStyle(Color.FromArgb(0x30, 1, 2, 3), barThickness: 14);
        var vertical = BarStyle(Color.FromArgb(0x40, 4, 5, 6), barThickness: 16);

        using var viewer = new ScrollViewer
        {
            HorizontalBarStyle = horizontal,
            VerticalBarStyle = vertical,
        };

        Assert.IsNotNull(viewer.HorizontalScrollBar);
        Assert.IsNotNull(viewer.VerticalScrollBar);
        Assert.AreEqual(ScrollBarOrientation.Horizontal, viewer.HorizontalScrollBar.Orientation);
        Assert.AreEqual(ScrollBarOrientation.Vertical, viewer.VerticalScrollBar.Orientation);
        Assert.AreEqual(Color.FromArgb(0x30, 1, 2, 3), viewer.HorizontalScrollBar.TrackBrush?.ApproximateColor);
        Assert.AreEqual(14, viewer.HorizontalScrollBar.BarThickness);
        Assert.AreEqual(Color.FromArgb(0x40, 4, 5, 6), viewer.VerticalScrollBar.TrackBrush?.ApproximateColor);
        Assert.AreEqual(16, viewer.VerticalScrollBar.BarThickness);
    }

    [TestMethod]
    public void ScrollViewer_MouseWheelMovesTheVerticalOffset()
    {
        using var viewer = new WheelViewer
        {
            Size = new Size(200, 100),
            ExtentHeight = 1000,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        viewer.PerformLayout();

        Assert.AreEqual(0, viewer.ScrollOffset.Y);

        viewer.Wheel(-360);

        Assert.IsGreaterThan(0, viewer.ScrollOffset.Y, "a wheel-down notch moves the offset down");
        Assert.IsLessThanOrEqualTo(900, viewer.ScrollOffset.Y, "the offset stays inside the extent");
    }

    [TestMethod]
    public void ScrollViewer_BarValueChangeRaisesViewChanged()
    {
        using var viewer = new WheelViewer
        {
            Size = new Size(200, 100),
            ExtentHeight = 1000,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        viewer.PerformLayout();

        var raised = 0;
        viewer.ViewChanged += (_, _) => raised++;

        viewer.VerticalScrollBar.Value = 300;

        Assert.AreEqual(300, viewer.ScrollOffset.Y);
        Assert.IsGreaterThan(0, raised, "a thumb move must notify virtualized hosts");
    }

    private sealed class WheelViewer : ScrollViewer
    {
        public void Wheel(int delta)
            => OnMouseWheel(new System.Windows.Forms.MouseEventArgs(
                System.Windows.Forms.MouseButtons.None,
                0,
                0,
                0,
                delta));
    }

    private static Style BarStyle(Color track, int barThickness)
    {
        var style = new Style { TargetType = typeof(XamlScrollBar) };
        style.Setters.Add(new Setter(XamlScrollBar.TrackBrushProperty, track));
        style.Setters.Add(new Setter(XamlScrollBar.BarThicknessProperty, barThickness));
        return style;
    }
}
