namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>The code-behind of the visual-backend XAML fixture.</summary>
public partial class VisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public VisualView()
    {
        InitializeComponent();
    }

    /// <summary>How many times the generated Click handler has run.</summary>
    public int ActionClicks { get; private set; }

    /// <summary>The generated border field.</summary>
    public VeloxDev.WinForms.Xaml.Visuals.Controls.Border Frame => frame;

    /// <summary>The generated text field.</summary>
    public VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock Title => title;

    /// <summary>The generated button field.</summary>
    public VeloxDev.WinForms.Xaml.Visuals.Controls.Button ActionButton => actionButton;

    private void OnActionClick(object? sender, EventArgs e) => ActionClicks++;
}
