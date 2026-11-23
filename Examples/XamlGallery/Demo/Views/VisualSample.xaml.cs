namespace Demo.Views;

/// <summary>A visual-backend view declared with unprefixed visual XAML.</summary>
public sealed partial class VisualSample : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
{
    private int _clicks;

    /// <summary>Creates the view and runs the generated initializer.</summary>
    public VisualSample()
    {
        InitializeComponent();
    }

    private void OnSampleClick(object? sender, EventArgs e)
    {
        _clicks++;
        sampleStatus.Text = $"Generated from visual XAML · clicks: {_clicks}";
    }
}
