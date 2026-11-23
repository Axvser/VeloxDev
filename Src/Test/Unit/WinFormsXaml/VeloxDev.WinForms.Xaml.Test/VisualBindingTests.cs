using System.ComponentModel;
using System.Globalization;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
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
    public void NestedBinding_ReadsTheInheritedContextWhenTheSubtreeIsAttached()
    {
        var model = new Model { Name = "nested" };
        using var root = new VC.StackPanel { DataContext = model };
        using var column = new VC.StackPanel();
        var text = new VC.TextBlock();
        text.SetBinding(VC.TextBlock.TextProperty, new Binding(nameof(Model.Name)));
        column.Add(text);
        root.Add(column);

        Assert.AreSame(model, text.DataContext, "the nested element sees the parent's context");
        Assert.AreEqual(
            "nested",
            text.Text,
            "a binding created before the subtree was attached must re-read the inherited context");
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

    [TestMethod]
    public void FindAncestor_ResolvesNearestAndAncestorLevel()
    {
        var outer = new VC.Grid { Width = 100 };
        var inner = new VC.Grid { Width = 50 };
        outer.Add(inner);

        var nearest = new VC.TextBlock();
        inner.Add(nearest);
        nearest.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding("Width")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(VC.Grid) },
            });

        Assert.AreEqual("50", nearest.Text, "AncestorLevel defaults to the nearest matching ancestor");

        var second = new VC.TextBlock();
        inner.Add(second);
        second.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding("Width")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(VC.Grid),
                    AncestorLevel = 2,
                },
            });

        Assert.AreEqual("100", second.Text, "AncestorLevel=2 skips the nearest match");
    }

    [TestMethod]
    public void FindAncestor_MissingAncestor_ReportsFailureAndUsesFallback()
    {
        var root = new VC.StackPanel();
        var text = new VC.TextBlock();
        root.Add(text);
        BindingFailureEventArgs? failure = null;
        void Handler(object? sender, BindingFailureEventArgs args)
        {
            if (ReferenceEquals(args.Target, text))
            {
                failure = args;
            }
        }

        VisualBindingOperations.BindingFailed += Handler;
        try
        {
            text.SetBinding(
                VC.TextBlock.TextProperty,
                new Binding("Width")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(VC.Grid) },
                    FallbackValue = "none",
                });
        }
        finally
        {
            VisualBindingOperations.BindingFailed -= Handler;
        }

        Assert.AreEqual("none", text.Text);
        Assert.IsNotNull(failure, "an unresolved FindAncestor is a binding failure, not a silent empty value");
        Assert.AreSame(typeof(VC.Grid), failure!.Binding.RelativeSource!.AncestorType);
    }

    [TestMethod]
    public void ElementName_ResolvesANonAncestorSibling()
    {
        var root = new VC.StackPanel();
        var source = new VC.TextBlock { Name = "source", Text = "sibling" };
        var target = new VC.TextBlock();
        root.Add(source);
        root.Add(target);

        target.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(VC.TextBlock.Text)) { ElementName = "source" });

        Assert.AreEqual("sibling", target.Text, "ElementName is namescope-wide, not ancestor-only");
    }

    [TestMethod]
    public void LostFocus_WritesTheSourceOnlyWhenTheTargetLosesFocus()
    {
        using var surface = new VisualHost { Size = new System.Drawing.Size(200, 80) };
        var model = new Model { Name = "source" };
        var box = new VC.TextBox { Width = 150, Height = 30 };
        surface.Root = box;
        box.SetBinding(
            VC.TextBox.TextProperty,
            new Binding(nameof(Model.Name))
            {
                Source = model,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            });

        Assert.AreEqual("source", box.Text);
        Assert.IsTrue(surface.FocusElement(box));

        box.Text = "typed";
        Assert.AreEqual("source", model.Name, "a focused field defers the write");

        surface.FocusElement(null);
        Assert.AreEqual("typed", model.Name, "losing focus flushes the pending value");
    }

    [TestMethod]
    public void Explicit_NeverWritesUntilUpdateSourceIsCalled()
    {
        var model = new Model { Name = "source" };
        var box = new VC.TextBox();
        var expression = box.SetBinding(
            VC.TextBox.TextProperty,
            new Binding(nameof(Model.Name))
            {
                Source = model,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            });

        Assert.AreEqual("source", box.Text);
        box.Text = "typed";
        Assert.AreEqual("source", model.Name, "Explicit does not write on the target change");

        Assert.IsTrue(expression.UpdateSource());
        Assert.AreEqual("typed", model.Name);

        // 生成代码丢弃返回句柄，XAML 的 Explicit 绑定靠 target+property 找回表达式。
        box.Text = "again";
        Assert.AreEqual("typed", model.Name);
        Assert.IsTrue(VisualBindingOperations.UpdateSource(box, VC.TextBox.TextProperty));
        Assert.AreEqual("again", model.Name);
    }

    [TestMethod]
    public void ValidatesOnExceptions_TurnsAWriteFailureIntoAValidationError()
    {
        var model = new FailingModel();
        var box = new VC.TextBox();
        var actions = new List<ValidationErrorEventAction>();
        void Handler(object? sender, ValidationErrorEventArgs args)
        {
            if (ReferenceEquals(args.Source, box))
            {
                actions.Add(args.Action);
            }
        }

        Validation.Error += Handler;
        try
        {
            box.SetBinding(
                VC.TextBox.TextProperty,
                new Binding(nameof(FailingModel.Name))
                {
                    Source = model,
                    Mode = BindingMode.TwoWay,
                    ValidatesOnExceptions = true,
                    NotifyOnValidationError = true,
                });

            Assert.IsFalse(Validation.GetHasError(box));

            box.Text = "bad";

            Assert.AreEqual("ok", model.Name, "the source keeps its previous value");
            Assert.IsTrue(Validation.GetHasError(box));
            var errors = Validation.GetErrors(box);
            Assert.IsNotNull(errors);
            Assert.AreEqual(1, errors!.Count);
            Assert.IsInstanceOfType<InvalidOperationException>(errors![0].Exception);
            Assert.IsTrue(actions.Contains(ValidationErrorEventAction.Added));

            box.Text = "good";

            Assert.AreEqual("good", model.Name);
            Assert.IsFalse(Validation.GetHasError(box));
            Assert.IsTrue(actions.Contains(ValidationErrorEventAction.Removed));
        }
        finally
        {
            Validation.Error -= Handler;
        }
    }

    [TestMethod]
    public void UnvalidatedWriteFailure_IsReportedInsteadOfThrown()
    {
        var model = new FailingModel();
        var box = new VC.TextBox();
        var failures = 0;
        void Handler(object? sender, BindingFailureEventArgs args)
        {
            if (ReferenceEquals(args.Target, box))
            {
                failures++;
            }
        }

        VisualBindingOperations.BindingFailed += Handler;
        try
        {
            box.SetBinding(
                VC.TextBox.TextProperty,
                new Binding(nameof(FailingModel.Name)) { Source = model, Mode = BindingMode.TwoWay });

            box.Text = "bad";
        }
        finally
        {
            VisualBindingOperations.BindingFailed -= Handler;
        }

        Assert.AreEqual(1, failures);
        Assert.AreEqual("ok", model.Name);
        Assert.IsFalse(Validation.GetHasError(box), "the flag was not set, so no validation state is produced");
    }

    [TestMethod]
    public void OneWayToSource_WritesTheSourceWithoutPushingTheTarget()
    {
        var model = new Model { Name = "source" };
        var box = new VC.TextBox();
        box.SetBinding(
            VC.TextBox.TextProperty,
            new Binding(nameof(Model.Name)) { Source = model, Mode = BindingMode.OneWayToSource });

        Assert.AreEqual(string.Empty, box.Text, "OneWayToSource does not initialize the target");

        box.Text = "typed";
        Assert.AreEqual("typed", model.Name);

        model.Name = "again";
        Assert.AreEqual("typed", box.Text, "a source change never reaches the target");
    }

    [TestMethod]
    public void BindingMode_DefaultsToOneWay()
    {
        Assert.AreEqual(BindingMode.OneWay, new Binding().Mode);

        var model = new Model { Name = "source" };
        var box = new VC.TextBox();
        box.SetBinding(VC.TextBox.TextProperty, new Binding(nameof(Model.Name)) { Source = model });

        box.Text = "typed";
        Assert.AreEqual("source", model.Name, "the default mode never writes the source");

        model.Name = "next";
        Assert.AreEqual("next", box.Text);
    }

    [TestMethod]
    public void Converter_ReceivesTheParameterAndTheCulture()
    {
        var converter = new RecordingConverter();
        var text = new VC.TextBlock();
        text.SetBinding(
            VC.TextBlock.TextProperty,
            new Binding(nameof(NumberModel.Amount))
            {
                Source = new NumberModel { Amount = 1.5 },
                Converter = converter,
                ConverterParameter = "p",
                ConverterCulture = CultureInfo.GetCultureInfo("de-DE"),
            });

        Assert.AreEqual("1,5", text.Text);
        Assert.AreEqual("p", converter.LastParameter);
        Assert.AreEqual("de-DE", converter.LastCulture!.Name);
    }

    [TestMethod]
    public void Generator_EmitsTheNewBindingSemantics()
    {
        const string source = """
            namespace ProbeBinding;

            public partial class ProbeBindingView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <local:ProbeBindingView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:ProbeBinding"
                x:Class="ProbeBinding.ProbeBindingView">

              <TextBlock Text="{Binding Caption, Mode=OneWayToSource, UpdateSourceTrigger=LostFocus, ValidatesOnExceptions=True, NotifyOnValidationError=True, ConverterCulture=en-US}" />
              <TextBlock Text="{Binding Width, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type local:ProbeBindingView}, AncestorLevel=2}}" />

            </local:ProbeBindingView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualBindingSemanticsProbe",
            new InMemoryAdditionalText("ProbeBindingView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(
            generated,
            "Mode = global::VeloxDev.WinForms.Xaml.BindingMode.OneWayToSource");
        StringAssert.Contains(
            generated,
            "UpdateSourceTrigger = global::VeloxDev.WinForms.Xaml.UpdateSourceTrigger.LostFocus");
        StringAssert.Contains(generated, "ValidatesOnExceptions = true");
        StringAssert.Contains(generated, "NotifyOnValidationError = true");
        StringAssert.Contains(
            generated,
            "ConverterCulture = global::System.Globalization.CultureInfo.GetCultureInfo(\"en-US\")");
        StringAssert.Contains(generated, "AncestorLevel = 2");
    }

    private sealed class ParentModel
    {
        public Model? Child { get; set; }
    }

    private sealed class NumberModel
    {
        public double Amount { get; set; }
    }

    private sealed class FailingModel : INotifyPropertyChanged
    {
        private string? _name = "ok";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Name
        {
            get => _name;
            set
            {
                if (value == "bad")
                {
                    throw new InvalidOperationException("bad value");
                }

                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    // 同时实现三套转换器约定，验证运行时会走带 parameter+culture 的那一档。
    private sealed class RecordingConverter : IValueConverter, IParameterizedValueConverter, ICultureAwareValueConverter
    {
        public string? LastParameter { get; private set; }

        public CultureInfo? LastCulture { get; private set; }

        public object? Convert(object? value, Type targetType)
            => Convert(value, targetType, null, CultureInfo.CurrentCulture);

        public object? ConvertBack(object? value, Type sourceType)
            => ConvertBack(value, sourceType, null, CultureInfo.CurrentCulture);

        public object? Convert(object? value, Type targetType, object? parameter)
            => Convert(value, targetType, parameter, CultureInfo.CurrentCulture);

        public object? ConvertBack(object? value, Type sourceType, object? parameter)
            => ConvertBack(value, sourceType, parameter, CultureInfo.CurrentCulture);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            LastParameter = parameter as string;
            LastCulture = culture;
            return value is IFormattable formattable ? formattable.ToString(null, culture) : value;
        }

        public object? ConvertBack(object? value, Type sourceType, object? parameter, CultureInfo culture)
            => System.Convert.ChangeType(value, sourceType, culture);
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
