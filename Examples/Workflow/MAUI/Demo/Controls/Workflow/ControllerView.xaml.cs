namespace Demo.Controls;

public partial class ControllerView : ContentView
{
    // Controller's [DefaultSize] is 220x340.
    private readonly NodeMetricScaler _scaler = new(220);

    public ControllerView()
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
