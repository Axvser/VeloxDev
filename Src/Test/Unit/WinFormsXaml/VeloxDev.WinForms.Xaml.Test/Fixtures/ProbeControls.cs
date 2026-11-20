using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>A dependency-property host with a small, fixed property set shared by the runtime tests.</summary>
public sealed class ProbeControl : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>A clamped integer, used for default/coerce/change assertions.</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number),
        typeof(int),
        typeof(ProbeControl),
        new PropertyMetadata(7, OnNumberChanged, CoerceNumber));

    /// <summary>A string property, used as the target of the binding tests.</summary>
    public static readonly DependencyProperty NameProperty = DependencyProperty.Register(
        nameof(Name),
        typeof(string),
        typeof(ProbeControl),
        new PropertyMetadata("(none)", OnNameChanged));

    /// <summary>A boolean condition used by the trigger tests.</summary>
    public static readonly DependencyProperty IsHotProperty = DependencyProperty.Register(
        nameof(IsHot),
        typeof(bool),
        typeof(ProbeControl));

    /// <summary>A setter target with no local value in most trigger tests.</summary>
    public static readonly DependencyProperty BorderWidthProperty = DependencyProperty.Register(
        nameof(BorderWidth),
        typeof(int),
        typeof(ProbeControl));

    /// <summary>The animated double used by the transition tests.</summary>
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow),
        typeof(double),
        typeof(ProbeControl),
        new PropertyMetadata(0d, OnGlowChanged));

    /// <summary>A non-visual property: its change must not dirty the control.</summary>
    public static readonly DependencyProperty ConfigurationProperty = DependencyProperty.Register(
        nameof(Configuration),
        typeof(int),
        typeof(ProbeControl),
        new PropertyMetadata(0) { AffectsRender = false });

    /// <summary>A value assigned before a trigger setter, to prove exit restores it.</summary>
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption),
        typeof(string),
        typeof(ProbeControl),
        new PropertyMetadata("caption"));

    /// <summary>Every change callback this control has observed, in call order.</summary>
    public List<DependencyPropertyChangedEventArgs> Changes { get; } = [];

    /// <summary>Every value written to <see cref="GlowProperty"/>, in call order.</summary>
    public List<double> GlowValues { get; } = [];

    /// <summary>The clamped integer.</summary>
    public int Number
    {
        get => (int)GetValue(NumberProperty)!;
        set => SetValue(NumberProperty, value);
    }

    /// <summary>The bound string.</summary>
    public new string Name
    {
        get => (string)GetValue(NameProperty)!;
        set => SetValue(NameProperty, value);
    }

    /// <summary>The trigger condition.</summary>
    public bool IsHot
    {
        get => (bool)GetValue(IsHotProperty)!;
        set => SetValue(IsHotProperty, value);
    }

    /// <summary>The trigger setter target.</summary>
    public int BorderWidth
    {
        get => (int)GetValue(BorderWidthProperty)!;
        set => SetValue(BorderWidthProperty, value);
    }

    /// <summary>The animated value.</summary>
    public double Glow
    {
        get => (double)GetValue(GlowProperty)!;
        set => SetValue(GlowProperty, value);
    }

    /// <summary>A string restored by a trigger exit.</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty)!;
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>A non-visual setting whose changes must not invalidate the control.</summary>
    public int Configuration
    {
        get => (int)GetValue(ConfigurationProperty)!;
        set => SetValue(ConfigurationProperty, value);
    }

    private static void OnNumberChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ProbeControl control)
        {
            control.Changes.Add(e);
        }
    }

    private static void OnNameChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ProbeControl control)
        {
            control.Changes.Add(e);
        }
    }

    private static void OnGlowChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ProbeControl control && e.NewValue is double value)
        {
            control.GlowValues.Add(value);
        }
    }

    // 夹在 0..100：测试要的是「存进去的是 coerce 之后的值」，不是抛异常。
    private static object? CoerceNumber(System.Windows.Forms.Control target, object? baseValue)
        => baseValue is null ? null : Math.Clamp(Convert.ToInt32(baseValue), 0, 100);
}

