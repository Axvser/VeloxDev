using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using JColor = Jalium.UI.Media.Color;
using JBrushes = Jalium.UI.Media.Brushes;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 刻意做小：一张边框 + 一段文字；锚点与尺寸走真实的 DataContext 通知。
internal sealed class JaliumBenchmarkNodeView : Border
{
    private INodeComponent? _node;

    public JaliumBenchmarkNodeView()
    {
        Width = 160d;
        Height = 90d;
        Background = new SolidColorBrush(JColor.FromRgb(0x31, 0x3A, 0x4A));
        BorderBrush = new SolidColorBrush(JColor.FromRgb(0x5B, 0x6B, 0x82));
        BorderThickness = new Thickness(1d);
        Child = new TextBlock
        {
            Text = "node",
            Foreground = JBrushes.White,
            FontSize = 12d,
            Margin = new Thickness(8d),
        };
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        => Bind(DataContext as INodeComponent);

    private void OnUnloaded(object? sender, RoutedEventArgs e)
        => Bind(null);

    private void Bind(INodeComponent? node)
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
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_node is null)
        {
            return;
        }

        Width = Math.Max(1d, _node.Size.Width);
        Height = Math.Max(1d, _node.Size.Height);
        Canvas.SetLeft(this, _node.Anchor.Horizontal);
        Canvas.SetTop(this, _node.Anchor.Vertical);
    }
}

// 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
internal sealed class JaliumBenchmarkLinkView : Border
{
    public JaliumBenchmarkLinkView()
    {
        Width = 1d;
        Height = 1d;
        Background = new SolidColorBrush(JColor.FromRgb(0x4A, 0x6A, 0x9A));
        IsHitTestVisible = false;
    }
}

// Jalium 的模板用 Func<FrameworkElement> 装配；两个模板按运行时类型二选一。
internal sealed class JaliumBenchmarkTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate _node = CreateTemplate(static () => new JaliumBenchmarkNodeView());
    private readonly DataTemplate _link = CreateTemplate(static () => new JaliumBenchmarkLinkView());

    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
        => item is INodeComponent ? _node : _link;

    private static DataTemplate CreateTemplate(Func<FrameworkElement> factory)
    {
        var template = new DataTemplate();
        template.SetVisualTree(factory);
        return template;
    }
}
