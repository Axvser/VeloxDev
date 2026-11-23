using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Cluster C of the full-parity templating pass: <see cref="VC.TreeView"/> and <see cref="VC.VirtualList"/> accept
/// WPF-shaped control templates while their no-template paths stay unchanged.
/// </summary>
[STATestClass]
public sealed class VisualTreeAndVirtualListTemplatingTests
{
    [TestMethod]
    public void TreeViewTemplate_HostsNestedRowsAndExposesSelectionAndExpansion()
    {
        using var surface = new VisualHost { Size = new Size(320, 240), DpiScaleOverride = 1f };
        using var tree = new VC.TreeView { Width = 300, Height = 200 };
        var group = new VC.TreeViewItem { Header = "Group", IsExpanded = true };
        var child = new VC.TreeViewItem { Header = "Child" };
        group.Items.Add(child);
        var second = new VC.TreeViewItem { Header = "Second" };
        tree.Items.Add(group);
        tree.Items.Add(second);

        var rowTemplate = CreateRowTemplate();
        var hoverTrigger = new VisualTrigger(VC.TreeViewItem.IsPointerOverProperty, true) { TargetName = "PART_Header" };
        hoverTrigger.Setters.Add(new VisualSetter(VC.TextBlock.ForegroundProperty, new SolidColorBrush(Color.Red)));
        rowTemplate.Triggers.Add(hoverTrigger);
        var pressTrigger = new VisualTrigger(VC.TreeViewItem.IsPressedProperty, true) { TargetName = "PART_Header" };
        pressTrigger.Setters.Add(new VisualSetter(VC.TextBlock.ForegroundProperty, new SolidColorBrush(Color.Gold)));
        rowTemplate.Triggers.Add(pressTrigger);

        group.Template = rowTemplate;
        child.Template = rowTemplate;
        second.Template = rowTemplate;

        VC.StackPanel? treeHost = null;
        var treeTemplate = new VisualControlTemplate(scope =>
        {
            treeHost = new VC.StackPanel();
            VC.ItemsControl.SetIsItemsHost(treeHost, true);
            scope.Register("PART_ItemsPresenter", treeHost);
            return treeHost;
        })
        {
            TargetType = typeof(VC.TreeView),
        };

        tree.Template = treeTemplate;
        surface.Root = tree;
        Layout(surface, 320, 240);

        Assert.AreSame(treeHost, tree.GetTemplateChild<VC.StackPanel>("PART_ItemsPresenter"));
        Assert.AreSame(treeHost, tree.TemplateRoot, "the host is the tree's template root");
        Assert.IsTrue(treeHost!.Children.Contains(group), "a root row is hosted by PART_ItemsPresenter");
        Assert.IsTrue(treeHost.Children.Contains(second), "the second root row is hosted too");

        var groupHost = group.GetTemplateChild<VC.ItemsPresenter>("PART_ItemsPresenter");
        Assert.IsNotNull(groupHost, "the row template's items presenter resolves through the row scope");
        Assert.IsTrue(groupHost!.Children.Contains(child), "an expanded row hosts its child rows");

        Assert.AreEqual("Group", group.GetTemplateChild<VC.TextBlock>("PART_Header")!.Text,
            "PART_Header receives the row header");

        var center = Center(second.AbsoluteBounds);
        var baseline = RenderPixel(surface, (int)center.X, (int)center.Y);

        Click(surface, center);

        Assert.AreSame(second, tree.SelectedItem);
        Assert.IsTrue(second.IsSelected, "the selected row exposes its state to template triggers");
        Assert.IsFalse(group.IsSelected, "the other row is not selected");

        var selected = RenderPixel(surface, (int)center.X, (int)center.Y);
        Assert.AreEqual(baseline.ToArgb(), selected.ToArgb(),
            "a templated tree must not paint its own selection band over the row template");

        var secondHeader = second.GetTemplateChild<VC.TextBlock>("PART_Header")!;
        Assert.IsNotNull(secondHeader);

        surface.ProcessPointerMove(center);
        Assert.IsTrue(second.IsPointerOver, "hover reaches the row's state dependency property");
        Assert.AreEqual(Color.Red.ToArgb(), secondHeader.Foreground.ToArgb(),
            "the hover trigger writes the named header part");

        surface.ProcessPointerDown(center, MouseButtons.Left);
        Assert.IsTrue(second.IsPressed, "a press reaches the row's state dependency property");
        Assert.AreEqual(Color.Gold.ToArgb(), secondHeader.Foreground.ToArgb(),
            "the press trigger wins while both states are active");
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.IsFalse(second.IsPressed, "the release clears the pressed state");
        Assert.AreEqual(Color.Red.ToArgb(), secondHeader.Foreground.ToArgb(),
            "releasing the press leaves the hover value in place");

        surface.ProcessPointerMove(new PointF(center.X, center.Y + 120f));
        Assert.IsFalse(second.IsPointerOver, "moving away clears hover");
        Assert.AreEqual(Color.Empty, secondHeader.Foreground, "the hover trigger removes its value on exit");

        var expander = group.GetTemplateChild<VC.Border>("PART_Expander");
        Assert.IsNotNull(expander, "PART_Expander resolves through the row scope");
        Click(surface, Center(expander!.AbsoluteBounds));

        Assert.IsFalse(group.IsExpanded, "clicking the expander part collapses the row");
        Assert.HasCount(2, tree.Rows, "the child row is no longer visible");
        Assert.IsFalse(groupHost.Children.Contains(child), "collapsing removes the child from the row presenter");

        tree.SelectedItem = group;
        Assert.IsTrue(tree.Focus());
        surface.ProcessKeyDown(Keys.Right);
        Assert.IsTrue(group.IsExpanded, "Right expands the selected row");
        Assert.IsTrue(groupHost.Children.Contains(child), "re-expanding reattaches the child row");

        tree.Template = null;
        Layout(surface, 320, 240);

        Assert.IsNull(tree.TemplateRoot, "clearing the template removes the root");
        Assert.IsNull(tree.GetTemplateChild<VC.StackPanel>("PART_ItemsPresenter"),
            "the part scope is cleared with the template");
        Assert.IsTrue(tree.Children.Contains(group), "rows return to direct children without a template");
        Assert.IsTrue(tree.Children.Contains(child), "the expanded child is a direct child again");
        Assert.IsTrue(tree.Children.Contains(second));
    }

