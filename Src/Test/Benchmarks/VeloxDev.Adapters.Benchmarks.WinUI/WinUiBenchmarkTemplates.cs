using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.WinUI;

internal static class WinUiBenchmarkTemplates
{
    // 节点模板只负责外观：尺寸与位置由 ViewArranger 命令式写入 Canvas。
    internal const string NodeXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Border Background="#313A4A" BorderBrush="#5B6B82" BorderThickness="1" />
        </DataTemplate>
        """;

    // 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
    internal const string LinkXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Border Width="1" Height="1" Background="#4A6A9A" IsHitTestVisible="False" />
        </DataTemplate>
        """;

    internal static DataTemplate Load(string xaml) => (DataTemplate)XamlReader.Load(xaml);
}

// WinUI 没有 FrameworkElementFactory；模板只能走运行期 XAML 解析，按运行时类型二选一。
internal sealed class WinUiBenchmarkTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate _node = WinUiBenchmarkTemplates.Load(WinUiBenchmarkTemplates.NodeXaml);
    private readonly DataTemplate _link = WinUiBenchmarkTemplates.Load(WinUiBenchmarkTemplates.LinkXaml);

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => item is INodeComponent ? _node : _link;

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item)
        => item is INodeComponent ? _node : _link;
}
