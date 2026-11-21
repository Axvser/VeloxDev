using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The first batch of WPF-parity data and selection controls: hierarchical trees, details lists, tables, calendars
/// and value-based selection.
/// </summary>
[STATestClass]
public sealed class DataControlTests
{
    [TestMethod]
    public void TreeView_BuildsNodesFromAHierarchicalSource()
    {
        var root = new Node("root", new Node("alpha"), new Node("beta", new Node("gamma")));
        using var tree = new V.TreeView
        {
            DisplayMemberPath = "Name",
            ItemsSource = new[] { root },
        };

        Assert.AreEqual(4, tree.NodeCount);
        Assert.AreEqual(4, tree.FlattenedItems.Count);
        Assert.AreSame(root, tree.FlattenedItems[0]);
        Assert.AreSame(root.Children[1], tree.FlattenedItems[2]);

        tree.SelectedIndex = 3;
        Assert.AreSame(root.Children[1].Children[0], tree.SelectedItem);
    }

    [TestMethod]
    public void ListView_BuildsColumnsAndRowsFromTheViewAndSource()
    {
        var first = new Node("alpha");
        var second = new Node("beta");

        var view = new V.GridView();
        view.Columns.Add(new V.GridViewColumn { Header = "Name", DisplayMemberBinding = "Name", Width = 140 });
        view.Columns.Add(new V.GridViewColumn { Header = "Tag", DisplayMemberBinding = "Tag", Width = 80 });

        using var list = new V.ListView { View = view };
        list.ItemsSource = new[] { first, second };
        XamlTestSurface.EnsureHandle(list);

        Assert.AreEqual(2, list.View!.Columns.Count);
        Assert.AreEqual("Name", list.View.Columns[0].Header);
        Assert.AreEqual(140, list.View.Columns[0].Width);
        Assert.AreEqual(2, list.ItemCount);

        list.SelectedIndex = 1;
        Assert.AreSame(second, list.SelectedItem);
    }

    [TestMethod]
    public void DataGrid_BuildsDeclaredColumnsAndRows()
    {
        var first = new Node("alpha");
        var second = new Node("beta");

        using var grid = new V.DataGrid { AutoGenerateColumns = false };
        grid.Columns.Add(new V.DataGridColumn { Header = "Name", Binding = "Name", Width = 120 });
        grid.ItemsSource = new[] { first, second };
        XamlTestSurface.EnsureHandle(grid);

        Assert.AreEqual(1, grid.Columns.Count);
        Assert.AreEqual(1, grid.ColumnCount);
        Assert.AreEqual(2, grid.ItemCount);

        grid.SelectedIndex = 1;
        Assert.AreSame(second, grid.SelectedItem);
    }

    [TestMethod]
    public void Calendar_KeepsSelectedAndDisplayDates()
    {
        using var calendar = new V.Calendar();
        XamlTestSurface.EnsureHandle(calendar);

        var selected = new DateTime(2024, 5, 6);
        calendar.SelectedDate = selected;
        Assert.AreEqual(selected, calendar.SelectedDate);

        var display = new DateTime(2024, 7, 1);
        calendar.SelectedDate = null;
        calendar.DisplayDate = display;
        Assert.AreEqual(display, calendar.DisplayDate);
        Assert.IsNull(calendar.SelectedDate);
    }

    [TestMethod]
    public void DatePicker_ParsesTextAndFormatsSelectedDate()
    {
        using var picker = new V.DatePicker { SelectedDateFormat = "yyyy-MM-dd" };
        XamlTestSurface.EnsureHandle(picker);

        picker.SelectedDate = new DateTime(2024, 5, 6);
        Assert.AreEqual("2024-05-06", picker.Text);

        picker.Text = "2024-05-07";
        Assert.AreEqual(new DateTime(2024, 5, 7), picker.SelectedDate);
    }

    [TestMethod]
    public void Selector_SelectedValueFollowsSelectedValuePath()
    {
        var alpha = new Node("alpha");
        var beta = new Node("beta");

        using var list = new V.ListBox { SelectedValuePath = "Name" };
        list.ItemsSource = new[] { alpha, beta };

        list.SelectedValue = "beta";
        Assert.AreEqual(1, list.SelectedIndex);
        Assert.AreSame(beta, list.SelectedItem);
        Assert.AreEqual("beta", list.SelectedValue);

        list.SelectedItem = alpha;
        Assert.AreEqual("alpha", list.SelectedValue);
        Assert.AreEqual(0, list.SelectedIndex);
    }

    [TestMethod]
    public void ListBox_SelectedItemsMirrorsANativeMultiSelection()
    {
        using var list = new V.ListBox
        {
            SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended,
            ItemsSource = new[] { "a", "b", "c" },
        };
        XamlTestSurface.EnsureHandle(list);

        list.SelectAll();

        Assert.AreEqual(3, list.SelectedItems.Count);
    }

    [TestMethod]
    public void ItemsControl_UsesTheItemTemplateSelector()
    {
        using var control = new V.ItemsControl
        {
            ItemsSource = new[] { "a", "b" },
            ItemTemplateSelector = new UpperTemplateSelector(),
        };

        control.PerformLayout();

        Assert.AreEqual(2, control.Controls.Count);
        Assert.IsTrue(
            control.Controls.Cast<System.Windows.Forms.Control>().All(child => child is V.TextBlock { Text: "templated" }));
    }

    public sealed class Node
    {
        public Node(string name, params Node[] children)
        {
            Name = name;
            Children = new List<Node>(children);
        }

        public string Name { get; }

        public string Tag => Name.ToUpperInvariant();

        public List<Node> Children { get; }
    }

    private sealed class UpperTemplateSelector : VeloxDev.WinForms.Xaml.TemplateSelector
    {
        public override VeloxDev.WinForms.Xaml.DataTemplate? SelectTemplate(object? item)
            => item is string ? new VeloxDev.WinForms.Xaml.DataTemplate(() => new V.TextBlock { Text = "templated" }) : null;
    }
}
