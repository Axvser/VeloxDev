using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia_StyleGraph.ViewModels;
using Avalonia_StyleGraph.ViewModels.Workflow;
using Avalonia_StyleGraph.ViewModels.Workflow.Helper;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.Views;

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["white"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["white"], ["#1e1e1e"])]
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        InitializeTheme();
    }

    private void ChangeThemeColor(object? sender, RoutedEventArgs e)
    {
        ThemeManager.Transition(
            ThemeManager.Current == typeof(Dark) ? typeof(Light) : typeof(Dark),
            TransitionEffects.Theme
            );
    }

    private async void ToolItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.Tag is string nodeType)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(nodeType));
            _ = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
    }

    private void SimulateData(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // 给 Tree 挂载 Node
        CreateStyleNode(vm.StyleGraphViewModel);
        CreateStyleNode(vm.StyleGraphViewModel);
        CreateTriggerNode(vm.StyleGraphViewModel,true);
        CreateTriggerNode(vm.StyleGraphViewModel, false);
        CreateProcessorNode(vm.StyleGraphViewModel);

        // 清理历史栈，避免非法的重做与撤销
        vm.StyleGraphViewModel.GetHelper().ClearHistory();
    }

    private static IWorkflowNodeViewModel CreateTriggerNode(StyleGraphViewModel graph,bool isHovered)
    {
        var inputSlot = new SlotViewModel
        {
            Offset = new Offset(20, 80),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneSource,
        };
        var outputSlot = new SlotViewModel
        {
            Offset = new Offset(140, 80),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneTarget,
        };

        var node = new HoverTriggerViewModel
        {
            Size = new Size(180, 180),
            Anchor = new Anchor(100, 200, 1),
            PointerHoverd = isHovered,
        };

        graph.GetHelper().CreateNode(node);

        node.GetHelper().CreateSlot(inputSlot);
        node.GetHelper().CreateSlot(outputSlot);
        return node;
    }

    private static IWorkflowNodeViewModel CreateStyleNode(StyleGraphViewModel graph)
    {
        var slot = new SlotViewModel
        {
            Offset = new Offset(160, 80),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneTarget,
        };

        var node = new HoverStyleViewModel
        {
            Size = new Size(200, 300),
            Anchor = new Anchor(400, 200, 1)
        };

        graph.GetHelper().CreateNode(node);

        node.GetHelper().CreateSlot(slot);
        return node;
    }

    private IWorkflowNodeViewModel CreateProcessorNode(StyleGraphViewModel graph)
    {
        var inputSlot = new SlotViewModel
        {
            Offset = new Offset(0, 80),   // 左侧中间
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleSources,
        };

        var node = new HoverProcessorViewModel
        {
            Size = new Size(180, 180),
            Anchor = new Anchor(400, 400, 1) // 靠右
        };

        if(node.GetHelper() is ProcessorHelper helper)
        {
            helper.Host = PreviewItem;
        }

        graph.GetHelper().CreateNode(node);

        node.GetHelper().CreateSlot(inputSlot);
        return node;
    }
}