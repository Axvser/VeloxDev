using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 刻意做小：一张边框 + 一段文字；锚点与尺寸走真实的绑定通知。
internal sealed class WpfBenchmarkNodeView : Border
{
    private IWorkflowNodeViewModel? _node;

    public WpfBenchmarkNodeView()
    {
        Width = 160d;
        Height = 90d;
        Background = new SolidColorBrush(WpfColor.FromRgb(0x31, 0x3A, 0x4A));
        BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x5B, 0x6B, 0x82));
        BorderThickness = new System.Windows.Thickness(1d);
        Child = new TextBlock
        {
            Text = "node",
            Foreground = WpfBrushes.White,
            FontSize = 12d,
            Margin = new System.Windows.Thickness(8d),
        };
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => Bind(DataContext as IWorkflowNodeViewModel);

    private void Bind(IWorkflowNodeViewModel? node)
    {
        if (ReferenceEquals(_node, node))
        {
            return;
        }

        if (_node is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnNodePropertyChanged;
        }

        _node = node;
        if (node is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnNodePropertyChanged;
        }

        Apply();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_node is null)
        {
            Width = 1d;
            Height = 1d;
            return;
        }

        Width = Math.Max(1d, _node.Size.Width);
        Height = Math.Max(1d, _node.Size.Height);
        Canvas.SetLeft(this, _node.Anchor.Horizontal);
        Canvas.SetTop(this, _node.Anchor.Vertical);
    }
}

// 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
internal sealed class WpfBenchmarkLinkView : Border
{
    public WpfBenchmarkLinkView()
    {
        Width = 1d;
        Height = 1d;
        Background = new SolidColorBrush(WpfColor.FromRgb(0x4A, 0x6A, 0x9A));
        IsHitTestVisible = false;
    }
}

// 两个 DataTemplate 按运行时类型二选一；工厂按类型缓存选中结果。
internal sealed class WpfBenchmarkTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate _node = CreateTemplate(typeof(WpfBenchmarkNodeView));
    private readonly DataTemplate _link = CreateTemplate(typeof(WpfBenchmarkLinkView));

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
        => item is IWorkflowNodeViewModel ? _node : _link;

    private static DataTemplate CreateTemplate(Type viewType)
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(viewType) };

        // 代码建的模板不会经过 XAML 的加载器密封；不密封时 LoadContent 会拒绝实例化。
        template.Seal();
        return template;
    }
}
