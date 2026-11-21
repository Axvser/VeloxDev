using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The first WPF-parity batch as authored in markup: layout controls, data controls and a point-list shape.
/// </summary>
[STATestClass]
public sealed class BatchOneXamlTests
{
    [TestMethod]
    public void BatchOneView_ConstructsEveryNewControlFromMarkup()
    {
        using var view = new BatchOneView();
        var model = new ExtendedModel();
        model.Items.Add(new ProbeItem { Name = "one" });
        model.Items.Add(new ProbeItem { Name = "two" });
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        Assert.AreEqual("Group", view.Group.Header);
        Assert.AreEqual("More", view.Expander.Header);
        Assert.IsTrue(view.Expander.IsExpanded);
        Assert.HasCount(2, view.Tabs.Items);
        Assert.AreEqual(0, view.Tabs.SelectedIndex);

        Assert.AreEqual(2, view.Tree.NodeCount);
        Assert.AreEqual(2, view.Tree.FlattenedItems.Count);

        Assert.HasCount(1, view.List.View!.Columns);
        Assert.AreEqual(2, view.List.ItemCount);

        Assert.HasCount(1, view.DataGrid.Columns);
        Assert.AreEqual(2, view.DataGrid.ItemCount);

        Assert.AreEqual(new DateTime(2026, 11, 22), view.Calendar.SelectedDate);
        Assert.AreEqual(new DateTime(2026, 11, 22), view.DatePicker.SelectedDate);
        Assert.HasCount(3, view.Polygon.Points);
    }

    [TestMethod]
    public void BatchOneView_TwoWayDateBindingsWriteBack()
    {
        using var view = new BatchOneView();
        var model = new ExtendedModel { Date = new DateTime(2026, 1, 2) };
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        view.DatePicker.SelectedDate = new DateTime(2026, 2, 3);
        XamlTestSurface.Pump(20);

        Assert.AreEqual(new DateTime(2026, 2, 3), model.Date);
    }
}
