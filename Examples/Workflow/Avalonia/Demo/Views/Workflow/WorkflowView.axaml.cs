using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System;
using System.Collections.Specialized;
using System.IO;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();
    private WindowNotificationManager _manager;

    public static readonly StyledProperty<Transform> CanvasTransformProperty =
        AvaloniaProperty.Register<WorkflowView, Transform>(nameof(CanvasTransform));

    public Transform CanvasTransform
    {
        get => this.GetValue(CanvasTransformProperty);
        set => SetValue(CanvasTransformProperty, value);
    }

    public WorkflowView()
    {
        InitializeComponent();
        DataContext = _workflowViewModel;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3 };

        SubscribeAutoScroll(_workflowViewModel);
        InitializeNetworkDemo();
    }

    private async void SaveWorkflow(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "保存 Workflow.json 到指定的目录中"
            });
        if (folder.Count < 1) return;
        var path = folder[0].TryGetLocalPath();
        if (path is null) return;
        _workflowViewModel.SaveCommand.Execute(Path.Combine(path, "Workflow.json"));
        _manager.Show(new Notification("OK", $"Workflow Saved At {path}"));
    }

    private async void SelectWorkflow(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var file = await topLevel.StorageProvider.OpenFilePickerAsync(
           new FilePickerOpenOptions
           {
               Title = "从Json文件加载工作流",
               AllowMultiple = false,
               FileTypeFilter = [FilePickerFileTypes.Json]
           });
        if (file.Count < 1) return;
        var path = file[0].TryGetLocalPath();
        await using var value = await file[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await value.CopyToAsync(ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var json = await reader.ReadToEndAsync();
        var success = json.TryDeserialize<TreeViewModel>(out var result);
        if (success && result is not null)
        {
            UnsubscribeAutoScroll(_workflowViewModel);
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
            SubscribeAutoScroll(_workflowViewModel);
            WorkflowSurfaceBehavior.Refresh(this);
            _manager.Show(new Notification("OK", $"Workflow Loaded From {path}"));
        }
    }

    private void LoadNetworkDemo(object? sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
        _manager.Show(new Notification("OK", "Workflow demo loaded."));
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

    private void OnSendToAgent(object? sender, RoutedEventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _workflowViewModel.AskCommand.Execute(text);
        AgentInput!.Text = string.Empty;
    }

    private void OnAgentInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
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
        {
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;
        }
    }

    private void UnsubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }
    }

    private void OnAgentToolCalled()
    {
        Dispatcher.UIThread.Post(() => WorkflowSurfaceBehavior.Refresh(this));
    }

    private void OnVisualRefreshRequested()
    {
        Dispatcher.UIThread.Post(() => WorkflowSurfaceBehavior.Refresh(this), DispatcherPriority.Background);
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ScrollListToEnd(AgentLogScroller), DispatcherPriority.Background);
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollListToEnd(ListBox? listBox)
    {
        if (listBox is null || listBox.ItemCount == 0) return;
        listBox.ScrollIntoView(listBox.ItemCount - 1);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.Offset = new Vector(scroller.Offset.X, scroller.Extent.Height);
    }
}