using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using VS = VeloxDev.WinForms.Xaml.Visuals.Controls.Shapes;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The extended visual control set: tree, menu, calendar/date picker, containers, shapes, presenter and ranges.
/// </summary>
[STATestClass]
public sealed class VisualExtendedControlTests
{
    [TestMethod]
    public void TreeView_FlattensExpandedRowsAndSelectsByPointerAndKeyboard()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var tree = new VC.TreeView();
        var group = new VC.TreeViewItem { Header = "Group", IsExpanded = true };
        var child = new VC.TreeViewItem { Header = "Child" };
        group.Items.Add(child);
        var second = new VC.TreeViewItem { Header = "Second" };
        tree.Items.Add(group);
        tree.Items.Add(second);
        surface.Root = tree;
        Layout(surface, 320, 240);

        Assert.HasCount(3, tree.Rows, "an expanded group shows its child");

        var secondBounds = second.AbsoluteBounds;
        surface.ProcessPointerDown(Center(secondBounds), MouseButtons.Left);
        surface.ProcessPointerUp(Center(secondBounds), MouseButtons.Left);
        Assert.AreSame(second, tree.SelectedItem, "clicking a row selects it");

        Assert.IsTrue(tree.Focus());
        surface.ProcessKeyDown(Keys.Up);
        Assert.AreSame(child, tree.SelectedItem, "Up moves to the previous visible row");

