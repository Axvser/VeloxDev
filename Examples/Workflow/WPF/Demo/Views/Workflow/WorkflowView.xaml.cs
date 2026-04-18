using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();
    private bool _isPanning;
    private Point _panStart;
    private Vector _panStartOffset;

    public WorkflowView()
    {
        InitializeComponent();
        InitializeNetworkDemo();
    }

    private void OnCanvasPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panStartOffset = new Vector(RootScrollViewer.HorizontalOffset, RootScrollViewer.VerticalOffset);
            Mouse.Capture(RootScrollViewer);
            e.Handled = true;
        }
    }

    private async void SelectWorkflow(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择工作流文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                using var stream = File.OpenRead(dialog.FileName);
                var (success, result) = await ComponentModelEx.TryDeserializeFromStreamAsync<TreeViewModel>(stream);

                if (!success || result is null)
                {
                    MessageBox.Show("文件格式不正确或解析失败。", "加载失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UnsubscribeAutoScroll(_workflowViewModel);
                _workflowViewModel = result;
                DataContext = _workflowViewModel;
                SubscribeAutoScroll(_workflowViewModel);
                UpdateVisibleRegion();

                MessageBox.Show($"工作流已从 {dialog.FileName} 加载成功。", "加载成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveWorkflow(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;

        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            tree.SaveCommand.Execute(Path.Combine(dialog.FolderName, "Workflow.json"));
            MessageBox.Show($"Workflow Saved At {dialog.FolderName}", "OK");
        }
    }

    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var current = e.GetPosition(this);
            var deltaX = _panStart.X - current.X;
            var deltaY = _panStart.Y - current.Y;
            RootScrollViewer.ScrollToHorizontalOffset(_panStartOffset.X + deltaX);
            RootScrollViewer.ScrollToVerticalOffset(_panStartOffset.Y + deltaY);
            e.Handled = true;
            return;
        }

        if (DataContext is not TreeViewModel tree) return;
        var point = e.GetPosition(WorkflowCanvas);
        tree.SetPointerCommand.Execute(new Anchor(
            point.X - tree.Layout.ActualOffset.Horizontal,
            point.Y - tree.Layout.ActualOffset.Vertical,
            0));
    }

    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Mouse.Capture(null);
            e.Handled = true;
            return;
        }

        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        tree.ResetVirtualLinkCommand.Execute(null);
    }

    private void LoadNetworkDemo(object sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
    }

    private void InitializeNetworkDemo()
    {
        UnsubscribeAutoScroll(_workflowViewModel);
        _workflowViewModel = WorkflowDemoSession.Create().Tree;
        DataContext = _workflowViewModel;
        SubscribeAutoScroll(_workflowViewModel);
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        UpdateVisibleRegion();
    }

    private void OnSendToAgent(object sender, RoutedEventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _workflowViewModel.AskCommand.Execute(text);
        AgentInput!.Text = string.Empty;
    }

    private void OnAgentInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSendToAgent(sender, e);
            e.Handled = true;
        }
    }

    private void SubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
            helper.ToolCalled += OnAgentToolCalled;
    }

    private void UnsubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
            helper.ToolCalled -= OnAgentToolCalled;
    }

    private void OnAgentToolCalled()
    {
        Dispatcher.InvokeAsync(UpdateVisibleRegion, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateVisibleRegion();
    }

    private void UpdateVisibleRegion()
    {
        if (RootScrollViewer is not { } viewer || _workflowViewModel is not { } vm) return;

        var helper = vm.GetHelper();
        helper.Virtualize(new Viewport(
            viewer.HorizontalOffset - vm.Layout.ActualOffset.Horizontal,
            viewer.VerticalOffset - vm.Layout.ActualOffset.Vertical,
            viewer.ViewportWidth,
            viewer.ViewportHeight));
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (AgentLogScroller is not { Items.Count: > 0 } lb) return;
            lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.ScrollToEnd();
    }
}