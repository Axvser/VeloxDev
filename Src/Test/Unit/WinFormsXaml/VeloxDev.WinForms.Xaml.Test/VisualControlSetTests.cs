using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The selection and item controls of the HWND-free visual backend: toggle states and grouping, item generation,
/// selection and fixed-row virtualization.
/// </summary>
[STATestClass]
public sealed class VisualControlSetTests
{
    [TestMethod]
    public void CheckBox_TogglesByPointerAndKeyboardAndSkipsWhenDisabled()
    {
        using var surface = CreateSurface(80, 120);
        var root = new VC.StackPanel();
        var box = new VC.CheckBox { Width = 34, Height = 34 };
        var checkedCount = 0;
        box.Checked += (_, _) => checkedCount++;
        root.Add(box);
        surface.Root = root;
        Layout(surface, 80, 120);

        Assert.IsFalse(box.IsChecked == true);

        var center = CenterOf(box);
        Click(surface, center.X, center.Y);
        Assert.IsTrue(box.IsChecked == true, "the first click checks the box");
        Assert.AreEqual(1, checkedCount);

        Click(surface, center.X, center.Y);
        Assert.IsFalse(box.IsChecked == true, "the second click clears it");
        Assert.AreEqual(1, checkedCount, "unchecking does not raise Checked");

        surface.FocusElement(box);
        surface.ProcessKeyDown(Keys.Space);
        surface.ProcessKeyUp(Keys.Space);
        Assert.IsTrue(box.IsChecked == true, "Space activates on key up");

        surface.ProcessKeyDown(Keys.Enter);
        surface.ProcessKeyUp(Keys.Enter);
        Assert.IsFalse(box.IsChecked == true, "Enter activates on key down");

        box.IsEnabled = false;
        Click(surface, center.X, center.Y);
        Assert.IsFalse(box.IsChecked == true, "a disabled check box ignores the pointer");
    }

