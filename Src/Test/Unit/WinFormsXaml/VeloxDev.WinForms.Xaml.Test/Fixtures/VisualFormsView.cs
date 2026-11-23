using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>
/// The code-behind of the markup-form fixture. It exercises unprefixed controls, a <c>local:</c> root, attached
/// properties, named resources, a style, a control template, a data template and a selector resource.
/// </summary>
public partial class VisualFormsView : VC.StackPanel
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public VisualFormsView()
    {
        InitializeComponent();
    }

    /// <summary>How many times the generated Click handler has run.</summary>
    public int ActionClicks { get; private set; }

    /// <summary>The generated caption field.</summary>
    public VC.TextBlock Caption => caption;

    /// <summary>The generated grid field.</summary>
    public VC.Grid Layout => grid;

    /// <summary>The border carrying <c>Grid.Row</c>.</summary>
    public VC.Border TopCell => topCell;

    /// <summary>The canvas field.</summary>
    public VC.Canvas CanvasRoot => surface;

    /// <summary>The border carrying <c>Canvas.Left</c> and <c>Canvas.Top</c>.</summary>
    public VC.Border Placed => placed;

    /// <summary>The generated items control.</summary>
    public VC.ItemsControl List => list;

    /// <summary>The generated button.</summary>
    public VC.Button Action => action;

    /// <summary>A caption that only receives the implicit style.</summary>
    public VC.TextBlock ImplicitCaption => implicitCaption;

    /// <summary>The selector resource stored under <c>RowSelector</c>.</summary>
    public Selector RowSelector => VisualResourceLookup.Get<Selector>(this, "RowSelector");

    /// <summary>The data template resource stored under <c>RowTemplate</c>.</summary>
    public DataTemplate RowTemplate => VisualResourceLookup.Get<DataTemplate>(this, "RowTemplate");

    /// <summary>The control template resource stored under <c>CardChrome</c>.</summary>
    public VisualControlTemplate CardChrome => VisualResourceLookup.Get<VisualControlTemplate>(this, "CardChrome");

    private void OnActionClick(object? sender, EventArgs e) => ActionClicks++;
}
