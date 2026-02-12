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
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
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

    public static readonly StyledProperty<Transform> GlobalScaleProperty =
        AvaloniaProperty.Register<WorkflowView, Transform>(nameof(GlobalScale));

    public Transform GlobalScale
    {
        get => this.GetValue(GlobalScaleProperty);
        set => SetValue(GlobalScaleProperty, value);
    }

    public Transform CanvasTransform
    {
        get => this.GetValue(CanvasTransformProperty);
        set => SetValue(CanvasTransformProperty, value);
    }

    public WorkflowView()
    {
        InitializeComponent();
        Root_Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3 };

        // 绑定摇杆事件
        PART_ROCKER.JoystickChanged += OnRockerChanged;

        // 创建定时器（初始不启动）
        _rockerPanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ～60 FPS
        _rockerPanTimer.Tick += OnRockerPanTick;
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
        var (success, result) = await WorkflowEx.TryDeserializeFromStreamAsync<TreeViewModel>(value);
        if (success && result is not null)
        {
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
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
        GlobalScale = new TransformGroup()
        {
            Children = [
                new ScaleTransform(_workflowViewModel.Layout.OriginScale.X, _workflowViewModel.Layout.OriginScale.Y)
                ]
        };
    }

    private void UpdateVisibleRegion()
    {
        if (Root_ScrollViewer is not { } viewer || _workflowViewModel is not { } vm) return;

        var scrollOffset = viewer.Offset;
        var viewport = viewer.Viewport;
        var layout = vm.Layout;

        // 获取画布的实际逻辑尺寸
        double canvasLogicalWidth = layout.ActualSize.Width;
        double canvasLogicalHeight = layout.ActualSize.Height;

        // 转换到逻辑坐标空间
        double visibleLeft = (scrollOffset.X - layout.ActualOffset.Left) / layout.OriginScale.X;
        double visibleTop = (scrollOffset.Y - layout.ActualOffset.Top) / layout.OriginScale.Y;
        double visibleWidth = viewport.Width / layout.OriginScale.X;
        double visibleHeight = viewport.Height / layout.OriginScale.Y;

        // 边界约束（确保不超出画布范围）
        visibleLeft = Math.Max(0, Math.Min(visibleLeft, canvasLogicalWidth));
        visibleTop = Math.Max(0, Math.Min(visibleTop, canvasLogicalHeight));
        visibleWidth = Math.Min(visibleWidth, canvasLogicalWidth - visibleLeft);
        visibleHeight = Math.Min(visibleHeight, canvasLogicalHeight - visibleTop);

        if (vm.GetHelper() is TreeHelper helper)
        {
            helper.Virtualize(new Viewport(visibleLeft, visibleTop, visibleWidth, visibleHeight));
        }
    }

    private async void PlusScale(object? sender, RoutedEventArgs e)
    {
        _workflowViewModel.PlusScaleCommand.Execute(null);
        ReLayout(Root_Canvas, _workflowViewModel.Layout);
    }

    private async void MinusScale(object? sender, RoutedEventArgs e)
    {
        _workflowViewModel.MinusScaleCommand.Execute(null);
        ReLayout(Root_Canvas, _workflowViewModel.Layout);
    }

    private async void SimulateData(object? sender, RoutedEventArgs e)
    {
        const int totalNodes = 1000; // 总节点数
        const int batchSize = 100;   // 每批次处理节点数
        const double gridSize = 200; // 标准网格大小
        const double jitter = 30;
        double canvasSize = gridSize * Math.Sqrt(totalNodes);

        var random = new Random(12345);
        var slotTypes = new[] { SlotChannel.MultipleBoth };
        var slotSizes = new[] { new Size(20, 20), new Size(25, 25), new Size(30, 30) };

        int gridCount = (int)Math.Ceiling(Math.Sqrt(totalNodes));
        int generated = 0;
        int i = 0, j = 0;

        var workflowViewModel = _workflowViewModel;
        var workflowHelper = workflowViewModel.GetHelper();

        // 初始化画布
        workflowViewModel.Layout.OriginSize = new Size(canvasSize, canvasSize);
        workflowViewModel.Layout.OriginAlignment = Alignments.TopLeft;
        DataContext = workflowViewModel;

        while (generated < totalNodes)
        {
            var batchNodes = new List<NodeViewModel>();
            var batchSlots = new Dictionary<NodeViewModel, List<SlotViewModel>>(); // 记录每个 node 对应的 slots

            int batchGenerated = 0;
            while (batchGenerated < batchSize && generated < totalNodes)
            {
                if (j >= gridCount) { j = 0; i++; }
                if (i >= gridCount) break;

                double baseX = i * gridSize;
                double baseY = j * gridSize;
                double x = Math.Max(0, Math.Min(baseX + jitter - random.NextDouble() * jitter * 2, canvasSize - 1));
                double y = Math.Max(0, Math.Min(baseY + jitter - random.NextDouble() * jitter * 2, canvasSize - 1));

                var node = new NodeViewModel
                {
                    Size = new Size(180 + random.Next(0, 280), 160 + random.Next(0, 260)),
                    Anchor = new Anchor(x, y, 0)
                };

                // 先只创建 node，不加 slot
                batchNodes.Add(node);
                batchSlots[node] = [];

                // 生成 slot 数据（但暂不挂载）
                int slotCount = random.Next(1, 4);
                for (int s = 0; s < slotCount; s++)
                {
                    var slot = new SlotViewModel
                    {
                        VisualPoint = new(
                            0.9,
                            random.NextDouble()
                        ),
                        Size = slotSizes[random.Next(slotSizes.Length)],
                        Channel = slotTypes[random.Next(slotTypes.Length)]
                    };
                    batchSlots[node].Add(slot);
                }

                generated++;
                batchGenerated++;
                j++;
            }

            // 🔑 关键步骤：先批量挂载所有 nodes 到 tree
            foreach (var node in batchNodes)
            {
                workflowHelper.CreateNode(node); // 此时 node 已属于 Tree，DataContext 和绑定生效
            }

            // 🔑 再批量为已挂载的 nodes 添加 slots
            foreach (var kvp in batchSlots)
            {
                var node = kvp.Key;
                var slots = kvp.Value;
                var nodeHelper = node.GetHelper(); // 此时 helper 应已正确初始化（因 node 已挂载）

                foreach (var slot in slots)
                {
                    nodeHelper.CreateSlot(slot); // 安全：node 已在树中
                }
            }

            workflowHelper.ClearHistory();
            _manager.Show(new Notification("Progress", $"Generated {generated:N0} / {totalNodes:N0} nodes"));

            await Task.Yield(); // 让 UI 线程有机会刷新

            if (this.Parent is null) break;
        }

        workflowViewModel.Layout.UpdateCommand.Execute(null);
        ReLayout(Root_Canvas, _workflowViewModel.Layout);
        _manager.Show(new Notification("OK", $"Completed! Generated {generated:N0} nodes"));
    }
}