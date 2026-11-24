using System.Windows;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WPF.Headless.Tests.Infrastructure;

// 只记录表面推来的小地图契约值；绘制由 MinimapHeadlessTests 单独覆盖。
internal sealed class TestMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{
    public double ScrollOffsetX { get; set; }
    public double ScrollOffsetY { get; set; }
    public double ContentOffsetX { get; set; }
    public double ContentOffsetY { get; set; }
    public double RulerBand { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public ITreeComponent? WorkflowTree { get; set; }
    public bool IsMinimapVisible { get; set; }
    public double AvailableRight { get; set; }
    public double AvailableTop { get; set; }
    public double AvailableBottom { get; set; }
    public double MinimapMargin { get; set; }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        => new(
            double.IsNaN(Width) ? availableSize.Width : Width,
            double.IsNaN(Height) ? availableSize.Height : Height);
}
