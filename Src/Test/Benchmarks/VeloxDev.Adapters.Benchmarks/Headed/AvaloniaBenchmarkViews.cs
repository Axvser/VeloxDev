using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using VeloxDev.WorkflowSystem;
using AvaloniaColor = Avalonia.Media.Color;
using AvaloniaBrushes = Avalonia.Media.Brushes;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 刻意做小：一张边框 + 一段文字，但订阅是真实的 Anchor/Size。
internal sealed class AvaloniaBenchmarkNodeView : Border
{
    private IWorkflowNodeViewModel? _node;

    public AvaloniaBenchmarkNodeView()
    {
        Width = 160d;
        Height = 90d;
        Background = new SolidColorBrush(AvaloniaColor.FromRgb(0x31, 0x3A, 0x4A));
        BorderBrush = new SolidColorBrush(AvaloniaColor.FromRgb(0x5B, 0x6B, 0x82));
        BorderThickness = new Avalonia.Thickness(1d);
        Child = new TextBlock
        {
            Text = "node",
            Foreground = AvaloniaBrushes.White,
            FontSize = 12d,
            Margin = new Avalonia.Thickness(8d),
        };
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
        => Bind(DataContext as IWorkflowNodeViewModel);

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
        => Bind(null);

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
            return;
        }

        Width = Math.Max(1d, _node.Size.Width);
        Height = Math.Max(1d, _node.Size.Height);
        Canvas.SetLeft(this, _node.Anchor.Horizontal);
        Canvas.SetTop(this, _node.Anchor.Vertical);
    }
}

// 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
internal sealed class AvaloniaBenchmarkLinkView : Border
{
    public AvaloniaBenchmarkLinkView()
    {
        Width = 1d;
        Height = 1d;
        Background = new SolidColorBrush(AvaloniaColor.FromRgb(0x4A, 0x6A, 0x9A));
        IsHitTestVisible = false;
    }
}

internal static class AvaloniaBenchmarkTemplates
{
    internal static IDataTemplate Create()
        => new FuncDataTemplate<IWorkflowViewModel>((item, _) =>
            item is IWorkflowNodeViewModel ? new AvaloniaBenchmarkNodeView() : new AvaloniaBenchmarkLinkView());
}
