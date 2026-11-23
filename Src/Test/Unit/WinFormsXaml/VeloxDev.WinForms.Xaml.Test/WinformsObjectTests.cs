using System.ComponentModel;
using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The AvaloniaObject-style property system: priority layers, attached values, inheritance and observable bindings.
/// </summary>
[STATestClass]
public sealed class WinformsObjectTests
{
    [TestMethod]
    public void LocalValue_BeatsTheDefaultAndClearsBack()
    {
        var model = new Model();
        var changes = new List<DependencyPropertyChangedEventArgs>();
        model.PropertyChanged += (_, e) => changes.Add(e);

        Assert.AreEqual("default", model.Name);

        model.Name = "local";
        Assert.AreEqual("local", model.Name);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual("default", changes[0].OldValue);
        Assert.AreEqual("local", changes[0].NewValue);

        model.ClearValue(Model.NameProperty);
        Assert.AreEqual("default", model.Name);
        Assert.AreEqual(2, changes.Count);
    }

    [TestMethod]
    public void PriorityLayers_ResolveFromLowestToHighest()
    {
        var model = new Model();
        model.SetValue(Model.NameProperty, "local", BindingPriority.LocalValue);
        model.SetValue(Model.NameProperty, "style", BindingPriority.Style);
        Assert.AreEqual("local", model.Name);

        model.SetValue(Model.NameProperty, "animation", BindingPriority.Animation);
        Assert.AreEqual("animation", model.Name);

        model.ClearValue(Model.NameProperty, BindingPriority.Animation);
        Assert.AreEqual("local", model.Name);

        model.ClearValue(Model.NameProperty, BindingPriority.LocalValue);
        Assert.AreEqual("style", model.Name);
    }

    [TestMethod]
    public void AttachedValues_ArePerObject()
    {
        var first = new VC.Border();
        var second = new VC.Border();

        VC.Grid.SetRow(first, 2);

        Assert.AreEqual(2, VC.Grid.GetRow(first));
        Assert.AreEqual(0, VC.Grid.GetRow(second));
    }

    [TestMethod]
    public void DataContext_InheritsAndStopsAtALocalValue()
    {
        var root = new VC.StackPanel();
        var child = new VC.Border { Padding = VeloxDev.WinForms.Xaml.Controls.Thickness.Empty };
        var grandchild = new VC.StackPanel();
        root.Add(child);
        child.Child = grandchild;

        root.DataContext = "root";
        Assert.AreEqual("root", grandchild.DataContext);

        var observed = new List<object?>();
        using var subscription = grandchild.GetObservable<object?>(VisualElement.DataContextProperty).Subscribe(
            new Observer<object?>(observed.Add));

        child.DataContext = "child";
        Assert.AreEqual("child", grandchild.DataContext);
        CollectionAssert.AreEqual(new object?[] { "root", "child" }, observed);

        root.DataContext = "next";
        Assert.AreEqual("child", grandchild.DataContext, "a local value stops inheritance");
    }

    [TestMethod]
    public void Bind_PushesAnObservableAndRestoresOnDispose()
    {
        var model = new Model();
        var source = new Subject<string?>();

        var subscription = model.Bind<string>(Model.NameProperty, source);
        source.Emit("bound");
        Assert.AreEqual("bound", model.Name);
        Assert.AreEqual("bound", model.GetValue(Model.NameProperty));

        subscription.Dispose();
        Assert.AreEqual("default", model.Name);
    }

    [TestMethod]
    public void GetObservable_EmitsTheCurrentValueAndChanges()
    {
        var model = new Model();
        var observed = new List<string?>();
        using var subscription = model.GetObservable<string>(Model.NameProperty).Subscribe(
            new Observer<string?>(observed.Add));

        model.Name = "one";
        model.Name = "two";

        CollectionAssert.AreEqual(new string?[] { "default", "one", "two" }, observed);
    }

    [TestMethod]
    public void TextBlock_TextChangeInvalidatesMeasure()
    {
        using var surface = new VisualHost { Size = new Size(400, 120) };
        var root = new VC.StackPanel();
        var text = new VC.TextBlock { Text = "short" };
        root.Add(text);
        surface.Root = root;
        Render(surface, 400, 120);
        var shortWidth = text.DesiredSize.Width;

        text.Text = "a much longer line of text";
        Render(surface, 400, 120);

        Assert.IsTrue(text.DesiredSize.Width > shortWidth, "the property system must invalidate measure");
    }

    [TestMethod]
    public void InotifyPropertyChanged_RaisesThePropertyName()
    {
        var model = new Model();
        var names = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        model.Name = "changed";

        CollectionAssert.AreEqual(new[] { nameof(Model.Name) }, names);
    }

    private static void Render(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private sealed class Model : WinformsObject
    {
        public static readonly DependencyProperty NameProperty =
            DependencyProperty.Register(
                nameof(Name), typeof(string), typeof(Model), new PropertyMetadata("default"));

        public string Name
        {
            get => (string)GetValue(NameProperty)!;
            set => SetValue(NameProperty, value);
        }
    }

    private sealed class Observer<T>(Action<T?> next) : IObserver<T?>
    {
        public void OnNext(T? value) => next(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    private sealed class Subject<T> : IObservable<T?>
    {
        private readonly List<IObserver<T?>> _observers = [];

        public IDisposable Subscribe(IObserver<T?> observer)
        {
            _observers.Add(observer);
            return new Unsubscriber(() => _observers.Remove(observer));
        }

        public void Emit(T? value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        private sealed class Unsubscriber(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