    [TestMethod]
    public void TreeViewTemplate_WithoutAnItemsHostThrows()
    {
        using var tree = new VC.TreeView();
        var template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.TreeView),
        };

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => tree.Template = template);
        StringAssert.Contains(thrown.Message, "PART_ItemsPresenter",
            "the diagnostic names the missing host part");
    }

    [TestMethod]
    public void TreeViewItemTemplate_WithoutAHeaderPartThrows()
    {
        using var item = new VC.TreeViewItem { Header = "row" };
        var template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.TreeViewItem),
        };

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => item.Template = template);
        StringAssert.Contains(thrown.Message, "PART_Header",
            "the diagnostic names the missing header part");
    }

    [TestMethod]
    public void VirtualListTemplate_HostsVirtualizedRowsScrollingSelectionAndCleanup()
    {
        var rows = Enumerable.Range(0, 100).Select(static index => (object)$"Row {index}").ToList();
        using var surface = new VisualHost { Size = new Size(220, 120), DpiScaleOverride = 1f };
        using var viewer = new VC.ScrollViewer { Width = 200, Height = 100 };
        using var list = new VC.VirtualList { ItemsSource = rows, ItemHeight = 20 };
        viewer.Content = list;

        VC.StackPanel? host = null;
        var template = new VisualControlTemplate(scope =>
        {
            host = new VC.StackPanel();
            VC.ItemsControl.SetIsItemsHost(host, true);
            scope.Register("PART_ItemsPresenter", host);
            return host;
        })
        {
            TargetType = typeof(VC.VirtualList),
        };

        list.Template = template;
        surface.Root = viewer;
        Layout(surface, 220, 120);

        Assert.AreSame(host, list.GetTemplateChild<VC.StackPanel>("PART_ItemsPresenter"));
        Assert.AreSame(host, list.TemplateRoot, "the host is the list's template root");
        Assert.AreEqual(2000f, viewer.ExtentHeight, 0.1f, "the full fixed-row extent still reaches the viewer");
        Assert.AreEqual(0, list.FirstRealizedIndex);
        Assert.AreEqual(5, list.RealizedCount, "only the rows inside the viewport are realized");
        Assert.IsTrue(host!.Children.Contains(list.Items[0]), "realized rows live in the template host");
        Assert.AreEqual(0f, list.Items[0].Bounds.Y, 0.1f);

        Click(surface, Center(list.Items[1].AbsoluteBounds));

        Assert.AreEqual(1, list.SelectedIndex, "the pointer selects the hit row");
        Assert.AreEqual("Row 1", list.SelectedItem);

        viewer.VerticalOffset = 200f;
        Layout(surface, 220, 120);

        Assert.AreEqual(10, list.FirstRealizedIndex, "a 200px offset starts at row 10");
        Assert.AreEqual(5, list.RealizedCount);
        Assert.AreEqual(200f, list.Items[0].Bounds.Y, 0.1f, "row 10 stays at its fixed position");

        list.Template = null;
        Layout(surface, 220, 120);

        Assert.IsNull(list.TemplateRoot, "clearing the template removes the root");
        Assert.AreSame(list, list.Items[0].ParentElement, "rows return to direct children without a template");
        Assert.AreEqual(10, list.FirstRealizedIndex, "virtualization still starts at the scroll offset");
        Assert.AreEqual(5, list.RealizedCount, "only the visible rows are realized without a template");
    }

    [TestMethod]
    public void VirtualListTemplate_AcceptsAnItemsPanelHost()
    {
        var rows = Enumerable.Range(0, 50).Select(static index => (object)index).ToList();
        using var surface = new VisualHost { Size = new Size(220, 120), DpiScaleOverride = 1f };
        using var viewer = new VC.ScrollViewer { Width = 200, Height = 100 };
        using var list = new VC.VirtualList { ItemsSource = rows, ItemHeight = 20 };
        var panel = new VC.StackPanel();
        list.ItemsPanel = new VC.ItemsPanelTemplate(() => panel);
        list.Template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.VirtualList),
        };
        viewer.Content = list;
        surface.Root = viewer;
        Layout(surface, 220, 120);

        Assert.IsGreaterThan(0, list.RealizedCount);
        Assert.AreSame(panel, list.Items[0].ParentElement, "an ItemsPanel supplies the template host");
        Assert.AreEqual(1000f, viewer.ExtentHeight, 0.1f, "the fixed-row extent still reaches the viewer");
    }

    [TestMethod]
    public void VirtualListTemplate_WithoutAnItemsHostThrows()
    {
        using var list = new VC.VirtualList { ItemsSource = new[] { "a" } };
        var template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.VirtualList),
        };

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => list.Template = template);
        StringAssert.Contains(thrown.Message, "ItemsPresenter",
            "the diagnostic names the missing host part");
    }

    [TestMethod]
    public void TreeAndVirtualList_WithoutTemplatesKeepTheirDirectChildLayout()
    {
        using var tree = new VC.TreeView();
        var group = new VC.TreeViewItem { Header = "Group", IsExpanded = true };
        var child = new VC.TreeViewItem { Header = "Child" };
        group.Items.Add(child);
        tree.Items.Add(group);

        Assert.AreSame(tree, group.ParentElement, "a tree without a template keeps root rows as direct children");
        Assert.AreSame(tree, child.ParentElement, "an expanded child stays a flat direct child");

        var rows = Enumerable.Range(0, 30).Select(static index => (object)index).ToList();
        using var list = new VC.VirtualList
        {
            ItemsSource = rows,
            ItemHeight = 20,
            Width = 200,
            Height = 100,
        };

        list.Measure(new Size(200, 100));
        list.Arrange(new RectangleF(0f, 0f, 200f, 100f));

        Assert.AreSame(list, list.Items[0].ParentElement, "a list without a template keeps realized rows as children");
        Assert.IsGreaterThan(0, list.RealizedCount);
        Assert.IsLessThan(rows.Count, list.RealizedCount, "the no-template list still virtualizes");
    }

    private static VisualControlTemplate CreateRowTemplate()
        => new(scope =>
        {
            var root = new VC.StackPanel();
            var expander = new VC.Border { Width = 12f, Height = 12f };
            var header = new VC.TextBlock { Height = 18f };
            var presenter = new VC.ItemsPresenter();
            root.Add(expander);
            root.Add(header);
            root.Add(presenter);
            scope.Register("PART_Expander", expander);
            scope.Register("PART_Header", header);
            scope.Register("PART_ItemsPresenter", presenter);
            return root;
        })
        {
            TargetType = typeof(VC.TreeViewItem),
        };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private static Color RenderPixel(VisualHost surface, int x, int y)
    {
        using var bitmap = new Bitmap(surface.Size.Width, surface.Size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, surface.Size);
        }

        return bitmap.GetPixel(x, y);
    }

    private static void Click(VisualHost surface, PointF point)
    {
        surface.ProcessPointerDown(point, MouseButtons.Left);
        surface.ProcessPointerUp(point, MouseButtons.Left);
    }

    private static PointF Center(RectangleF bounds)
        => new(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
}
