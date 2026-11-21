using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Binding features added on top of the original three modes: converter parameters, format and null fallbacks,
/// indexers, source selection, write-back timing and style-setter bindings.
/// </summary>
[STATestClass]
public sealed class ExtendedBindingTests
{
    [TestMethod]
    public void ConverterParameter_IsPassedToTheConverter()
    {
        using var control = new ProbeControl { DataContext = new ProbeViewModel { Name = "value" } };
        control.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Name") { Converter = new ParameterConverter(), ConverterParameter = "!" });

        Assert.AreEqual("value!", control.Name);
    }

    [TestMethod]
    public void StringFormat_FormatsTheConvertedValue()
    {
        using var control = new ProbeControl { DataContext = new ProbeViewModel { Amount = 1.5d } };
        control.SetBinding(ProbeControl.CaptionProperty, new Binding("Amount") { StringFormat = "N2" });

        Assert.AreEqual(1.5d.ToString("N2", CultureInfo.CurrentCulture), control.Caption);
    }

    [TestMethod]
    public void FallbackValue_IsUsedWhenThePathCannotBeResolved()
    {
        using var control = new ProbeControl { DataContext = new ProbeViewModel() };
        control.SetBinding(ProbeControl.NameProperty, new Binding("Missing") { FallbackValue = "fallback" });

        Assert.AreEqual("fallback", control.Name);
    }

    [TestMethod]
    public void TargetNullValue_ReplacesANullSourceValue()
    {
        using var control = new ProbeControl
        {
            DataContext = new ProbeViewModel { OptionalName = null },
        };
        control.SetBinding(ProbeControl.NameProperty, new Binding("OptionalName") { TargetNullValue = "null" });

        Assert.AreEqual("null", control.Name);
    }

    [TestMethod]
    public void AnIndexerPath_ReadsAndWritesTheCollectionItem()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        var item = new ProbeItem { Name = "first" };
        viewModel.Items.Add(item);
        control.DataContext = viewModel;

        control.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Items[0].Name") { Mode = BindingMode.TwoWay });

        Assert.AreEqual("first", control.Name);

        item.Name = "from source";
        Assert.AreEqual("from source", control.Name);

