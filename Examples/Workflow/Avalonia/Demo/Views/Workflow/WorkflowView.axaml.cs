using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using System;
using System.IO;
using VeloxDev.Core.Extension;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using Size = VeloxDev.Core.WorkflowSystem.Size;

namespace Demo;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();
    private WindowNotificationManager _manager;
    private DispatcherTimer? _rockerPanTimer;
    private const double PanSpeed = 8.0;

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

        // 绑定摇杆事件
        PART_ROCKER.JoystickChanged += OnRockerChanged;

        // 创建定时器（初始不启动）
        _rockerPanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ～60 FPS
        _rockerPanTimer.Tick += OnRockerPanTick;

        InitializeNetworkDemo();
    }

    private void OnRockerChanged(object? sender, Vector e)
    {
        // 如果摇杆几乎居中，停止平移
        if (Math.Abs(e.X) < 0.05 && Math.Abs(e.Y) < 0.05)
        {
            _rockerPanTimer?.Stop();
            return;
        }

        // 启动定时器（如果未运行）
        if (!_rockerPanTimer!.IsEnabled)
        {
            _rockerPanTimer.Start();
        }
    }

    private void OnRockerPanTick(object? sender, EventArgs e)
    {
        if (Root_ScrollViewer == null || PART_ROCKER == null) return;

        var x = PART_ROCKER.X;
        var y = PART_ROCKER.Y;

        // 微小死区
        if (Math.Abs(x) < 0.05 && Math.Abs(y) < 0.05)
        {
            _rockerPanTimer?.Stop();
            return;
        }

        // 计算位移（注意方向）
        double deltaX = x * PanSpeed; // X>0 → 向右滚动（内容左移）→ Offset.X 增加
        double deltaY = y * PanSpeed; // Y>0 → 向下滚动 → Offset.Y 增加

        var currentOffset = Root_ScrollViewer.Offset;
        var newOffsetX = currentOffset.X + deltaX;
        var newOffsetY = currentOffset.Y + deltaY;

        // 获取最大滚动范围
        var maxH = GetHorizontalScrollMaximum(Root_ScrollViewer);
        var maxV = GetVerticalScrollMaximum(Root_ScrollViewer);

        // 边界检测 + 自动扩展布局（与按钮逻辑一致）
        if (newOffsetX < 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(-newOffsetX, 0);
            newOffsetX = 0;
            ReLayout(Root_Canvas, _workflowViewModel.Layout); // 必须刷新 transform
        }
        else if (newOffsetX > maxH)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(newOffsetX - maxH, 0);
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
            // 重新计算 maxH，因为 layout 扩展了
            maxH = GetHorizontalScrollMaximum(Root_ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
        }

        if (newOffsetY < 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(0, -newOffsetY);
            newOffsetY = 0;
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
        }
        else if (newOffsetY > maxV)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(0, newOffsetY - maxV);
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
            maxV = GetVerticalScrollMaximum(Root_ScrollViewer);
            newOffsetY = Math.Min(newOffsetY, maxV);
        }

        // 应用新偏移
        Root_ScrollViewer.Offset = new Vector(
            Math.Max(0, Math.Min(newOffsetX, maxH)),
            Math.Max(0, Math.Min(newOffsetY, maxV))
        );

        UpdateVisibleRegion();
    }

    private async void SaveWorkflow(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        _manager.Show(new Notification("No", $"Workflow Saved"));
        if (topLevel is null) return;
        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "保存 Workflow.json 到指定的目录中"
            });
        if (folder.Count < 1) return;
        var path = folder[0].TryGetLocalPath();
        _manager.Show(new Notification("Pre", $"Workflow Saved At {path}"));
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
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
            ReLayout(Root_Canvas, _workflowViewModel.Layout);
            UpdateVisibleRegion();
            _manager.Show(new Notification("OK", $"Workflow Loaded From {path}"));
        }
    }

    private void OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        var point = e.GetPosition(Root_Canvas);
        _workflowViewModel.SetPointerCommand.Execute(new Anchor(
            point.X - _workflowViewModel.Layout.ActualOffset.Left,
            point.Y - _workflowViewModel.Layout.ActualOffset.Top,
            0));
    }

    private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
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
        Root_GridDecorator.ContentOffsetX = _workflowViewModel.Layout.ActualOffset.Left;
        Root_GridDecorator.ContentOffsetY = _workflowViewModel.Layout.ActualOffset.Top;
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
        switch (layout.OriginAlignment)
        {
            case Alignments.TopLeft:
                canvas.VerticalAlignment = VerticalAlignment.Top;
                canvas.HorizontalAlignment = HorizontalAlignment.Left;
                canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                break;
            case Alignments.TopCenter:
                canvas.VerticalAlignment = VerticalAlignment.Top;
                canvas.HorizontalAlignment = HorizontalAlignment.Left;
                canvas.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                break;
            case Alignments.TopRight:
                canvas.VerticalAlignment = VerticalAlignment.Top;
                canvas.HorizontalAlignment = HorizontalAlignment.Right;
                canvas.RenderTransformOrigin = new RelativePoint(1, 0, RelativeUnit.Relative);
                break;
            case Alignments.CenterLeft:
                canvas.VerticalAlignment = VerticalAlignment.Center;
                canvas.HorizontalAlignment = HorizontalAlignment.Left;
                canvas.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
                break;
            case Alignments.Center:
                canvas.VerticalAlignment = VerticalAlignment.Center;
                canvas.HorizontalAlignment = HorizontalAlignment.Center;
                canvas.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                break;
            case Alignments.CenterRight:
                canvas.VerticalAlignment = VerticalAlignment.Center;
                canvas.HorizontalAlignment = HorizontalAlignment.Right;
                canvas.RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
                break;
            case Alignments.BottomLeft:
                canvas.VerticalAlignment = VerticalAlignment.Bottom;
                canvas.HorizontalAlignment = HorizontalAlignment.Left;
                canvas.RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
                break;
            case Alignments.BottomCenter:
                canvas.VerticalAlignment = VerticalAlignment.Bottom;
                canvas.HorizontalAlignment = HorizontalAlignment.Center;
                canvas.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
                break;
            case Alignments.BottomRight:
                canvas.VerticalAlignment = VerticalAlignment.Bottom;
                canvas.HorizontalAlignment = HorizontalAlignment.Right;
                canvas.RenderTransformOrigin = new RelativePoint(1, 1, RelativeUnit.Relative);
                break;
        }
        CanvasTransform = new TransformGroup()
        {
            Children = [
                new TranslateTransform(
                    _workflowViewModel.Layout.ActualOffset.Left,
                    _workflowViewModel.Layout.ActualOffset.Top
                    )
                ]
        };
        UpdateGridDecorator();
    }

    private void UpdateVisibleRegion()
    {
        if (Root_ScrollViewer is not { } viewer || _workflowViewModel is not { } vm) return;

        UpdateGridDecorator();

        if (vm.GetHelper() is TreeHelper helper)
        {
            helper.Virtualize(new Viewport(
                viewer.Offset.X - vm.Layout.ActualOffset.Left,
                viewer.Offset.Y - vm.Layout.ActualOffset.Top,
                viewer.Viewport.Width,
                viewer.Viewport.Height));
        }
    }

    private void LoadNetworkDemo(object? sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
        _manager.Show(new Notification("OK", "HTTP workflow demo loaded."));
    }

    private void InitializeNetworkDemo()
    {
        _workflowViewModel = CreateNetworkDemo();
        DataContext = _workflowViewModel;
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        ReLayout(Root_Canvas, _workflowViewModel.Layout);
        UpdateVisibleRegion();
    }

    private static TreeViewModel CreateNetworkDemo()
    {
        var tree = new TreeViewModel();
        tree.Layout.OriginAlignment = Alignments.TopLeft;
        tree.Layout.OriginSize = new Size(2200, 980);

        var helper = tree.GetHelper();
        var nodeSize = new Size(300, 260);
        var controllerSize = new Size(220, 170);

        NodeViewModel CreateNode(
            string title,
            NetworkRequestMethod method,
            string url,
            string captureKey,
            double left,
            double top,
            string headers = "",
            string bodyTemplate = "")
            => new()
            {
                Title = title,
                Method = method,
                Url = url,
                Headers = headers,
                BodyTemplate = bodyTemplate,
                CaptureKey = captureKey,
                Size = nodeSize,
                Anchor = new Anchor(left, top, 0),
            };

        var controller = new ControllerViewModel
        {
            Size = controllerSize,
            Anchor = new Anchor(60, 360, 0),
            SeedPayload = "demo-request-chain",
            BroadcastMode = WorkflowBroadcastMode.BreadthFirst,
        };

        var fetchTodo = CreateNode(
            "Fetch Todo",
            NetworkRequestMethod.Get,
            "https://jsonplaceholder.typicode.com/todos/1",
            "todo",
            280,
            120);

        var fetchPost = CreateNode(
            "Fetch Post",
            NetworkRequestMethod.Get,
            "https://jsonplaceholder.typicode.com/posts/1",
            "post",
            280,
            470);

        var auditTodo = CreateNode(
            "Audit Todo",
            NetworkRequestMethod.Post,
            "https://httpbin.org/post",
            "audit",
            640,
            20,
            headers: "X-Demo-Source: VeloxDev Workflow",
            bodyTemplate: "{\"todoSummary\":\"{{todo.summary}}\",\"todoStatus\":\"{{todo.status}}\"}");

        var patchRemote = CreateNode(
            "Patch Remote",
            NetworkRequestMethod.Patch,
            "https://httpbin.org/patch",
            "patch",
            640,
            250,
            bodyTemplate: "{\"todoUrl\":\"{{todo.url}}\",\"status\":\"processed\"}");

        var deleteRemote = CreateNode(
            "Delete Remote",
            NetworkRequestMethod.Delete,
            "https://httpbin.org/delete",
            "delete",
            640,
            480,
            headers: "X-Delete-Reason: {{todo.status}}");

        var syncPost = CreateNode(
            "Sync Post",
            NetworkRequestMethod.Post,
            "https://httpbin.org/post",
            "sync",
            1000,
            140,
            bodyTemplate: "{\"postUrl\":\"{{post.url}}\",\"summary\":\"{{post.summary}}\"}");

        var mirrorPost = CreateNode(
            "Mirror Post",
            NetworkRequestMethod.Put,
            "https://httpbin.org/put",
            "mirror",
            1000,
            390,
            bodyTemplate: "{\"body\":\"{{post.body}}\",\"status\":\"mirrored\"}");

        var mergeResults = CreateNode(
            "Merge Results",
            NetworkRequestMethod.Post,
            "https://httpbin.org/post",
            "merge",
            1360,
            210,
            bodyTemplate: "{\"latest\":\"{{last.summary}}\",\"source\":\"{{last.url}}\"}");

        var archiveTrace = CreateNode(
            "Archive Trace",
            NetworkRequestMethod.Post,
            "https://httpbin.org/post",
            "archive",
            1720,
            210,
            bodyTemplate: "{\"merge\":\"{{merge.summary}}\",\"seed\":\"{{seed}}\"}");

        foreach (var node in new IWorkflowNodeViewModel[]
        {
            controller,
            fetchTodo,
            fetchPost,
            auditTodo,
            patchRemote,
            deleteRemote,
            syncPost,
            mirrorPost,
            mergeResults,
            archiveTrace,
        })
        {
            helper.CreateNode(node);
        }

        controller.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        fetchTodo.InputSlot = CreateInputSlot();
        fetchTodo.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        fetchPost.InputSlot = CreateInputSlot();
        fetchPost.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        auditTodo.InputSlot = CreateInputSlot();
        auditTodo.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        patchRemote.InputSlot = CreateInputSlot();
        patchRemote.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        deleteRemote.InputSlot = CreateInputSlot();
        deleteRemote.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        syncPost.InputSlot = CreateInputSlot();
        syncPost.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        mirrorPost.InputSlot = CreateInputSlot();
        mirrorPost.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        mergeResults.InputSlot = CreateInputSlot(SlotChannel.MultipleSources);
        mergeResults.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        archiveTrace.InputSlot = CreateInputSlot();

        Connect(tree, controller.OutputSlot!, fetchTodo.InputSlot!);
        Connect(tree, controller.OutputSlot!, fetchPost.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, auditTodo.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, patchRemote.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, deleteRemote.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, syncPost.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, mirrorPost.InputSlot!);
        Connect(tree, auditTodo.OutputSlot!, mergeResults.InputSlot!);
        Connect(tree, patchRemote.OutputSlot!, mergeResults.InputSlot!);
        Connect(tree, deleteRemote.OutputSlot!, mergeResults.InputSlot!);
        Connect(tree, syncPost.OutputSlot!, mergeResults.InputSlot!);
        Connect(tree, mirrorPost.OutputSlot!, mergeResults.InputSlot!);
        Connect(tree, mergeResults.OutputSlot!, archiveTrace.InputSlot!);

        helper.ClearHistory();
        return tree;
    }

    private static SlotViewModel CreateInputSlot(SlotChannel channel = SlotChannel.OneSource)
        => new()
        {
            Size = new Size(20, 20),
            Channel = channel,
        };

    private static SlotViewModel CreateOutputSlot(SlotChannel channel)
        => new()
        {
            Size = new Size(20, 20),
            Channel = channel,
        };

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}