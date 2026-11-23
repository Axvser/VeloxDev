using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Text editing in the HWND-free visual backend: multi-line layout, word navigation, the undo journal, horizontal
/// scrolling and the native editor island that supplies IME and accessibility.
/// </summary>
[STATestClass]
public sealed class VisualTextEditingTests
{
    [TestMethod]
    public void TextBox_MeasuresMultipleLinesWhenAcceptsReturn()
    {
        var single = new VC.TextBox { Text = "one" };
        var multi = new VC.TextBox { AcceptsReturn = true, Text = "one\ntwo\nthree" };

        var singleSize = single.Measure(new Size(300, 2000));
        var multiSize = multi.Measure(new Size(300, 2000));

        Assert.IsTrue(multiSize.Height > singleSize.Height, "three lines must be taller than one");
        Assert.IsTrue(multiSize.Height >= singleSize.Height + 20, "the extra lines add real height");
    }

    [TestMethod]
    public void TextBox_WrapsLongLinesInsideTheAvailableWidth()
    {
        var wrapped = new VC.TextBox { Wrap = true, Text = new string('W', 120) };
        var unwrapped = new VC.TextBox { Text = new string('W', 120) };

        var wrappedSize = wrapped.Measure(new Size(80, 4000));
        var unwrappedSize = unwrapped.Measure(new Size(80, 4000));

        Assert.IsTrue(wrappedSize.Height > unwrappedSize.Height, "wrapping adds lines");
    }

    [TestMethod]
    public void TextBox_NavigatesByWordWithControl()
    {
        using var surface = CreateSurface(320, 80);
        var text = new VC.TextBox { Width = 280, Height = 34, Text = "hello world" };
        surface.Root = text;
        Layout(surface, 320, 80);
        surface.FocusElement(text);

        surface.ProcessKeyDown(Keys.End);
        Assert.AreEqual(11, text.CaretIndex);

        surface.ProcessKeyDown(Keys.Left, Keys.Control);
        Assert.AreEqual(6, text.CaretIndex, "Ctrl+Left stops at the start of the previous word");

        surface.ProcessKeyDown(Keys.Left, Keys.Control);
        Assert.AreEqual(0, text.CaretIndex);

        surface.ProcessKeyDown(Keys.Right, Keys.Control);
        Assert.AreEqual(6, text.CaretIndex, "Ctrl+Right stops at the start of the next word");

        surface.ProcessKeyDown(Keys.Right, Keys.Control);
        Assert.AreEqual(11, text.CaretIndex);

        surface.ProcessKeyDown(Keys.End, Keys.Control);
        Assert.AreEqual(11, text.CaretIndex);

        surface.ProcessKeyDown(Keys.Home, Keys.Control);
        Assert.AreEqual(0, text.CaretIndex);
    }

    [TestMethod]
    public void TextBox_SelectsAWordOnDoubleClick()
    {
        using var surface = CreateSurface(320, 80);
        var text = new VC.TextBox { Width = 280, Height = 34, Text = "hello world" };
        surface.Root = text;
        Layout(surface, 320, 80);

        var point = text.PointToSurface(new PointF(12f, 16f));
        surface.ProcessPointerDown(point, MouseButtons.Left, clickCount: 2);

        Assert.AreEqual(0, text.SelectionStart);
        Assert.AreEqual(5, text.SelectionLength);
    }

    [TestMethod]
    public void TextBox_UndoesAndRedoesEdits()
    {
        using var surface = CreateSurface(320, 80);
        var text = new VC.TextBox { Width = 280, Height = 34 };
        surface.Root = text;
        Layout(surface, 320, 80);
        surface.FocusElement(text);

        surface.ProcessTextInput("abc");
        Assert.AreEqual("abc", text.Text);

        surface.ProcessKeyDown(Keys.Back);
        Assert.AreEqual("ab", text.Text);

        surface.ProcessKeyDown(Keys.Z, Keys.Control);
        Assert.AreEqual("abc", text.Text, "undo restores the deleted character");

        surface.ProcessKeyDown(Keys.Y, Keys.Control);
        Assert.AreEqual("ab", text.Text, "redo reapplies the edit");

        surface.ProcessKeyDown(Keys.Z, Keys.Control);
        Assert.AreEqual("abc", text.Text);
    }

    [TestMethod]
    public void TextBox_ScrollsHorizontallyToKeepTheCaretVisible()
    {
        using var surface = CreateSurface(200, 80);
        var text = new VC.TextBox { Width = 120, Height = 34, Text = new string('W', 120) };
        surface.Root = text;
        Layout(surface, 200, 80);
        surface.FocusElement(text);

        surface.ProcessKeyDown(Keys.End);

        Assert.IsTrue(text.MaxHorizontalOffset > 0f, "the text is wider than the content rectangle");
        Assert.IsTrue(text.HorizontalOffset > 0f, "the caret at the end must pull the view right");

        surface.ProcessKeyDown(Keys.Home, Keys.Control);
        Assert.AreEqual(0f, text.HorizontalOffset, 0.5f, "going home scrolls back to the left edge");
    }

