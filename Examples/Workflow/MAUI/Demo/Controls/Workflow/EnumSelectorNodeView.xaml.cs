namespace Demo.Controls;

public partial class EnumSelectorNodeView : ContentView
{
    // Enum selector's [DefaultSize] is 280x380.
    private readonly NodeMetricScaler _scaler = new(280);

    public EnumSelectorNodeView()
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
