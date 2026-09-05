namespace Demo.Controls;

public partial class TimerNodeView : ContentView
{
    // Timer's [DefaultSize] is 200x140.
    private readonly NodeMetricScaler _scaler = new(200);

    public TimerNodeView()
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
