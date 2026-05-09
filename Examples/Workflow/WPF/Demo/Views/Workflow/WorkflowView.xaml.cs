using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.MVVM.Serialization;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();

    public static readonly DependencyProperty CanvasTransformProperty = DependencyProperty.Register(
        nameof(CanvasTransform),
        typeof(Transform),
        typeof(WorkflowView),
        new PropertyMetadata(Transform.Identity));

    public WorkflowView()
    {
        InitializeComponent();
        DataContext = _workflowViewModel;
        InitializeNetworkDemo();
    }

    public Transform CanvasTransform
    {
        get => (Transform)GetValue(CanvasTransformProperty);
        set => SetValue(CanvasTransformProperty, value);
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
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var result = json.Deserialize<TreeViewModel>();

                UnsubscribeAutoScroll(_workflowViewModel);
                _workflowViewModel = result;
                DataContext = _workflowViewModel;
                SubscribeAutoScroll(_workflowViewModel);
                 WorkflowSurfaceBehavior.Refresh(this);

                MessageBox.Show($"工作流已从 {dialog.FileName} 加载成功。", "加载成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败：{ex.GetType().Name}\n{ex.Message}", "错误",
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
        WorkflowSurfaceBehavior.Refresh(this);
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
        Dispatcher.InvokeAsync(() => WorkflowSurfaceBehavior.Refresh(this), System.Windows.Threading.DispatcherPriority.Background);
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