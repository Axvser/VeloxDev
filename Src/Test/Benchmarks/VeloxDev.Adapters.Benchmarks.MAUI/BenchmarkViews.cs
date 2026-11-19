using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 刻意做小：一张边框；几何由适配器的 ViewArranger 写进 AbsoluteLayout 附加属性。
internal sealed class BenchmarkNodeView : Border
{
    public BenchmarkNodeView()
    {
        BackgroundColor = Color.FromArgb("#313A4A");
        Stroke = new SolidColorBrush(Color.FromArgb("#5B6B82"));
        StrokeThickness = 1d;
        InputTransparent = true;
        Content = new Label
        {
            Text = "node",
            FontSize = 12d,
            TextColor = Colors.White,
            Margin = new Thickness(8d, 6d),
        };
    }
}

// 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
internal sealed class BenchmarkLinkView : Border
{
    public BenchmarkLinkView()
    {
        WidthRequest = 1d;
        HeightRequest = 1d;
        BackgroundColor = Color.FromArgb("#4A6A9A");
        InputTransparent = true;
    }
}

// 两个模板按运行时类型二选一；适配器的 ViewFactory 会按类型缓存选中结果。
internal sealed class BenchmarkTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate _node = new(static () => new BenchmarkNodeView());
    private readonly DataTemplate _link = new(static () => new BenchmarkLinkView());

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item is IWorkflowNodeViewModel ? _node : _link;
}
