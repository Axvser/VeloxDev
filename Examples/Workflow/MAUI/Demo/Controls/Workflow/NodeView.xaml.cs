namespace Demo.Controls;

public partial class NodeView : ContentView
{
    // Generic catch-all card: design 260x180 (the Trimmed generic node's design size).
    private readonly NodeMetricScaler _scaler = new(260);

    public NodeView()
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