/// <summary>An attached property owner: the value lives on any <see cref="System.Windows.Forms.Control"/>.</summary>
public static class AttachedProbe
{
    /// <summary>The attached note, registered against a type that is not a control.</summary>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.RegisterAttached(
        "Note",
        typeof(string),
        typeof(AttachedProbe),
        new PropertyMetadata("(none)", OnNoteChanged));

    private static readonly List<(System.Windows.Forms.Control Target, object? OldValue, object? NewValue)> Observed = [];

    /// <summary>Every attached-property change observed since the last <see cref="Reset"/>.</summary>
    public static IReadOnlyList<(System.Windows.Forms.Control Target, object? OldValue, object? NewValue)> Changes => Observed;

    /// <summary>Reads the attached note.</summary>
    public static string? GetNote(System.Windows.Forms.Control control) => (string?)NoteProperty.GetValue(control);

    /// <summary>Writes the attached note.</summary>
    public static void SetNote(System.Windows.Forms.Control control, string? value) => NoteProperty.SetValue(control, value);

    /// <summary>Clears the attached note.</summary>
    public static void ClearNote(System.Windows.Forms.Control control) => NoteProperty.ClearValue(control);

    /// <summary>Forgets the observed changes.</summary>
    public static void Reset() => Observed.Clear();

    private static void OnNoteChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
        => Observed.Add((target, e.OldValue, e.NewValue));
}

/// <summary>A view model that raises <see cref="INotifyPropertyChanged"/> for the binding tests.</summary>
public sealed class ProbeViewModel : INotifyPropertyChanged
{
    private string _name = "initial";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The bound name.</summary>
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

    /// <summary>A nested object, so a dot-separated path can be exercised.</summary>
    public ProbeViewModel? Child { get; set; }

    private ObservableCollection<ProbeItem> _items = [];

    /// <summary>A replaceable observable collection used by the DataTrigger collection tests.</summary>
    public ObservableCollection<ProbeItem> Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value))
            {
                return;
            }

            _items = value ?? [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        }
    }

    /// <summary>A plain list without collection notifications, for the static-path tests.</summary>
    public List<ProbeItem> PlainItems { get; } = [];
}

/// <summary>An observable item used by the DataTrigger indexer-path tests.</summary>
public sealed class ProbeItem : INotifyPropertyChanged
{
    private string _name = "item";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The item name.</summary>
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

    /// <summary>A nested observable collection for multi-level indexer paths.</summary>
    public ObservableCollection<ProbeItem> Children { get; } = [];
}

/// <summary>The data context used by the analyzer-generated XAML fixture.</summary>
public sealed class GeneratedModel : INotifyPropertyChanged
{
    private string _title = "generated title";
    private string _caption = "generated caption";
    private int _column = 1;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The title the card binds to.</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>The caption the native label binds to.</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            if (_caption == value)
            {
                return;
            }

            _caption = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
        }
    }

    /// <summary>The column the attached binding drives.</summary>
    public int Column
    {
        get => _column;
        set
        {
            if (_column == value)
            {
                return;
            }

            _column = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Column)));
        }
    }
}

/// <summary>Brackets values on the way in and strips the brackets on the way back.</summary>
public sealed class BracketConverter : IValueConverter
{
    /// <summary>The number of <see cref="Convert"/> calls.</summary>
    public int ConvertCalls { get; private set; }

    /// <summary>The number of <see cref="ConvertBack"/> calls.</summary>
    public int ConvertBackCalls { get; private set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType)
    {
        ConvertCalls++;
        return value is null ? null : "[" + value + "]";
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType)
    {
        ConvertBackCalls++;
        return value?.ToString()?.Trim('[', ']');
    }
}

/// <summary>A trigger action that records when it runs and what the control looked like at that moment.</summary>
public sealed class RecordingTriggerAction : TriggerAction
{
    /// <summary>How many times <see cref="Invoke"/> ran.</summary>
    public int Invocations { get; private set; }

    /// <summary>The control the last invocation ran against.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Control? LastControl { get; private set; }

    /// <summary>What <see cref="ProbeControl.BorderWidth"/> was when the action ran.</summary>
    public int BorderWidthAtInvocation { get; private set; } = -1;

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        Invocations++;
        LastControl = control;
        BorderWidthAtInvocation = control is ProbeControl probe ? probe.BorderWidth : -1;
    }
}
