namespace Demo.Controls;

public partial class ControllerView : ContentView
{
    // Controller's [DefaultSize] is 230x170. 设计宽必须与它一致，k 才是「设计单位 → 卡片单位」那一档，
    // 否则第一帧就把整张卡按错误的 k 缩掉一截（原来这里写的是 220，与实际尺寸对不上）。
    private readonly NodeMetricScaler _scaler = new(230);

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
