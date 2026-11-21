using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using VeloxDev.WinForms.Xaml.Test.Fixtures;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// WPF-parity additions: binding validation, visual states and dynamic resources.
/// </summary>
[STATestClass]
public sealed class ValidationVsmDynamicTests
{
    [TestMethod]
    public void RulesPublishErrorsWithoutBlockingTheValue()
    {
        using var probe = new ProbeControl();
        var model = new ValidationModel();
        probe.DataContext = model;

        probe.SetBinding(
            ProbeControl.NameProperty,
            new Binding("Name") { ValidationRules = { new RequiredRule("name required") } });
        probe.SetBinding(
            ProbeControl.NumberProperty,
            new Binding("Number") { ValidationRules = { new RangeRule(1, 10, "out of range") } });

        Assert.AreEqual("ok", probe.Name);
        Assert.IsFalse(Validation.GetHasError(probe));

        model.Name = string.Empty;

        Assert.AreEqual(string.Empty, probe.Name, "the rejected value is kept");
        Assert.IsTrue(Validation.GetHasError(probe));
        var nameErrors = Validation.GetErrors(probe);
        Assert.IsNotNull(nameErrors);
        Assert.AreEqual(1, nameErrors!.Count);
        Assert.AreEqual("name required", nameErrors[0].Message);

        model.Name = "fixed";
        Assert.IsFalse(Validation.GetHasError(probe));

        model.Number = 99;

        Assert.AreEqual(99, probe.Number, "the rejected number is kept");
        Assert.IsTrue(Validation.GetHasError(probe));
        var numberErrors = Validation.GetErrors(probe);
        Assert.IsNotNull(numberErrors);
        Assert.AreEqual(1, numberErrors!.Count);
        Assert.AreEqual("out of range", numberErrors[0].Message);

        model.Number = 3;
        Assert.IsFalse(Validation.GetHasError(probe));
    }

    [TestMethod]
    public void NotifyDataErrorInfoSourcePublishesItsErrors()
    {
        using var probe = new ProbeControl();
        var model = new NotifyDataErrorModel();
        probe.DataContext = model;
        probe.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        Assert.IsFalse(Validation.GetHasError(probe));

        model.SetError(nameof(NotifyDataErrorModel.Name), "from the source");

        Assert.IsTrue(Validation.GetHasError(probe));
        var errors = Validation.GetErrors(probe);
        Assert.IsNotNull(errors);
        Assert.AreEqual(1, errors!.Count);
        Assert.AreEqual("from the source", errors[0].Message);

        model.ClearErrors(nameof(NotifyDataErrorModel.Name));
        Assert.IsFalse(Validation.GetHasError(probe));
    }

    [TestMethod]
    public void DataErrorInfoSourcePublishesItsError()
    {
        using var probe = new ProbeControl();
        var model = new DataErrorModel();
        probe.DataContext = model;
        probe.SetBinding(ProbeControl.NameProperty, new Binding("Name"));

        model.Name = string.Empty;

        Assert.IsTrue(Validation.GetHasError(probe));
        var errors = Validation.GetErrors(probe);
        Assert.IsNotNull(errors);
        Assert.AreEqual(1, errors!.Count);
        Assert.AreEqual("name empty", errors[0].Message);

        model.Name = "filled";
        Assert.IsFalse(Validation.GetHasError(probe));
    }

