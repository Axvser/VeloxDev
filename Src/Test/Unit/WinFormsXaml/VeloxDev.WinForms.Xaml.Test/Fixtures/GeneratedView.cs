namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>The code-behind of the analyzer-generated XAML fixture.</summary>
public partial class GeneratedView : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public GeneratedView()
    {
        InitializeComponent();
    }

    /// <summary>How many times the generated Click handler has run.</summary>
    public int ProbeClicks { get; private set; }

    private void OnProbeClick(object? sender, EventArgs e) => ProbeClicks++;
}