    [TestMethod]
    public void CheckBox_SupportsTheIndeterminateState()
    {
        using var surface = CreateSurface(80, 80);
        var box = new VC.CheckBox { Width = 34, Height = 34 };
        surface.Root = box;
        Layout(surface, 80, 80);

        box.IsChecked = null;
        Assert.IsNull(box.IsChecked);

        var center = new PointF(
            box.AbsoluteBounds.X + (box.AbsoluteBounds.Width / 2f),
            box.AbsoluteBounds.Y + (box.AbsoluteBounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);

        Assert.IsTrue(box.IsChecked == true, "an indeterminate box becomes checked");
    }

    [TestMethod]
    public void RadioButton_ChecksAndUnchecksOnlyTheSameGroupInTheParentSubtree()
    {
        var root = new VC.StackPanel();
        var first = new VC.RadioButton { GroupName = "mode" };
        var second = new VC.RadioButton { GroupName = "mode" };
        var other = new VC.RadioButton { GroupName = "size" };
        var nestedRoot = new VC.StackPanel();
        var nested = new VC.RadioButton { GroupName = "mode" };
        nestedRoot.Add(nested);
        root.Add(first);
        root.Add(second);
        root.Add(other);
        root.Add(nestedRoot);

        var secondChecked = 0;
        second.Checked += (_, _) => secondChecked++;

        first.IsChecked = true;
        Assert.IsTrue(first.IsChecked == true);

        second.IsChecked = true;
        Assert.IsFalse(first.IsChecked == true, "the same group is exclusive");
        Assert.IsTrue(second.IsChecked == true);
        Assert.AreEqual(1, secondChecked);
        Assert.IsFalse(nested.IsChecked == true, "a nested radio in the parent subtree is part of the group");

        other.IsChecked = true;
        Assert.IsTrue(second.IsChecked == true, "a different group is left alone");

        second.IsChecked = false;
        Assert.IsFalse(second.IsChecked == true, "a radio can be cleared programmatically");
    }

    [TestMethod]
    public void RadioButton_ActivationAlwaysChecksAndNeverTogglesOff()
    {
        using var surface = CreateSurface(80, 80);
        var radio = new VC.RadioButton { Width = 34, Height = 34 };
        surface.Root = radio;
        Layout(surface, 80, 80);

        var center = new PointF(
            radio.AbsoluteBounds.X + (radio.AbsoluteBounds.Width / 2f),
            radio.AbsoluteBounds.Y + (radio.AbsoluteBounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.IsTrue(radio.IsChecked == true);

        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.IsTrue(radio.IsChecked == true, "clicking a checked radio keeps it checked");
    }

    [TestMethod]
    public void ToggleButton_ActivatesByPointerKeyboardAndIgnoresDisabled()
    {
        using var surface = CreateSurface(120, 80);
        var toggle = new VC.ToggleButton { Width = 68, Height = 34 };
        surface.Root = toggle;
        Layout(surface, 120, 80);

        var bounds = toggle.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.IsTrue(toggle.IsChecked);

        surface.FocusElement(toggle);
        surface.ProcessKeyDown(Keys.Space);
        surface.ProcessKeyUp(Keys.Space);
        Assert.IsFalse(toggle.IsChecked, "Space flips the switch back");

        toggle.IsEnabled = false;
        surface.ProcessKeyDown(Keys.Enter);
        surface.ProcessKeyUp(Keys.Enter);
        Assert.IsFalse(toggle.IsChecked, "a disabled switch ignores the keyboard");
    }

    [TestMethod]
    public void ItemsControl_GeneratesItemsStacksThemAndFollowsCollectionChanges()
    {
        var source = new ObservableCollection<string> { "alpha", "beta" };
        using var surface = CreateSurface(200, 200);
        var control = new VC.ItemsControl
        {
            ItemsSource = source,
            ItemFactory = item => new VC.TextBlock { Text = item?.ToString() ?? string.Empty },
        };
        surface.Root = control;
        Layout(surface, 200, 200);

        Assert.AreEqual(2, control.Items.Count);
        Assert.AreEqual("alpha", ((VC.TextBlock)control.Items[0]).Text);
        Assert.AreEqual(control.Items[0].Bounds.Height, control.Items[1].Bounds.Y, 0.1f, "items stack vertically");
        Assert.AreEqual(0f, control.Items[0].Bounds.Y, 0.1f);

        source.Add("gamma");
        Assert.AreEqual(3, control.Items.Count, "an add rebuilds the realized items");

        source.RemoveAt(0);
        Assert.AreEqual(2, control.Items.Count, "a remove rebuilds the realized items");
        Assert.AreEqual("beta", ((VC.TextBlock)control.Items[0]).Text);

        var previous = control.Items[0];
        control.ItemFactory = _ => new VC.TextBlock { Text = "replaced" };
        Assert.AreEqual(2, control.Items.Count);
        Assert.AreNotSame(previous, control.Items[0], "changing the factory rebuilds the visuals");
        Assert.AreEqual("replaced", ((VC.TextBlock)control.Items[0]).Text);
    }

    [TestMethod]
    public void ItemsControl_DefaultFactoryProjectsTheItemToString()
    {
        using var surface = CreateSurface(160, 100);
        var control = new VC.ItemsControl { ItemsSource = new[] { "one", "two" } };
        surface.Root = control;
        Layout(surface, 160, 100);

        Assert.AreEqual(2, control.Items.Count);
        Assert.AreEqual("one", ((VC.TextBlock)control.Items[0]).Text);
    }

    [TestMethod]
    public void ListBox_SelectsByPointerAndKeyboardAndRaisesSelectionChanged()
    {
        var source = new ObservableCollection<string> { "alpha", "beta", "gamma" };
        using var surface = CreateSurface(220, 160);
        var list = new VC.ListBox { Width = 200, Height = 140, ItemsSource = source };
        var changes = 0;
        list.SelectionChanged += (_, _) => changes++;
        surface.Root = list;
        Layout(surface, 220, 160);

        Assert.AreEqual(-1, list.SelectedIndex);

        var row = list.Items[1];
        Click(
            surface,
            list.Bounds.X + (row.Bounds.X + (row.Bounds.Width / 2f)),
            list.Bounds.Y + (row.Bounds.Y + (row.Bounds.Height / 2f)));
        Assert.AreEqual(1, list.SelectedIndex, "the pointer selects the hit row");
        Assert.AreEqual("beta", list.SelectedItem);
        Assert.AreEqual(1, changes);

        surface.FocusElement(list);
        surface.ProcessKeyDown(Keys.Down);
        Assert.AreEqual(2, list.SelectedIndex);

        surface.ProcessKeyDown(Keys.Up);
        Assert.AreEqual(1, list.SelectedIndex);

        surface.ProcessKeyDown(Keys.Home);
        Assert.AreEqual(0, list.SelectedIndex);

        surface.ProcessKeyDown(Keys.End);
        Assert.AreEqual(2, list.SelectedIndex);

        list.SelectedItem = "alpha";
        Assert.AreEqual(0, list.SelectedIndex, "setting the item updates the index");
        Assert.AreEqual(6, changes);
    }

    [TestMethod]
    public void ListBox_ClearsAnOutOfRangeSelectionWhenTheItemsShrink()
    {
        var source = new ObservableCollection<string> { "alpha", "beta", "gamma" };
        using var surface = CreateSurface(220, 160);
        var list = new VC.ListBox { Width = 200, Height = 140, ItemsSource = source, SelectedIndex = 2 };
        surface.Root = list;
        Layout(surface, 220, 160);
        Assert.AreEqual("gamma", list.SelectedItem);

        source.RemoveAt(2);
        Assert.AreEqual(-1, list.SelectedIndex, "removing the selected item clears the selection");
        Assert.IsNull(list.SelectedItem);
    }

    [TestMethod]
    public void VirtualList_RealizesOnlyVisibleRowsAndFollowsTheScrollOffset()
    {
        var source = Enumerable.Range(0, 100).Select(index => $"Row {index}").ToList();
        using var surface = CreateSurface(220, 120);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var list = new VC.VirtualList { ItemsSource = source, ItemHeight = 20 };
        view.Content = list;
        surface.Root = view;
        Layout(surface, 220, 120);

        Assert.AreEqual(2000f, view.ExtentHeight, 0.1f, "the full extent is reported to the scroll viewer");
        Assert.AreEqual(0, list.FirstRealizedIndex);
        Assert.AreEqual(5, list.RealizedCount, "only the rows inside the 100px viewport are realized");
        Assert.IsTrue(list.Items.Count < source.Count);

        view.VerticalOffset = 200f;
        Layout(surface, 220, 120);

        Assert.AreEqual(10, list.FirstRealizedIndex, "a 200px offset starts at row 10");
        Assert.AreEqual(5, list.RealizedCount);
        Assert.AreEqual(200f, list.Items[0].Bounds.Y, 0.1f, "row 10 stays at its absolute position");
    }

    private static VisualSurface CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualSurface surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private static void Click(VisualSurface surface, float x, float y)
    {
        var point = new PointF(x, y);
        surface.ProcessPointerDown(point, MouseButtons.Left);
        surface.ProcessPointerUp(point, MouseButtons.Left);
    }

    private static PointF CenterOf(VisualElement element)
    {
        var bounds = element.AbsoluteBounds;
        return new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
    }
}
