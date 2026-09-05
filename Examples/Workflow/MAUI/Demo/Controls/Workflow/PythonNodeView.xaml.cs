namespace Demo.Controls;

public partial class PythonNodeView : ContentView
{
    // Python's [DefaultSize] is 280x260.
    private readonly NodeMetricScaler _scaler = new(280);

    public PythonNodeView()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (DesignGrid is not null)
        {
            _scaler.ApplyScale(DesignGrid, width);
        }
    }
}
