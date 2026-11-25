using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.WinUI;

// 节点视图直接复用 Trimmed demo 的 NodeView —— 它是 VeloxDev.WinUI.Templates 里 workflow-node-view 的镜像；
// 基准量的是发布模板那套视图，不在基准里维护私有副本。合成节点提供 Name / InputSlot / OutputSlots，
// 模板的绑定因此会真正实例化标题、端口与输出槽行。
internal static class WinUiBenchmarkTemplates
{
    internal const string NodeXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:demo="using:Demo.Views.Workflow">
            <demo:NodeView />
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