    [TestMethod]
    public void VisualStateSettersApplyAndRestore()
    {
        using var probe = new ProbeControl();
        var group = new VisualStateGroup { Name = "Common" };
        var normal = new VisualState { Name = "Normal" };
        var hot = new VisualState { Name = "Hot" };
        var action = new RecordingTriggerAction();

        hot.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 4));
        hot.Setters.Add(new Setter(ProbeControl.CaptionProperty, "hot"));
        hot.EnterAction = action;
        hot.ExitAction = action;
        group.States.Add(normal);
        group.States.Add(hot);

        VisualStateManager.SetVisualStateGroups(probe, new List<VisualStateGroup> { group });

        Assert.IsTrue(VisualStateManager.GoToState(probe, "Hot", useTransitions: false));
        Assert.AreEqual(4, probe.BorderWidth);
        Assert.AreEqual("hot", probe.Caption);
        Assert.AreEqual("Hot", VisualStateManager.GetCurrentState(probe, "Common"));
        Assert.AreEqual(1, action.Invocations);

        Assert.IsTrue(VisualStateManager.GoToState(probe, "Normal", useTransitions: false));
        Assert.AreEqual(0, probe.BorderWidth, "the previous state's setter is restored");
        Assert.AreEqual("caption", probe.Caption, "the metadata value comes back");
        Assert.AreEqual("Normal", VisualStateManager.GetCurrentState(probe, "Common"));
        Assert.AreEqual(2, action.Invocations);

        Assert.IsFalse(VisualStateManager.GoToState(probe, "Missing", useTransitions: false));
    }

    [TestMethod]
    public void DynamicResource_FollowsAnAncestorAttachedLater()
    {
        using var root = new ColorProbe();
        using var middle = new ColorProbe();
        using var leaf = new ColorProbe();

        root.Resources["Accent"] = Color.Red;
        middle.Controls.Add(leaf);
        XamlRuntime.SetDynamicResource(leaf, leaf, ColorProbe.AccentProperty, "Accent");

        Assert.AreEqual(Color.Empty, leaf.Accent, "the resource is not reachable yet");

        root.Controls.Add(middle);

        Assert.AreEqual(Color.Red, leaf.Accent, "the ancestor dictionary enters the chain");
    }

    [TestMethod]
    public void DynamicResourceFollowsResourceReplacement()
    {
        using var root = new ColorProbe();
        using var child = new ColorProbe();
        root.Controls.Add(child);

        root.Resources["Accent"] = Color.Red;
        XamlRuntime.SetDynamicResource(root, child, ColorProbe.AccentProperty, "Accent");

        Assert.AreEqual(Color.Red, child.Accent);

        root.Resources["Accent"] = Color.Blue;
        Assert.AreEqual(Color.Blue, child.Accent);

        root.Resources.Remove("Accent");
        Assert.AreEqual(Color.Empty, child.Accent, "removing the resource returns the default");
    }

    [TestMethod]
    public void MergedDictionaryLookupAndDynamicResource()
    {
        using var root = new ColorProbe();
        using var child = new ColorProbe();
        root.Controls.Add(child);

        var merged = new ResourceDictionary { ["Accent"] = Color.Green };
        root.Resources.MergedDictionaries.Add(merged);

        Assert.AreEqual(Color.Green, root.FindResource("Accent"));
        Assert.IsTrue(root.Resources.ContainsKey("Accent"));

        XamlRuntime.SetDynamicResource(root, child, ColorProbe.AccentProperty, "Accent");
        Assert.AreEqual(Color.Green, child.Accent);

        merged["Accent"] = Color.Black;
        Assert.AreEqual(Color.Black, child.Accent, "a merged dictionary change reaches the dynamic resource");

        root.Resources.MergedDictionaries.Remove(merged);
        Assert.AreEqual(Color.Empty, child.Accent);
        Assert.IsNull(root.FindResource("Accent"));
    }

    private sealed class ColorProbe : VeloxDev.WinForms.Xaml.Controls.Control
    {
        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent),
            typeof(Color),
            typeof(ColorProbe),
            new PropertyMetadata(Color.Empty));

        public Color Accent
        {
            get => (Color)GetValue(AccentProperty)!;
            set => SetValue(AccentProperty, value);
        }
    }

    private sealed class ValidationModel : INotifyPropertyChanged
    {
        private string _name = "ok";
        private int _number = 5;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
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

        public int Number
        {
            get => _number;
            set
            {
                if (_number == value)
                {
                    return;
                }

                _number = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
            }
        }
    }

    private sealed class NotifyDataErrorModel : INotifyDataErrorInfo, INotifyPropertyChanged
    {
        private readonly Dictionary<string, List<string>> _errors = new();
        private string _name = "ok";

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        public bool HasErrors => _errors.Count > 0;

        public string Name
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

        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                var all = new List<string>();
                foreach (var errors in _errors.Values)
                {
                    all.AddRange(errors);
                }

                return all;
            }

            return _errors.TryGetValue(propertyName, out var reported) ? reported : new List<string>();
        }

        public void SetError(string propertyName, string message)
        {
            if (!_errors.TryGetValue(propertyName, out var errors))
            {
                errors = new List<string>();
                _errors[propertyName] = errors;
            }

            errors.Add(message);
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        public void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }
        }
    }

    private sealed class DataErrorModel : IDataErrorInfo, INotifyPropertyChanged
    {
        private string _name = "ok";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Error => string.Empty;

        public string this[string columnName]
            => columnName == nameof(Name) && string.IsNullOrEmpty(_name) ? "name empty" : string.Empty;

        public string Name
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
    }

    private sealed class RequiredRule : ValidationRule
    {
        private readonly string _message;

        public RequiredRule(string message)
        {
            _message = message;
        }

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
            => string.IsNullOrWhiteSpace(value as string)
                ? new ValidationResult(false, _message)
                : ValidationResult.ValidResult;
    }

    private sealed class RangeRule : ValidationRule
    {
        private readonly int _minimum;
        private readonly int _maximum;
        private readonly string _message;

        public RangeRule(int minimum, int maximum, string message)
        {
            _minimum = minimum;
            _maximum = maximum;
            _message = message;
        }

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
            => value is int number && number >= _minimum && number <= _maximum
                ? ValidationResult.ValidResult
                : new ValidationResult(false, _message);
    }
}