    [TestMethod]
    public void TextBox_HostsTheNativeEditorOnlyWhileFocused()
    {
        using var surface = CreateSurface(320, 160);
        _ = surface.Handle;
        Application.DoEvents();

        var root = new VC.StackPanel();
        var text = new VC.TextBox { Width = 260, Height = 34 };
        var button = new VC.Button { Width = 260, Height = 34, Text = "Other" };
        root.Add(text);
        root.Add(button);
        surface.Root = root;
        Layout(surface, 320, 160);

        surface.FocusElement(text);
        Application.DoEvents();

        Assert.IsTrue(text.IsUsingNativeEditor, "a focused field shows the native island");
        Assert.IsTrue(surface.Controls.Contains(text.NativeEditor), "the island is parented to the surface");

        surface.FocusElement(button);
        Application.DoEvents();

        Assert.IsFalse(text.IsUsingNativeEditor, "leaving the field deactivates the island");
        Assert.IsFalse(surface.Controls.Contains(text.NativeEditor), "the island is removed from the surface");
    }

    [TestMethod]
    public void TextBox_NativeEditorSurvivesTheNextLayoutPass()
    {
        using var surface = CreateSurface(320, 160);
        _ = surface.Handle;
        Application.DoEvents();

        var text = new VC.TextBox { Width = 260, Height = 34 };
        surface.Root = text;
        Layout(surface, 320, 160);

        surface.FocusElement(text);
        Application.DoEvents();
        Assert.IsTrue(surface.Controls.Contains(text.NativeEditor), "the focused field parents the island");

        // 岛的 ArrangeOverride 只在尺寸变化时重跑；下一次整树布局如果把它当成“这趟没安排到”摘掉，
        // 它就会脱离宿主且再也拿不到焦点。回归：聚焦后的任何一趟布局都必须保留这个岛。
        surface.InvalidateSurface();
        Layout(surface, 320, 160);
        Application.DoEvents();

        Assert.IsTrue(text.IsUsingNativeEditor, "the island stays open across a layout pass");
        Assert.IsTrue(surface.Controls.Contains(text.NativeEditor), "the island stays parented to the surface");
    }

    [TestMethod]
    public void TextBox_UseNativeEditorFalseKeepsTheWholeFieldVisual()
    {
        using var surface = CreateSurface(320, 160);
        _ = surface.Handle;
        Application.DoEvents();

        var text = new VC.TextBox { Width = 260, Height = 34, UseNativeEditor = false };
        surface.Root = text;
        Layout(surface, 320, 160);
        surface.FocusElement(text);
        Application.DoEvents();

        Assert.AreSame(text, surface.FocusedElement);
        Assert.IsFalse(text.IsUsingNativeEditor, "the opt-out never opens the native island");
        Assert.IsFalse(surface.Controls.Contains(text.NativeEditor), "no child HWND is parented");

        surface.ProcessTextInput("abc");
        Assert.AreEqual("abc", text.Text, "typing still goes through the visual text path");
        Assert.AreEqual(3, text.CaretIndex);

        text.UseNativeEditor = true;
        Application.DoEvents();

        Assert.IsTrue(text.IsUsingNativeEditor, "turning it back on shows the island while focused");
        Assert.IsTrue(surface.Controls.Contains(text.NativeEditor));
    }

    [TestMethod]
    public void TextBox_NativeEditorIsClippedToTheScrollViewport()
    {
        using var surface = CreateSurface(200, 160);
        _ = surface.Handle;
        Application.DoEvents();

        var scroll = new VC.ScrollViewer { Width = 200, Height = 100 };
        var content = new VC.StackPanel { Width = 150 };
        content.Add(new VC.Border { Height = 190 });
        var text = new VC.TextBox { Width = 150, Height = 34 };
        content.Add(text);
        scroll.Content = content;
        surface.Root = scroll;
        Layout(surface, 200, 160);

        scroll.VerticalOffset = 100;
        Layout(surface, 200, 160);
        surface.FocusElement(text);
        Application.DoEvents();

        Assert.IsTrue(text.IsUsingNativeEditor, "the field is focused");
        var viewport = scroll.ContentPresenter!.AbsoluteBounds;
        var scale = surface.DeviceDpi / 96f;
        var editorTop = text.NativeEditor.Bounds.Top / scale;
        var editorBottom = text.NativeEditor.Bounds.Bottom / scale;

        // 原生岛是独立 HWND；不按视口裁剪时，它会越过 ScrollViewer 的下边缘盖住滚动条。
        Assert.IsTrue(editorTop >= viewport.Top - 0.5f, "the island starts inside the viewport");
        Assert.IsTrue(editorBottom <= viewport.Bottom + 0.5f, "the island ends inside the viewport");
    }

    [TestMethod]
    public void TextBox_TransparentBackgroundFallsBackToTheNearestOpaqueAncestor()
    {
        using var surface = CreateSurface(220, 120);
        _ = surface.Handle;
        Application.DoEvents();

        var sheet = new VC.Border { Background = new SolidColorBrush(Color.FromArgb(255, 13, 17, 23)) };
        var text = new VC.TextBox { Width = 160, Height = 32, Background = Brush.Empty };
        sheet.Add(text);
        surface.Root = sheet;
        Layout(surface, 220, 120);

        surface.FocusElement(text);
        Application.DoEvents();

        Assert.IsTrue(text.IsUsingNativeEditor, "the native island still opens for a transparent visual field");
        Assert.AreEqual(Color.FromArgb(255, 13, 17, 23), text.NativeEditor.BackColor,
            "the native TextBox gets the nearest opaque ancestor colour, never transparent");
    }

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