        control.Name = "from target";
        Assert.AreEqual("from target", item.Name);
    }

    [TestMethod]
    public void UpdateSourceTrigger_Explicit_WaitsForTheHost()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;
        control.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });

        control.Name = "typed";
        Assert.AreEqual("initial", viewModel.Name);

        Assert.IsTrue(control.UpdateSource(ProbeControl.NameProperty));
        Assert.AreEqual("typed", viewModel.Name);
    }

    [TestMethod]
    public void NativeBinding_Explicit_WritesOnlyWhenAsked()
    {
        using var host = new ProbeControl();
        using var textBox = new System.Windows.Forms.TextBox();
        host.Controls.Add(textBox);
        host.BindingContext = new System.Windows.Forms.BindingContext();
        var viewModel = new ProbeViewModel();
        host.DataContext = viewModel;

        host.SetNativeBinding(
            textBox,
            "Text",
            new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        XamlTestSurface.EnsureHandle(textBox);

        textBox.Text = "typed";
        XamlTestSurface.Pump(20);
        Assert.AreEqual("initial", viewModel.Name);

        Assert.IsTrue(host.UpdateSource("Text"));
        Assert.AreEqual("typed", viewModel.Name);
    }

    [TestMethod]
    public void UpdateSourceTrigger_LostFocus_WritesWhenFocusLeaves()
    {
        using var control = new FocusProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;
        control.SetBinding(
            FocusProbeControl.TextProperty,
            new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });

        control.Text = "typed";
        Assert.AreEqual("initial", viewModel.Name);

        control.RaiseLostFocus();
        Assert.AreEqual("typed", viewModel.Name);
    }

    [TestMethod]
    public void ElementName_ResolvesThroughTheNameScopeAndFollowsChanges()
    {
        using var root = new VeloxDev.WinForms.Xaml.Controls.Control
        {
            NameScope = new NameScope(),
        };
        using var source = new ProbeControl { Name = "source" };
        using var target = new ProbeControl();
        root.Controls.Add(source);
        root.Controls.Add(target);
        root.NameScope!.Register("source", source);

        target.SetBinding(ProbeControl.NameProperty, new Binding("Name") { ElementName = "source" });
        Assert.AreEqual("source", target.Name);

        source.Name = "updated";
        Assert.AreEqual("updated", target.Name);
    }

    [TestMethod]
    public void RelativeSource_SelfReadsTheOwningControl()
    {
        using var control = new ProbeControl { Caption = "caption" };
        control.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Caption") { RelativeSource = RelativeSource.Self });

        Assert.AreEqual("caption", control.Name);

        control.Caption = "changed";
        Assert.AreEqual("changed", control.Name);
    }

    [TestMethod]
    public void RelativeSource_FindAncestorReadsTheParent()
    {
        using var parent = new ProbeControl { Name = "parent" };
        using var child = new ProbeControl();
        parent.Controls.Add(child);

        child.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Name")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(ProbeControl),
                },
            });

        Assert.AreEqual("parent", child.Name);
    }

    [TestMethod]
    public void AStyleSetter_CanUseABinding()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel { Caption = "one" };
        control.DataContext = viewModel;

        var style = new Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.NameProperty, new Binding("Caption")));
        control.Style = style;

        Assert.AreEqual("one", control.Name);

        viewModel.Caption = "two";
        Assert.AreEqual("two", control.Name);
    }

    [TestMethod]
    public void ATriggerSetter_CanUseABinding()
    {
        using var control = new ProbeControl { Name = "initial" };
        var viewModel = new ProbeViewModel { Caption = "one" };
        control.DataContext = viewModel;

        var trigger = new Trigger(ProbeControl.IsHotProperty, true);
        trigger.Setters.Add(new Setter(ProbeControl.NameProperty, new Binding("Caption")));
        control.Triggers.Add(trigger);

        control.IsHot = true;
        Assert.AreEqual("one", control.Name);

        viewModel.Caption = "two";
        Assert.AreEqual("two", control.Name);

        control.IsHot = false;
        Assert.AreEqual("initial", control.Name);
    }

    [TestMethod]
    public void ATriggerActionGroup_RunsEveryAction()
    {
        using var control = new ProbeControl();
        var first = new RecordingTriggerAction();
        var second = new RecordingTriggerAction();
        var group = new TriggerActionGroup();
        group.Actions.Add(first);
        group.Actions.Add(second);

        group.Invoke(control);

        Assert.AreEqual(1, first.Invocations);
        Assert.AreEqual(1, second.Invocations);
    }

    [TestMethod]
    public void VirtualizingItemsControl_RealizesOnlyTheViewport()
    {
        using var scroll = new ScrollViewer
        {
            Size = new System.Drawing.Size(200, 100),
            ViewportHeight = 100,
            ExtentHeight = 2000,
        };
        using var items = new ItemsControl
        {
            ItemsSource = Enumerable.Range(0, 100).ToArray(),
            Virtualizing = true,
            ItemHeight = 20,
        };
        scroll.Controls.Add(items);
        XamlTestSurface.EnsureHandle(scroll);
        items.PerformLayout();

        Assert.IsTrue(items.RealizedItemCount <= 10, $"realized {items.RealizedItemCount} items");

        scroll.ScrollOffset = new System.Drawing.Point(0, 500);
        XamlTestSurface.Pump(30);
        items.PerformLayout();

        Assert.IsTrue(items.RealizedItemCount <= 10, $"realized {items.RealizedItemCount} items");
        Assert.IsTrue(items.Controls.OfType<TextBlock>().Any(static text => text.Text == "25"));
    }

    [TestMethod]
    public void DeclarativeContextMenu_InvokesTheGeneratedHandler()
    {
        using var view = new ExtendedView();
        XamlTestSurface.EnsureHandle(view);

        view.MenuButton.ContextMenu!.Items[0].PerformClick();

        Assert.AreEqual(1, view.MenuClicks);
    }

    [TestMethod]
    public void DeclarativePopup_OpensAndCloses()
    {
        using var view = new ExtendedView();
        XamlTestSurface.EnsureHandle(view);

        try
        {
            view.Popup.IsOpen = true;
            Assert.IsTrue(view.Popup.IsOpen);
        }
        finally
        {
            view.Popup.IsOpen = false;
        }

        Assert.IsFalse(view.Popup.IsOpen);
    }
}
