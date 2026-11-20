using System.Collections.ObjectModel;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// DataTrigger paths through observable collections: Add/Remove/Replace/Move/Reset, index shifts, nested collections,
/// collection replacement, and unsubscription when the trigger is removed.
/// </summary>
[STATestClass]
public sealed class DataTriggerCollectionTests
{
    [TestMethod]
    public void AddAndRemove_ReevaluatesThePath()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        control.DataContext = model;
        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(0, control.BorderWidth);

        model.Items.Add(new ProbeItem { Name = "first" });
        Assert.AreEqual(4, control.BorderWidth);

        model.Items.Clear();
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void ReplaceAndIndexShift_ReevaluatesThePath()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        model.Items.Add(new ProbeItem { Name = "first" });
        model.Items.Add(new ProbeItem { Name = "second" });
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "second" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(0, control.BorderWidth);

        model.Items.RemoveAt(0);
        Assert.AreEqual(4, control.BorderWidth, "second shifted to index 0");

        model.Items.Insert(0, new ProbeItem { Name = "first" });
        Assert.AreEqual(0, control.BorderWidth, "second shifted back to index 1");

        model.Items[0] = new ProbeItem { Name = "second" };
        Assert.AreEqual(4, control.BorderWidth, "Replace at index 0 updates the path");
    }

    [TestMethod]
    public void Move_ReevaluatesThePath()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        model.Items.Add(new ProbeItem { Name = "first" });
        model.Items.Add(new ProbeItem { Name = "second" });
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "second" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(0, control.BorderWidth);

        model.Items.Move(1, 0);
        Assert.AreEqual(4, control.BorderWidth);

        model.Items.Move(0, 1);
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void Reset_ReevaluatesAndDropsStaleElements()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        var first = new ProbeItem { Name = "first" };
        model.Items.Add(first);
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);
        Assert.AreEqual(4, control.BorderWidth);

        model.Items.Clear();
        Assert.AreEqual(0, control.BorderWidth);

        first.Name = "changed";
        Assert.AreEqual(0, control.BorderWidth, "the removed element must no longer be observed");

        model.Items.Add(new ProbeItem { Name = "first" });
        Assert.AreEqual(4, control.BorderWidth);
    }

    [TestMethod]
    public void NestedCollection_ReevaluatesAndFollowsIndexShifts()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        var item = new ProbeItem { Name = "parent" };
        model.Items.Add(item);
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Children[0].Name", Value = "child" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(0, control.BorderWidth);

        item.Children.Add(new ProbeItem { Name = "child" });
        Assert.AreEqual(4, control.BorderWidth);

        item.Children.Insert(0, new ProbeItem { Name = "other" });
        Assert.AreEqual(0, control.BorderWidth, "child shifted to index 1");

        item.Children.RemoveAt(0);
        Assert.AreEqual(4, control.BorderWidth, "child shifted back to index 0");

        item.Children.Clear();
        Assert.AreEqual(0, control.BorderWidth);
    }

    [TestMethod]
    public void ReplacingTheCollection_ReattachesToTheNewCollection()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        model.Items.Add(new ProbeItem { Name = "first" });
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);
        Assert.AreEqual(4, control.BorderWidth);

        model.Items = new ObservableCollection<ProbeItem> { new() { Name = "second" } };
        Assert.AreEqual(0, control.BorderWidth);

        model.Items = new ObservableCollection<ProbeItem> { new() { Name = "first" } };
        Assert.AreEqual(4, control.BorderWidth, "the new collection is observed");
    }

    [TestMethod]
    public void RemovingTheTrigger_UnsubscribesFromTheCollection()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        model.Items.Add(new ProbeItem { Name = "first" });
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);
        Assert.AreEqual(4, control.BorderWidth);

        control.Triggers.Remove(trigger);
        Assert.AreEqual(0, control.BorderWidth);

        model.Items.Clear();
        model.Items.Add(new ProbeItem { Name = "first" });

        Assert.AreEqual(0, control.BorderWidth, "a removed trigger must not react to collection changes");
    }

    [TestMethod]
    public void APlainListWithoutNotifications_RemainsStatic()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "PlainItems[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        model.PlainItems.Add(new ProbeItem { Name = "first" });

        Assert.AreEqual(0, control.BorderWidth, "a plain List has no collection notifications");
    }
}
