using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The binding engine: the three modes, <see cref="INotifyPropertyChanged"/> updates, data-context inheritance,
/// native <see cref="Control.DataBindings"/> links and converters.
/// </summary>
[STATestClass]
public sealed class BindingTests
{
    [TestMethod]
    public void OneWay_AppliesTheSourceAndFollowsChangeNotifications()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();

        // 先挂绑定、后给上下文：生成的代码正是这个顺序。
        control.SetBinding(ProbeControl.NameProperty, new Binding("Name"));
        control.DataContext = viewModel;

        Assert.AreEqual("initial", control.Name);

        viewModel.Name = "updated";

        Assert.AreEqual("updated", control.Name);
    }

    [TestMethod]
    public void OneWay_WhenTheContextExistsFirst_AppliesImmediately()
    {
        using var control = new ProbeControl { DataContext = new ProbeViewModel { Name = "already" } };

        control.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        Assert.AreEqual("already", control.Name);
    }

    [TestMethod]
    public void OneTime_PushesTheValueOnceAndIgnoresLaterChanges()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;

        control.SetBinding(ProbeControl.NameProperty, new Binding("Name") { Mode = BindingMode.OneTime });
        Assert.AreEqual("initial", control.Name);

        viewModel.Name = "after";

        Assert.AreEqual("initial", control.Name);
    }

    [TestMethod]
    public void TwoWay_TargetWriteReachesTheSource()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;
        control.SetBinding(ProbeControl.NameProperty, new Binding("Name") { Mode = BindingMode.TwoWay });

        control.Name = "typed";

        Assert.AreEqual("typed", viewModel.Name);
        Assert.AreEqual("typed", control.Name);
    }

    [TestMethod]
    public void TwoWay_SourceChangeReachesTheTargetWithoutABounce()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;
        control.SetBinding(ProbeControl.NameProperty, new Binding("Name") { Mode = BindingMode.TwoWay });
        var changesBefore = control.Changes.Count;

        viewModel.Name = "from source";

        Assert.AreEqual("from source", control.Name);
        Assert.HasCount(changesBefore + 1, control.Changes);
        Assert.AreEqual("from source", control.Changes[^1].NewValue);
    }

    [TestMethod]
    public void DataContext_IsInheritedDownTheControlTreeAndFollowsAncestorChanges()
    {
        using var parent = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var child = new ProbeControl();
        parent.Controls.Add(child);

        child.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        parent.DataContext = new ProbeViewModel { Name = "from parent" };
        Assert.AreEqual("from parent", child.Name);

        parent.DataContext = new ProbeViewModel { Name = "second parent" };
        Assert.AreEqual("second parent", child.Name);
    }

    [TestMethod]
    public void DataContext_ALocalValueStopsInheritance()
    {
        using var parent = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var child = new ProbeControl();
        parent.Controls.Add(child);

        child.SetBinding(ProbeControl.NameProperty, new Binding("Name"));
        child.DataContext = new ProbeViewModel { Name = "local" };
        parent.DataContext = new ProbeViewModel { Name = "parent" };

        Assert.AreEqual("local", child.Name);

        parent.DataContext = new ProbeViewModel { Name = "changed parent" };
        Assert.AreEqual("local", child.Name);
    }

    [TestMethod]
    public void DataContext_ADescendantWithoutALocalValueStillFollowsAnAncestorWhenOneSitsBetween()
    {
        using var parent = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var middle = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var leaf = new ProbeControl();
        parent.Controls.Add(middle);
        middle.Controls.Add(leaf);

        leaf.SetBinding(ProbeControl.NameProperty, new Binding("Name"));
        parent.DataContext = new ProbeViewModel { Name = "through the middle" };

        Assert.AreEqual("through the middle", leaf.Name);
    }

    [TestMethod]
    public void ADottedPath_ResolvesNestedObjectsAndObservesThem()
    {
        using var control = new ProbeControl();
        var child = new ProbeViewModel { Name = "nested" };
        var viewModel = new ProbeViewModel { Child = child };
        control.DataContext = viewModel;

        control.SetBinding(ProbeControl.NameProperty, new Binding("Child.Name"));
        Assert.AreEqual("nested", control.Name);

        child.Name = "nested changed";

        Assert.AreEqual("nested changed", control.Name);
    }

    [TestMethod]
    public void ClearBinding_StopsFollowingTheSource()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel();
        control.DataContext = viewModel;
        control.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        control.ClearBinding(ProbeControl.NameProperty);
        viewModel.Name = "ignored";

        Assert.AreEqual("initial", control.Name);
    }

    [TestMethod]
    public void NativeBinding_GoesThroughControlDataBindings()
    {
        using var host = new ProbeControl();
        using var label = new Label();
        host.Controls.Add(label);
        // WinForms 的绑定靠上下文激活：真实宿主由 Form 提供，这里的无头宿主自己装一个。
        host.BindingContext = new BindingContext();
        host.DataContext = new ProbeViewModel { Name = "native" };

        host.SetNativeBinding(label, "Text", new Binding("Name"));

        // WinForms 只在控件拿到句柄后才真正激活 DataBindings；无头测试因此要显式建句柄。
        XamlTestSurface.EnsureHandle(label);

        Assert.HasCount(1, label.DataBindings);
        Assert.AreEqual("native", label.Text);
    }

    [TestMethod]
    public void NativeBinding_StaysPendingUntilADataContextExists()
    {
        using var host = new ProbeControl();
        using var label = new Label();
        host.Controls.Add(label);
        host.BindingContext = new BindingContext();

        host.SetNativeBinding(label, "Text", new Binding("Name"));
        Assert.IsEmpty(label.DataBindings);

        XamlTestSurface.EnsureHandle(label);
        host.DataContext = new ProbeViewModel { Name = "late" };

        Assert.HasCount(1, label.DataBindings);
        Assert.AreEqual("late", label.Text);
    }

    [TestMethod]
    public void NativeBinding_OneTime_SetsThePropertyWithoutADataBinding()
    {
        using var host = new ProbeControl();
        using var label = new Label();
        host.Controls.Add(label);
        host.BindingContext = new BindingContext();
        var viewModel = new ProbeViewModel { Name = "one time" };
        host.DataContext = viewModel;

        host.SetNativeBinding(label, "Text", new Binding("Name") { Mode = BindingMode.OneTime });
        viewModel.Name = "later";

        Assert.AreEqual("one time", label.Text);
        Assert.IsEmpty(label.DataBindings);
    }

    [TestMethod]
    public void NativeBinding_TwoWay_WritesTheNativeValueBackToTheSource()
    {
        using var host = new ProbeControl();
        using var textBox = new TextBox();
        host.Controls.Add(textBox);
        host.BindingContext = new BindingContext();
        var viewModel = new ProbeViewModel();
        host.DataContext = viewModel;

        host.SetNativeBinding(textBox, "Text", new Binding("Name") { Mode = BindingMode.TwoWay });
        XamlTestSurface.EnsureHandle(textBox);
        textBox.Text = "typed natively";
        XamlTestSurface.Pump(30);

        Assert.AreEqual("typed natively", viewModel.Name);
    }

    [TestMethod]
    public void AConverter_IsUsedInBothDirectionsOnADependencyPropertyBinding()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel { Name = "value" };
        var converter = new BracketConverter();
        control.DataContext = viewModel;

        control.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Name") { Mode = BindingMode.TwoWay, Converter = converter });

        Assert.AreEqual("[value]", control.Name);
        Assert.AreEqual(1, converter.ConvertCalls);

        viewModel.Name = "next";
        Assert.AreEqual("[next]", control.Name);

        control.Name = "manual";

        Assert.AreEqual("manual", viewModel.Name);
        Assert.AreEqual(1, converter.ConvertBackCalls);
    }

    [TestMethod]
    public void RefreshBindings_ReResolvesAfterAnExternalSourceReplacement()
    {
        using var control = new ProbeControl();
        var viewModel = new ProbeViewModel { Name = "before" };
        control.DataContext = viewModel;
        control.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        viewModel.Name = "after";
        control.RefreshBindings();

        Assert.AreEqual("after", control.Name);
    }
}
