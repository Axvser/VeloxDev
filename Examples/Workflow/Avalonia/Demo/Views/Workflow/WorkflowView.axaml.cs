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
    private bool _isPanning;
    private Point _panStart;
    private Vector _panStartOffset;

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
        Root_Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3 };

        SubscribeAutoScroll(_workflowViewModel);
        InitializeNetworkDemo();
    }

    private void OnCanvasPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // 中键拖拽平移画布
        if (e.GetCurrentPoint(Root_ScrollViewer).Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panStartOffset = Root_ScrollViewer.Offset;
            e.Handled = true;
        }
    }

    private void OnCanvasPanMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isPanning) return;

        var current = e.GetPosition(this);
        var deltaX = _panStart.X - current.X;
        var deltaY = _panStart.Y - current.Y;

        var newOffsetX = _panStartOffset.X + deltaX;
        var newOffsetY = _panStartOffset.Y + deltaY;

        var maxH = GetHorizontalScrollMaximum(Root_ScrollViewer);
        var maxV = GetVerticalScrollMaximum(Root_ScrollViewer);

        bool layoutChanged = false;

        if (newOffsetX < 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(-newOffsetX, 0);
            newOffsetX = 0;
            layoutChanged = true;
        }
        else if (newOffsetX > maxH)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(newOffsetX - maxH, 0);
            newOffsetX = maxH;
            layoutChanged = true;
        }

        if (newOffsetY < 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(0, -newOffsetY);
            newOffsetY = 0;
            layoutChanged = true;
        }
        else if (newOffsetY > maxV)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(0, newOffsetY - maxV);
            newOffsetY = maxV;
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
            maxH = GetHorizontalScrollMaximum(Root_ScrollViewer);
            maxV = GetVerticalScrollMaximum(Root_ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);

            // 重置拖拽基准点，防止下次 move 再次累加相同偏移
            _panStart = current;
            _panStartOffset = new Vector(
                Math.Max(0, Math.Min(newOffsetX, maxH)),
                Math.Max(0, Math.Min(newOffsetY, maxV)));
        }

        Root_ScrollViewer.Offset = new Vector(
            Math.Max(0, Math.Min(newOffsetX, maxH)),
            Math.Max(0, Math.Min(newOffsetY, maxV))
        );

        UpdateVisibleRegion();
        e.Handled = true;
    }

    private void OnCanvasPanReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            e.Handled = true;
        }
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
        var value = await file[0].OpenReadAsync();
        var (success, result) = await ComponentModelEx.TryDeserializeFromStreamAsync<TreeViewModel>(value);
        if (success && result is not null)
        {
            UnsubscribeAutoScroll(_workflowViewModel);
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
            SubscribeAutoScroll(_workflowViewModel);
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
            UpdateVisibleRegion();
            _manager.Show(new Notification("OK", $"Workflow Loaded From {path}"));
        }
    }

    private void OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        OnCanvasPanMoved(sender, e);
        if (_isPanning) return;

        var point = e.GetPosition(Root_Canvas);
        _workflowViewModel.SetPointerCommand.Execute(new Anchor(
            point.X - _workflowViewModel.Layout.ActualOffset.Horizontal,
            point.Y - _workflowViewModel.Layout.ActualOffset.Vertical,
            0));
    }

    private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        OnCanvasPanReleased(sender, e);
        if (e.Handled) return;

        _workflowViewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        _workflowViewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateVisibleRegion();
    }

    private void UpdateGridDecorator()
    {
        if (Root_GridDecorator is null)
        {
            return;
        }

        Root_GridDecorator.ScrollOffsetX = Root_ScrollViewer?.Offset.X ?? 0;
        Root_GridDecorator.ScrollOffsetY = Root_ScrollViewer?.Offset.Y ?? 0;
        Root_GridDecorator.ContentOffsetX = _workflowViewModel.Layout.ActualOffset.Horizontal;
        Root_GridDecorator.ContentOffsetY = _workflowViewModel.Layout.ActualOffset.Vertical;
    }

    public static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) return 0;

        var extent = scrollViewer.Extent;
        var viewport = scrollViewer.Viewport;

        // 最大滚动值 = 内容宽度 - 可视区域宽度
        double maxScroll = Math.Max(0, extent.Width - viewport.Width);
        return maxScroll;
    }

    public static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) return 0;

        var extent = scrollViewer.Extent;
        var viewport = scrollViewer.Viewport;

        // 最大滚动值 = 内容宽度 - 可视区域宽度
        double maxScroll = Math.Max(0, extent.Height - viewport.Height);
        return maxScroll;
    }

    private void ReLayout(Canvas canvas, CanvasLayout layout)
    {
        canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        CanvasTransform = new TransformGroup()
        {
            Children = [
                new TranslateTransform(
                    _workflowViewModel.Layout.ActualOffset.Horizontal,
                    _workflowViewModel.Layout.ActualOffset.Vertical
                    )
                ]
        };
        UpdateGridDecorator();
    }

    private void UpdateVisibleRegion()
    {
        if (Root_ScrollViewer is not { } viewer || _workflowViewModel is not { } vm) return;

        UpdateGridDecorator();

        var viewport = new Viewport(
                viewer.Offset.X - vm.Layout.ActualOffset.Horizontal,
                viewer.Offset.Y - vm.Layout.ActualOffset.Vertical,
                viewer.Viewport.Width,
                viewer.Viewport.Height);

        vm.GetHelper().Viewport = viewport;
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
        ReLayout(Root_Canvas, _workflowViewModel.Layout);
        UpdateVisibleRegion();
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
        Dispatcher.UIThread.Post(UpdateVisibleRegion);
    }

    private void OnVisualRefreshRequested()
    {
        // Deferred: let Avalonia finish layout for any newly created slot views,
        // then nudge every visible node to trigger SyncSlotLayouts in each node view.
        Dispatcher.UIThread.Post(() =>
        {
            if (_workflowViewModel is not { } vm) return;
            if (vm.GetHelper() is not AgentHelper helper) return;
            foreach (var node in helper.VisibleItems ?? [])
            {
                if (node is IWorkflowNodeViewModel n)
                {
                    n.MoveCommand.Execute(new Offset(0.5, 0));
                    n.MoveCommand.Execute(new Offset(-0.5, 0));
                }
            }
        }, DispatcherPriority.Background);
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