        var expanderX = group.AbsoluteBounds.X + 8f;
        var expanderY = group.AbsoluteBounds.Y + (group.AbsoluteBounds.Height / 2f);
        surface.ProcessPointerDown(new PointF(expanderX, expanderY), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(expanderX, expanderY), MouseButtons.Left);
        Assert.IsFalse(group.IsExpanded, "clicking the disclosure triangle collapses the group");
        Assert.HasCount(2, tree.Rows);
    }

    [TestMethod]
    public void ContextMenu_InvokesAnItemAndClosesOnOutsidePress()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var root = new VC.StackPanel();
        using var target = new VC.Border { Width = 80f, Height = 30f, Background = Color.Lime };
        root.Add(target);
        surface.Root = root;
        Layout(surface, 320, 240);

        var invoked = 0;
        using var menu = new VC.VisualContextMenu();
        menu.Items.Add(new VC.VisualMenuItem { Header = "Delete", Command = () => invoked++ });
        menu.OpenAt(target, new PointF(target.AbsoluteBounds.X + 4f, target.AbsoluteBounds.Y + 4f));
        Layout(surface, 320, 240);

        var firstRow = new PointF(menu.AbsoluteBounds.X + 30f, menu.AbsoluteBounds.Y + 18f);
        surface.ProcessPointerDown(firstRow, MouseButtons.Left);
        surface.ProcessPointerUp(firstRow, MouseButtons.Left);

        Assert.AreEqual(1, invoked, "the clicked row invokes its command");
        Assert.IsFalse(menu.IsOpen, "invoking an item closes the menu");

        menu.OpenAt(target, new PointF(target.AbsoluteBounds.X + 4f, target.AbsoluteBounds.Y + 4f));
        Layout(surface, 320, 240);
        surface.ProcessPointerDown(new PointF(300f, 220f), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(300f, 220f), MouseButtons.Left);
        Assert.IsFalse(menu.IsOpen, "an outside press closes the menu");
    }

    [TestMethod]
    public void Calendar_KeyboardMovesTheSelection()
    {
        using var surface = new VisualSurface { Size = new Size(320, 280) };
        using var calendar = new VC.Calendar { Width = 250f, Height = 226f };
        surface.Root = calendar;
        Layout(surface, 320, 280);
        Assert.IsTrue(calendar.Focus());

        surface.ProcessKeyDown(Keys.Right);

        Assert.IsNotNull(calendar.SelectedDate);
        Assert.AreEqual(DateTime.Today.AddDays(1).Date, calendar.SelectedDate!.Value.Date);
        Assert.AreEqual(calendar.SelectedDate!.Value.Month, calendar.DisplayMonth.Month);
    }

    [TestMethod]
    public void DatePicker_OpensCalendarSelectsAndCloses()
    {
        using var surface = new VisualSurface { Size = new Size(320, 280) };
        using var root = new VC.StackPanel();
        using var picker = new VC.DatePicker { Width = 180f };
        root.Add(picker);
        surface.Root = root;
        Layout(surface, 320, 280);

        var field = Center(picker.AbsoluteBounds);
        surface.ProcessPointerDown(field, MouseButtons.Left);
        surface.ProcessPointerUp(field, MouseButtons.Left);

        Assert.IsTrue(picker.IsDropDownOpen);
        Assert.IsInstanceOfType<VC.Calendar>(surface.FocusedElement);

        surface.ProcessKeyDown(Keys.Right);

        Assert.IsFalse(picker.IsDropDownOpen, "choosing a day closes the calendar");
        Assert.IsNotNull(picker.SelectedDate);
        Assert.AreEqual(DateTime.Today.AddDays(1).Date, picker.SelectedDate!.Value.Date);
    }

    [TestMethod]
    public void Expander_TogglesTheContentVisibilityAndHeight()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var expander = new VC.Expander { Width = 240f, Header = "More" };
        var content = new VC.TextBlock { Text = "Body", Height = 60f };
        expander.Content = content;
        surface.Root = expander;
        Layout(surface, 320, 240);

        Assert.IsTrue(content.IsVisible);
        var expandedHeight = expander.DesiredSize.Height;

        surface.ProcessPointerDown(
            new PointF(expander.AbsoluteBounds.X + 20f, expander.AbsoluteBounds.Y + 10f),
            MouseButtons.Left);
        surface.ProcessPointerUp(
            new PointF(expander.AbsoluteBounds.X + 20f, expander.AbsoluteBounds.Y + 10f),
            MouseButtons.Left);

        Assert.IsFalse(expander.IsExpanded);
        Assert.IsFalse(content.IsVisible);
        Layout(surface, 320, 240);
        Assert.IsLessThan(expandedHeight, expander.DesiredSize.Height);
    }

    [TestMethod]
    public void TabControl_SwitchesTheVisiblePage()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var tabs = new VC.TabControl();
        var first = new VC.TextBlock { Text = "First" };
        var second = new VC.TextBlock { Text = "Second" };
        tabs.Items.Add(new VC.TabItem { Header = "One", Content = first });
        tabs.Items.Add(new VC.TabItem { Header = "Two", Content = second });
        tabs.SelectedIndex = 0;
        tabs.Refresh();
        surface.Root = tabs;
        Layout(surface, 320, 240);

        Assert.AreSame(tabs, first.ParentElement, "the selected page is a child of the tab control");
        Assert.IsNull(second.ParentElement);

        Assert.IsTrue(tabs.Focus());
        surface.ProcessKeyDown(Keys.Right);

        Assert.AreEqual(1, tabs.SelectedIndex);
        Assert.AreSame(tabs, second.ParentElement);
        Assert.IsNull(first.ParentElement);
    }

    [TestMethod]
    public void UniformGrid_PlacesChildrenIntoEqualCells()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var grid = new VC.UniformGrid { Columns = 2, Width = 200f, Height = 100f };
        var a = new VC.Border();
        var b = new VC.Border();
        var c = new VC.Border();
        grid.Add(a);
        grid.Add(b);
        grid.Add(c);
        surface.Root = grid;
        Layout(surface, 320, 240);

        Assert.AreEqual(0f, a.Bounds.X);
        Assert.AreEqual(100f, b.Bounds.X, "the second column starts at half the width");
        Assert.AreEqual(0f, c.Bounds.X);
        Assert.AreEqual(50f, c.Bounds.Y, "the third child wraps to the second row");
    }

    [TestMethod]
    public void Viewbox_ScalesTheChildIntoTheBox()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var viewbox = new VC.Viewbox { Width = 200f, Height = 200f };
        var child = new VC.Border { Width = 100f, Height = 50f };
        viewbox.Child = child;
        surface.Root = viewbox;
        Layout(surface, 320, 240);

        Assert.AreEqual(2f, child.Transform.Elements[0], "a 100x50 child fits a 200x200 box at scale 2");
        Assert.AreEqual(100f, child.Bounds.Width, "the layout slot stays at the design size; the transform scales the paint");
        Assert.AreEqual(50f, child.Bounds.Height);
    }

    [TestMethod]
    public void VisualPresenter_HostsAContainerVisual()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var presenter = new VC.VisualPresenter { Width = 120f, Height = 60f };
        var container = new ContainerVisual();
        container.Add(new ShapeVisual
        {
            Bounds = new RectangleF(0f, 0f, 40f, 40f),
            Geometry = new EllipseGeometry(),
            Fill = new SolidColorBrush(Color.Orange),
        });
        presenter.Content = container;
        surface.Root = presenter;
        Layout(surface, 320, 240);

        Assert.AreEqual(120f, container.Bounds.Width);
        Assert.AreEqual(60f, container.Bounds.Height);
    }

    [TestMethod]
    public void Slider_PointerAndKeyboardChangeTheValue()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var slider = new VC.Slider { Width = 200f, Minimum = 0d, Maximum = 100d };
        surface.Root = slider;
        Layout(surface, 320, 240);

        var right = new PointF(slider.AbsoluteBounds.Right - 8f, Center(slider.AbsoluteBounds).Y);
        surface.ProcessPointerDown(right, MouseButtons.Left);
        surface.ProcessPointerUp(right, MouseButtons.Left);
        Assert.IsGreaterThan(90d, slider.Value);

        Assert.IsTrue(slider.Focus());
        surface.ProcessKeyDown(Keys.Home);
        Assert.AreEqual(0d, slider.Value);
        surface.ProcessKeyDown(Keys.Right);
        Assert.IsGreaterThan(0d, slider.Value);
    }

    [TestMethod]
    public void Shapes_MeasureAndRenderWithoutAnHwnd()
    {
        using var surface = new VisualSurface { Size = new Size(320, 240) };
        using var root = new VC.StackPanel { Spacing = 4 };
        using var ellipse = new VS.Ellipse { Width = 40f, Height = 20f, Fill = Theme.Current.Accent };
        using var polygon = new VS.Polygon { Points = [new PointF(0, 20), new PointF(20, 0), new PointF(40, 20)] };
        using var path = new VS.Path { Data = "M0,0 L20,0 L20,20 Z", Width = 20f, Height = 20f };
        root.Add(ellipse);
        root.Add(polygon);
        root.Add(path);
        surface.Root = root;

        using var bitmap = new Bitmap(320, 240);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(320, 240));

        Assert.IsEmpty(surface.Controls);
        Assert.IsGreaterThanOrEqualTo(40, polygon.DesiredSize.Width);
        Assert.IsGreaterThanOrEqualTo(20, path.DesiredSize.Height);
    }

    private static PointF Center(RectangleF bounds)
        => new(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));

    private static void Layout(VisualSurface surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
