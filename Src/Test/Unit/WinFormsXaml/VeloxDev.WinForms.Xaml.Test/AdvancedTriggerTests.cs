using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// DataTrigger and MultiTrigger on a control's own trigger collection: source observation, context reattachment,
/// all-conditions evaluation and interaction with each other.
/// </summary>
[STATestClass]
public sealed class AdvancedTriggerTests
{
    [TestMethod]
    public void ControlDataTrigger_FollowsTheDataContextAndReattaches()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        view.BindingContext = new BindingContext();
        var model = new GeneratedModel();
        view.DataContext = model;

        Assert.AreEqual("control-data", card.Note, "the DataTrigger on Title is active");

        model.Title = "other";
        Assert.IsNull(card.Note);

        model.Title = "generated title";
        Assert.AreEqual("control-data", card.Note);

        view.DataContext = new GeneratedModel { Title = "generated title" };
        Assert.AreEqual("control-data", card.Note, "a new data context reattaches the trigger");
    }

    [TestMethod]
    public void ControlMultiTrigger_RequiresAllConditionsAndRestores()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();

        card.IsHot = true;
        Assert.IsNull(card.Note, "IsPinned is still false");

        card.IsPinned = true;
        Assert.AreEqual("control-multi", card.Note);

        card.IsHot = false;
        Assert.IsNull(card.Note);

        card.IsHot = true;
        Assert.AreEqual("control-multi", card.Note);
    }

    [TestMethod]
    public void ControlDataTriggerAndMultiTrigger_PrecedenceAndExit()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();
        view.BindingContext = new BindingContext();
        view.DataContext = new GeneratedModel();

        Assert.AreEqual("control-data", card.Note);

        card.IsHot = true;
        card.IsPinned = true;
        Assert.AreEqual("control-multi", card.Note, "the later MultiTrigger wins");

        card.IsPinned = false;
        Assert.AreEqual("control-data", card.Note, "exiting the MultiTrigger reveals the DataTrigger");

        view.DataContext = new GeneratedModel { Title = "other" };
        Assert.IsNull(card.Note);
    }

    [TestMethod]
    public void DataTrigger_PathForm_ObservesINotifyPropertyChanged()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        control.DataContext = model;

        var trigger = new DataTrigger { Path = nameof(ProbeViewModel.Name), Value = "initial" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(4, control.BorderWidth);

        model.Name = "other";
        Assert.AreEqual(0, control.BorderWidth);

        model.Name = "initial";
        Assert.AreEqual(4, control.BorderWidth);
    }

    [TestMethod]
    public void DataTrigger_SimpleIndexerPath_ObservesTheElement()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        var item = new ProbeItem { Name = "first" };
        model.Items.Add(item);
        control.DataContext = model;

        var trigger = new DataTrigger { Path = "Items[0].Name", Value = "first" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);

        Assert.AreEqual(4, control.BorderWidth);

        item.Name = "second";
        Assert.AreEqual(0, control.BorderWidth);

        item.Name = "first";
        Assert.AreEqual(4, control.BorderWidth);
    }

    [TestMethod]
    public void RemovingAStackedDataTrigger_DetachesItsObserver()
    {
        using var control = new ProbeControl();
        var model = new ProbeViewModel();
        control.DataContext = model;

        var trigger = new DataTrigger { Binding = new Binding(nameof(ProbeViewModel.Name)), Value = "initial" };
        trigger.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        control.Triggers.Add(trigger);
        Assert.AreEqual(4, control.BorderWidth);

        control.Triggers.Remove(trigger);
        Assert.AreEqual(0, control.BorderWidth);

        model.Name = "other";
        model.Name = "initial";

        Assert.AreEqual(0, control.BorderWidth, "a removed DataTrigger must not react to source changes");
    }
}
