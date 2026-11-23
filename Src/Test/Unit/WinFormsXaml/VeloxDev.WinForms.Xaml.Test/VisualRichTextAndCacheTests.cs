using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>Rich text editing and the bitmap cache/dirty-rect rendering path.</summary>
[STATestClass]
public sealed class VisualRichTextAndCacheTests
{
    [TestMethod]
    public void RichTextBox_EditsParagraphsAndUndoes()
    {
        using var surface = new VisualHost { Size = new Size(360, 260) };
        using var rich = new VC.RichTextBox { Width = 300f, Text = string.Empty };
        surface.Root = rich;
        Layout(surface, 360, 260);
        Assert.IsTrue(rich.Focus());

        surface.ProcessTextInput("Hello");
        Assert.AreEqual("Hello", rich.Text);

        surface.ProcessKeyDown(Keys.Enter);
        surface.ProcessTextInput("World");
        Assert.AreEqual("Hello\nWorld", rich.Text);

        surface.ProcessKeyDown(Keys.Back);
        Assert.AreEqual("Hello\nWorl", rich.Text);

        surface.ProcessKeyDown(Keys.Z, Keys.Control);
        Assert.AreEqual("Hello\nWorld", rich.Text);
    }

    [TestMethod]
    public void RichTextBox_TogglesBoldOnTheSelection()
    {
        using var surface = new VisualHost { Size = new Size(360, 220) };
        using var rich = new VC.RichTextBox { Width = 300f };
        surface.Root = rich;
        Layout(surface, 360, 220);

        rich.SetPlainText("Bold text");
        rich.Select(0, 4);
        rich.ToggleBold();

        Assert.IsTrue(rich.GetStyleAt(0)!.Bold);
        Assert.IsTrue(rich.GetStyleAt(3)!.Bold);
        Assert.IsFalse(rich.GetStyleAt(5)!.Bold);
    }

    [TestMethod]
    public void RichTextBox_PaintsItsText()
    {
        using var surface = new VisualHost { Size = new Size(260, 120) };
        using var rich = new VC.RichTextBox
        {
            Width = 220f,
            Background = Color.Black,
            Foreground = Color.Red,
            BorderThickness = default,
        };
        rich.SetPlainText("MMMM");
        surface.Root = rich;
        Layout(surface, 260, 120);

        using var bitmap = new Bitmap(260, 120);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(260, 120));
        }

        var red = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R > 180 && pixel.G < 100 && pixel.B < 100)
                {
                    red++;
                }
            }
        }

        Assert.IsGreaterThan(20, red, "the rich text cells paint with their run colour");
    }

    [TestMethod]
    public void RichTextBox_PaintsTheGalleryCardDocument()
    {
        using var surface = new VisualHost { Size = new Size(520, 220) };
        using var rich = new VC.RichTextBox { Width = 440f, Height = 170f };
        rich.SetPlainText("VeloxDev RichTextBox\nSelect text, then press Ctrl+B / Ctrl+I / Ctrl+U.\nCtrl+Z and Ctrl+Y walk the edit history.");
        rich.Select(0, 8);
        rich.ToggleBold();
        rich.Select(0, 0);
        surface.Root = rich;
        Layout(surface, 520, 220);

        using var bitmap = new Bitmap(520, 220);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(520, 220));
        }

        var textPixels = 0;
        var background = Color.FromArgb(0xFF, 0x3C, 0x3C, 0x3C);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) > 30 && pixel.R > 80)
                {
                    textPixels++;
                }
            }
        }

        Assert.IsGreaterThan(20, textPixels, "the gallery card's three paragraphs paint");
    }

    [TestMethod]
    public void BitmapCache_RepaintsAfterAChildDependencyPropertyChange()
    {
        using var surface = new VisualHost { Size = new Size(220, 120) };
        using var root = new VC.Border
        {
            CacheAsBitmap = true,
            Width = 140f,
            Height = 80f,
            Background = Color.Black,
            BorderThickness = default,
            Padding = new VeloxDev.WinForms.Xaml.Controls.Thickness(8f),
        };
        var child = new VC.Border { Width = 60f, Height = 40f, Background = Color.Red };
        root.Add(child);
        surface.Root = root;
        Layout(surface, 220, 120);

        var point = new Point(
            (int)(child.AbsoluteBounds.X + (child.AbsoluteBounds.Width / 2f)),
            (int)(child.AbsoluteBounds.Y + (child.AbsoluteBounds.Height / 2f)));

        Assert.AreEqual(Color.Red.ToArgb(), Sample(surface, 220, 120, point).ToArgb());

        child.Background = Color.Blue;
        Layout(surface, 220, 120);

        Assert.AreEqual(
            Color.Blue.ToArgb(),
            Sample(surface, 220, 120, point).ToArgb(),
            "a dependency-property change marks the enclosing bitmap cache dirty");
    }

    private static Color Sample(VisualHost surface, int width, int height, Point point)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(width, height));
        }

        return bitmap.GetPixel(point.X, point.Y);
    }

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
