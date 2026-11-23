using System.ComponentModel;
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>WPF-style path bindings over the visual backend's <see cref="IWinformsObject"/> targets.</summary>
[STATestClass]
public sealed class VisualBindingTests
{
    [TestMethod]
    public void OneWayBinding_FollowsTheSource()
    {
        var model = new Model { Name = "first" };
        var text = new VC.TextBlock();
        text.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model });

        Assert.AreEqual("first", text.Text);

        model.Name = "second";
        Assert.AreEqual("second", text.Text);
    }

    [TestMethod]
    public void Binding_UsesTheDataContextAndReattaches()
    {
        var first = new Model { Name = "first" };
        var second = new Model { Name = "second" };
        var text = new VC.TextBlock();
        text.SetBinding(VC.TextBlock.TextProperty, new Binding(nameof(Model.Name)));

        text.DataContext = first;
        Assert.AreEqual("first", text.Text);

        text.DataContext = second;
        Assert.AreEqual("second", text.Text);
    }

    [TestMethod]
    public void DataContextBinding_FollowsTheParentContext()
    {
        var first = new Model { Name = "first" };
        var second = new Model { Name = "second" };
        var parent = new VC.Grid { DataContext = new ParentModel { Child = first } };
        var child = new VC.TextBlock();
        child.SetBinding(VC.TextBlock.TextProperty, new Binding(nameof(Model.Name)));
        VisualBindingOperations.Bind(
            child,
            VisualElement.DataContextProperty,
            new Binding(nameof(ParentModel.Child)));
        parent.Add(child);

        Assert.AreSame(first, child.DataContext);
        Assert.AreEqual("first", child.Text);

        // The child's DataContext binding resolves against the parent's context; a reused view must see the new one.
        parent.DataContext = new ParentModel { Child = second };

        Assert.AreSame(second, child.DataContext);
        Assert.AreEqual("second", child.Text);
    }

    [TestMethod]
    public void TwoWayBinding_WritesBackToTheSource()
    {
        var model = new Model { Name = "source" };
        var box = new VC.TextBox();
        box.SetBinding(
            VC.TextBox.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model, Mode = BindingMode.TwoWay });

        Assert.AreEqual("source", box.Text);

        box.Text = "target";
        Assert.AreEqual("target", model.Name);
    }

    [TestMethod]
    public void Dispose_DetachesTheBinding()
    {
        var model = new Model { Name = "first" };
        var text = new VC.TextBlock();
        var subscription = text.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model });

        subscription.Dispose();
        model.Name = "second";

        Assert.AreEqual("first", text.Text);
    }

    [TestMethod]
    public void OneTime_DoesNotFollowLaterChanges()
    {
        var model = new Model { Name = "first" };
        var text = new VC.TextBlock();
        text.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model, Mode = BindingMode.OneTime });

        model.Name = "second";

        Assert.AreEqual("first", text.Text);
    }

    [TestMethod]
    public void StringFormat_FallbackAndNullValue_AreApplied()
    {
        var model = new Model { Count = 7, Name = null };
        var count = new VC.TextBlock();
        count.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(Model.Count)) { Source = model, StringFormat = "Count: {0}" });
        Assert.AreEqual("Count: 7", count.Text);

        var missing = new VC.TextBlock();
        missing.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding("Missing") { Source = model, FallbackValue = "fallback" });
        Assert.AreEqual("fallback", missing.Text);

        var name = new VC.TextBlock();
        name.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model, TargetNullValue = "(none)" });
        Assert.AreEqual("(none)", name.Text);
    }

    private sealed class ParentModel
    {
        public Model? Child { get; set; }
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private string? _name;
        private int _count;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Count
        {
            get => _count;
            set
            {
                if (_count == value)
                {
                    return;
                }

                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
        }
    }
}
