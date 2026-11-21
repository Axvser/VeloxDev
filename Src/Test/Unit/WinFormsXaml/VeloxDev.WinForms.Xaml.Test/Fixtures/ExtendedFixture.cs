using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>A card used by the extended XAML fixture: template binding, style-setter binding and template triggers.</summary>
public partial class ExtendedCard : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>The title bound from the data context and from the template.</summary>
    [XamlProperty(DefaultValue = "")]
    public partial string Title { get; set; }

    /// <summary>A style-only value.</summary>
    [XamlProperty]
    public partial int StyleValue { get; set; }

    /// <summary>The template-trigger condition.</summary>
    [XamlProperty]
    public partial bool IsHot { get; set; }
}

/// <summary>Static members used by the <c>x:Static</c> half of the extended fixture.</summary>
public static class ExtendedStatics
{
    /// <summary>The static value read by the generated markup.</summary>
    public static string Value => "static value";
}

/// <summary>The data context used by the extended fixture.</summary>
public sealed class ExtendedModel : INotifyPropertyChanged
{
    private string _caption = "Caption";
    private double _amount = 1.5d;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The caption bound through a converter and a style setter.</summary>
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

    /// <summary>The number formatted through <c>StringFormat</c>.</summary>
    public double Amount
    {
        get => _amount;
        set
        {
            if (Math.Abs(_amount - value) < double.Epsilon)
            {
                return;
            }

            _amount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Amount)));
        }
    }

    /// <summary>The command used by the XAML command/input smoke test.</summary>
    public ICommand SaveCommand { get; }

    /// <summary>How many times <see cref="SaveCommand"/> ran.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Creates the model and its command.</summary>
    public ExtendedModel()
    {
        SaveCommand = new RelayCommand(_ => SaveCount++);
    }

    /// <summary>The items shown by the paneled items control.</summary>
    public ObservableCollection<ProbeItem> Items { get; } = [];

    private DateTime? _date = new(2026, 11, 22);

    /// <summary>The date bound by the calendar and date picker.</summary>
    public DateTime? Date
    {
        get => _date;
        set
        {
            if (_date == value)
            {
                return;
            }

            _date = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
        }
    }
}

/// <summary>The code-behind of the extended analyzer-generated fixture.</summary>
public partial class ExtendedView : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public ExtendedView()
    {
        InitializeComponent();
    }

    /// <summary>How many times the generated menu item handler ran.</summary>
    public int MenuClicks { get; private set; }

    /// <summary>The element-name binding result.</summary>
    public string ElementText => elementText.Text;

    /// <summary>The <c>x:Static</c> result.</summary>
    public string StaticText => staticText.Text;

    /// <summary>The <c>StringFormat</c> result.</summary>
    public string FormattedText => formattedText.Text;

    /// <summary>The card driven by the converter style.</summary>
    public ExtendedCard Card => card;

    /// <summary>The card driven by the template style.</summary>
    public ExtendedCard TemplateCard => templateCard;

    /// <summary>The button carrying the declarative context menu and tooltip.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Button MenuButton => menuButton;

    /// <summary>The items control with an items panel.</summary>
    public VeloxDev.WinForms.Xaml.Controls.ItemsControl Items => items;

    /// <summary>The popup declared with a child property element.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Popup Popup => popup;

    private void OnMenuOpen(object? sender, EventArgs e) => MenuClicks++;
}
