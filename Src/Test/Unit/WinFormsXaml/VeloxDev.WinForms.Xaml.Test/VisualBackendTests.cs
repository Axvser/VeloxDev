using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The HWND-free visual backend: layout, hit-testing, focus, text editing, scrolling and rendering without a
/// per-element window.
/// </summary>
[STATestClass]
public sealed class VisualBackendTests
{
    [TestMethod]
    public void VisualElements_AreNotWinFormsControls()
    {
        Assert.IsFalse(typeof(VisualElement).IsSubclassOf(typeof(System.Windows.Forms.Control)));
        Assert.IsFalse(typeof(VC.Button).IsSubclassOf(typeof(System.Windows.Forms.Control)));
    }

    [TestMethod]
    public void CheckBoxGlyphIsCenteredInsideTheControl()
    {
        using var surface = CreateSurface(28, 28);
        surface.BackColor = Color.Black;
        var box = new VC.CheckBox { Width = 28, Height = 28, IsChecked = true };
        surface.Root = box;

        using var bitmap = new Bitmap(28, 28);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(28, 28));
        }

        var minX = 28;
        var minY = 28;
        for (var y = 0; y < 28; y++)
        {
            for (var x = 0; x < 28; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.B > 150 && pixel.R < 80)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                }
            }
        }

        Assert.IsTrue(minX >= 2, "the chrome must not fall back to the control origin");
        Assert.IsTrue(minY >= 2, "the chrome must not fall back to the control origin");
        Assert.IsTrue(minX < 14 && minY < 14, "the chrome stays near the center of the control");
    }

    [TestMethod]
    public void TransparentPanelBackgroundIsHitTestable()
    {
        using var surface = CreateSurface(100, 100);
        var root = new VC.Canvas { Width = 100, Height = 100 };
        var child = new VC.Grid { Width = 40, Height = 40, Background = new SolidColorBrush(Color.Transparent) };
        root.Add(child);
        surface.Root = root;
        Layout(surface, 100, 100);

        Assert.AreSame(child, surface.FindElementAt(new PointF(20f, 20f)));
    }

    [TestMethod]
    public void ChildStraddlingParentBoundsStaysHitTestable()
    {
        using var surface = CreateSurface(100, 100);
        var root = new VC.Canvas { Width = 100, Height = 100 };
        var child = new VC.Button { Width = 20, Height = 20 };
        VC.Canvas.SetLeft(child, -10);
        VC.Canvas.SetTop(child, 40);
        root.Add(child);
        surface.Root = root;
        Layout(surface, 100, 100);

        Assert.AreSame(child, surface.FindElementAt(new PointF(5f, 50f)));
    }

    [TestMethod]
    public void PointTransformsIncludeVisualTransforms()
    {
        using var surface = CreateSurface(200, 200);
        var root = new VC.Canvas { Width = 200, Height = 200 };
        var child = new VC.Border { Width = 20, Height = 20 };
        VC.Canvas.SetLeft(child, 10);
        VC.Canvas.SetTop(child, 20);
        child.Transform = new System.Drawing.Drawing2D.Matrix(2f, 0f, 0f, 2f, 0f, 0f);
        root.Add(child);
        surface.Root = root;
        Layout(surface, 200, 200);

        var surfacePoint = child.PointToSurface(new PointF(20f, 20f));
        Assert.AreEqual(50f, surfacePoint.X, 0.01f);
        Assert.AreEqual(60f, surfacePoint.Y, 0.01f);

        var local = child.PointFromSurface(surfacePoint);
        Assert.AreEqual(20f, local.X, 0.01f);
        Assert.AreEqual(20f, local.Y, 0.01f);

        var bounds = child.AbsoluteBounds;
        Assert.AreEqual(10f, bounds.X, 0.01f);
        Assert.AreEqual(20f, bounds.Y, 0.01f);
        Assert.AreEqual(40f, bounds.Width, 0.01f);
        Assert.AreEqual(40f, bounds.Height, 0.01f);
    }

    [TestMethod]
    public void StackPanel_StacksWithSpacingAndAlignsTheCrossAxis()
    {
        using var surface = CreateSurface(200, 100);
        var root = new VC.StackPanel { Spacing = 10, Padding = new XamlThickness(5f) };
        var first = new VC.Border { Width = 50, Height = 20, HorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment.Center };
        var second = new VC.Border { Width = 60, Height = 30, HorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment.Left };
        root.Add(first);
        root.Add(second);
        surface.Root = root;

        Layout(surface, 200, 100);

        Assert.AreEqual(new RectangleF(75f, 5f, 50f, 20f), first.Bounds);
        Assert.AreEqual(new RectangleF(5f, 35f, 60f, 30f), second.Bounds);
        Assert.AreEqual(0, surface.Controls.Count, "no HWND is created for visual elements");
    }

    [TestMethod]
    public void Border_InsetsItsChildByPaddingAndBorderThickness()
    {
        using var surface = CreateSurface(100, 60);
        var root = new VC.Border
        {
            Padding = new XamlThickness(4f),
            BorderThickness = new XamlThickness(2f),
        };
        var child = new VC.Border { Padding = XamlThickness.Empty };
        root.Child = child;
        surface.Root = root;

        Layout(surface, 100, 60);

        Assert.AreEqual(new RectangleF(6f, 6f, 88f, 48f), child.Bounds);
    }

    [TestMethod]
    public void HitTest_FindsTheDeepestButton()
    {
        using var surface = CreateSurface(200, 100);
        var root = new VC.StackPanel();
        var first = new VC.Button { Text = "First", Width = 120, Height = 40 };
        var second = new VC.Button { Text = "Second", Width = 120, Height = 40 };
        root.Add(first);
        root.Add(second);
        surface.Root = root;

        Layout(surface, 200, 100);

        Assert.AreSame(first, surface.FindElementAt(new PointF(100f, 20f)));
        Assert.AreSame(second, surface.FindElementAt(new PointF(100f, 60f)));
        Assert.IsNull(surface.FindElementAt(new PointF(180f, 20f)), "a transparent panel gap passes the hit through");
    }

    [TestMethod]
    public void Tab_MovesLogicalFocusInTreeOrder()
    {
        using var surface = CreateSurface(200, 100);
        var root = new VC.StackPanel();
        var first = new VC.Button { Text = "First", Width = 120, Height = 40 };
        var second = new VC.Button { Text = "Second", Width = 120, Height = 40 };
        root.Add(first);
        root.Add(second);
        surface.Root = root;
        Layout(surface, 200, 100);

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(first, surface.FocusedElement);

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(second, surface.FocusedElement);

        surface.ProcessKeyDown(Keys.Tab, Keys.Shift);
        Assert.AreSame(first, surface.FocusedElement);
    }

    [TestMethod]
    public void Button_ClicksFromPointerAndKeyboard()
    {
        using var surface = CreateSurface(200, 100);
        var root = new VC.StackPanel();
        var button = new VC.Button { Text = "Run", Width = 120, Height = 40 };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        root.Add(button);
        surface.Root = root;
        Layout(surface, 200, 100);

        surface.ProcessPointerDown(new PointF(100f, 20f), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(100f, 20f), MouseButtons.Left);
        Assert.AreEqual(1, clicks, "pointer click");

        surface.FocusElement(button);
        surface.ProcessKeyDown(Keys.Space);
        Assert.AreEqual(1, clicks, "Space activates on key up, not on repeat");
        surface.ProcessKeyUp(Keys.Space);
        Assert.AreEqual(2, clicks, "Space activates the focused button");
    }

    [TestMethod]
    public void TextBox_EditsTextAndHandlesSelection()
    {
        using var surface = CreateSurface(260, 80);
        var text = new VC.TextBox { Width = 220, Height = 34 };
        surface.Root = text;
        Layout(surface, 260, 80);
        surface.FocusElement(text);

        surface.ProcessTextInput("abc");
        Assert.AreEqual("abc", text.Text);

        surface.ProcessKeyDown(Keys.Back);
        Assert.AreEqual("ab", text.Text);

        surface.ProcessKeyDown(Keys.Home);
        surface.ProcessKeyDown(Keys.Delete);
        Assert.AreEqual("b", text.Text);

        surface.ProcessKeyDown(Keys.A, Keys.Control);
        surface.ProcessTextInput("x");
        Assert.AreEqual("x", text.Text);
    }

    [TestMethod]
    public void TextBox_ExtendsSelectionBackwardsFromTheCaret()
    {
        using var surface = CreateSurface(260, 120);
        var root = new VC.StackPanel();
        var text = new VC.TextBox { Width = 220, Height = 34, Text = "abcd" };
        var disabled = new VC.TextBox { Width = 220, Height = 34, IsEnabled = false };
        root.Add(text);
        root.Add(disabled);
        surface.Root = root;
        Layout(surface, 260, 120);
        surface.FocusElement(text);

        surface.ProcessKeyDown(Keys.End);
        surface.ProcessKeyDown(Keys.Left, Keys.Shift);
        surface.ProcessKeyDown(Keys.Left, Keys.Shift);

        Assert.AreEqual(2, text.SelectionStart);
        Assert.AreEqual(2, text.SelectionLength);
        Assert.AreEqual(2, text.CaretIndex);

        Assert.IsFalse(surface.FocusElement(disabled), "a disabled field cannot take focus");
    }

    [TestMethod]
    public void ScrollViewer_ScrollsWheelAndClampsOffset()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var content = new VC.StackPanel();
        for (var index = 0; index < 50; index++)
        {
            content.Add(new VC.Border { Height = 20, Padding = XamlThickness.Empty, IsHitTestVisible = true });
        }

        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreEqual(1000f, view.ExtentHeight, 0.1f);
        Assert.AreEqual(100f, view.ViewportHeight, 0.1f);
        Assert.AreEqual(900f, view.MaxVerticalOffset, 0.1f);

        surface.ProcessPointerWheel(new PointF(120f, 80f), -120);
        Assert.IsTrue(view.VerticalOffset > 0f);

        surface.ProcessPointerWheel(new PointF(120f, 80f), -120000);
        Assert.AreEqual(900f, view.VerticalOffset, 0.1f);
    }

    [TestMethod]
    public void ThousandsOfVisuals_CreateNoChildWindows()
    {
        using var surface = CreateSurface(800, 600);
        var view = new VC.ScrollViewer { Width = 800, Height = 600 };
        var content = new VC.StackPanel();
        for (var index = 0; index < 2000; index++)
        {
            content.Add(new VC.TextBlock { Text = $"Row {index}", Height = 18 });
        }

        view.Content = content;
        surface.Root = view;
        Layout(surface, 800, 600);

        Assert.AreEqual(0, surface.Controls.Count, "2000 rows must not create a single HWND");
        Assert.AreEqual(36000f, view.ExtentHeight, 0.1f);
    }

    [TestMethod]
    public void NativeControlHost_PlacesANativeChild()
    {
        using var surface = CreateSurface(200, 100);
        var host = new VC.NativeControlHost { Width = 100, Height = 30 };
        using var native = new System.Windows.Forms.Label { Text = "native" };
        host.Child = native;
        surface.Root = host;

        Layout(surface, 200, 100);

        Assert.IsTrue(surface.Controls.Contains(native));
        Assert.AreEqual(new Rectangle(50, 35, 100, 30), native.Bounds);
    }

    [TestMethod]
    public void NativeKeyMessages_ReachTheFocusedElement()
    {
        using var surface = CreateSurface(260, 160);
        _ = surface.Handle;
        Application.DoEvents();

        var root = new VC.StackPanel();
        var first = new VC.TextBox { Width = 220, Height = 34 };
        var second = new VC.TextBox { Width = 220, Height = 34 };
        root.Add(first);
        root.Add(second);
        surface.Root = root;
        Layout(surface, 260, 160);
        surface.FocusElement(first);

        SendKey(surface, Keys.Tab);
        Assert.AreSame(second, surface.FocusedElement, "Tab must reach the logical focus chain through WM_KEYDOWN");

        first.Text = "abc";
        surface.FocusElement(first);
        SendKey(surface, Keys.End);
        SendKey(surface, Keys.Left);
        Assert.AreEqual(2, first.CaretIndex, "arrow keys must reach the focused field");
    }

    [TestMethod]
    public void CollapsingANativeHost_RemovesItsChildWindow()
    {
        using var surface = CreateSurface(200, 100);
        var host = new VC.NativeControlHost { Width = 100, Height = 30 };
        using var native = new System.Windows.Forms.Label { Text = "native" };
        host.Child = native;
        surface.Root = host;
        Layout(surface, 200, 100);
        Assert.IsTrue(surface.Controls.Contains(native));

        host.Visibility = VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed;
        Layout(surface, 200, 100);

        Assert.IsFalse(surface.Controls.Contains(native), "a collapsed native island must not float above the tree");
    }

    [TestMethod]
    public void RemovingTheFocusedTextBox_ClearsFocus()
    {
        using var surface = CreateSurface(260, 80);
        var root = new VC.StackPanel();
        var text = new VC.TextBox { Width = 220, Height = 34 };
        root.Add(text);
        surface.Root = root;
        Layout(surface, 260, 80);
        surface.FocusElement(text);
        Assert.AreSame(text, surface.FocusedElement);

        root.RemoveChild(text);

        Assert.IsNull(surface.FocusedElement);
    }

    [TestMethod]
    public void Border_RendersBackgroundAndFullStroke()
    {
        using var surface = CreateSurface(20, 20);
        surface.BackColor = Color.Black;
        var root = new VC.Border
        {
            Padding = XamlThickness.Empty,
            Background = new SolidColorBrush(Color.Red),
            BorderBrush = new SolidColorBrush(Color.White),
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 0f,
        };
        surface.Root = root;

        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(20, 20));
        }

        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(0, 10).ToArgb(), "left stroke");
        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(19, 10).ToArgb(), "right stroke");
        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(10, 0).ToArgb(), "top stroke");
        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(10, 19).ToArgb(), "bottom stroke");
        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(10, 10).ToArgb(), "body");
    }

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private static void SendKey(VisualHost surface, Keys key)
    {
        SendMessage(surface.Handle, WmKeyDown, (IntPtr)key, IntPtr.Zero);
        SendMessage(surface.Handle, WmKeyUp, (IntPtr)key, IntPtr.Zero);
        Application.DoEvents();
    }

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
