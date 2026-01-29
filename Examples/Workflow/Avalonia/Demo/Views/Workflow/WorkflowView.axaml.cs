using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
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
    private const double PanSpeed = 8.0; // 像素/帧（可调）

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
            ReLayout(); // 必须刷新 transform
        }
        else if (newOffsetX > maxH)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(newOffsetX - maxH, 0);
            ReLayout();
            // 重新计算 maxH，因为 layout 扩展了
            maxH = GetHorizontalScrollMaximum(Root_ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
        }

        if (newOffsetY < 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(0, -newOffsetY);
            newOffsetY = 0;
            ReLayout();
        }
        else if (newOffsetY > maxV)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(0, newOffsetY - maxV);
            ReLayout();
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
        _workflowViewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private void SimulateData(object? sender, RoutedEventArgs e)
    {
        const int totalNodes = 1_000_000;
        const double gridSize = 150; // 格子大小（也是最小间距基础）
        const double jitter = 30;    // 抖动幅度（±jitter）
        double canvasSize = gridSize * Math.Sqrt(totalNodes); // ≈150,000

        var random = new Random(12345); // 固定种子
        var workflowHelper = _workflowViewModel.GetHelper();

        var slotTypes = new[]
        {
        SlotChannel.OneBoth,
        SlotChannel.MultipleTargets,
        SlotChannel.MultipleSources
    };

        var slotSizes = new[]
        {
        new Size(20, 20),
        new Size(25, 25),
        new Size(30, 30)
    };

        // 计算网格行列数
        int gridCount = (int)Math.Ceiling(Math.Sqrt(totalNodes)); // 1000 for 1M
        int generated = 0;

        for (int i = 0; i < gridCount && generated < totalNodes; i++)
        {
            for (int j = 0; j < gridCount && generated < totalNodes; j++)
            {
                // 格子左上角
                double baseX = i * gridSize;
                double baseY = j * gridSize;

                // 在格子内随机抖动（避免完全对齐）
                double x = baseX + jitter - random.NextDouble() * jitter * 2;
                double y = baseY + jitter - random.NextDouble() * jitter * 2;

                // 边界保护
                x = Math.Max(0, Math.Min(x, canvasSize - 1));
                y = Math.Max(0, Math.Min(y, canvasSize - 1));

                var node = new NodeViewModel()
                {
                    Size = new Size(
                        width: 80 + random.Next(0, 80),   // 80~160
                        height: 60 + random.Next(0, 60)   // 60~120
                    ),
                    Anchor = new Anchor(x, y, 0) // 所有在同一层简化
                };

                // 添加 1~3 个插槽
                int slotCount = random.Next(1, 4);
                for (int s = 0; s < slotCount; s++)
                {
                    node.GetHelper().CreateSlot(new SlotViewModel
                    {
                        Offset = new Offset(
                            left: 5 + random.Next(0, (int)node.Size.Width - 40),
                            top: 5 + random.Next(0, (int)node.Size.Height - 40)
                        ),
                        Size = slotSizes[random.Next(slotSizes.Length)],
                        Channel = slotTypes[random.Next(slotTypes.Length)]
                    });
                }

                workflowHelper.CreateNode(node);
                generated++;

                // 每 10,000 个清理一次历史（避免 Undo 栈爆炸）
                if (generated % 10_000 == 0)
                {
                    _workflowViewModel.GetHelper().ClearHistory();
                    GC.Collect(); // 可选：强制回收（仅调试用）
                }
            }
        }

        _workflowViewModel.GetHelper().ClearHistory();
        _workflowViewModel.Layout.OriginSize = new Size(150000,150000);
        DataContext = _workflowViewModel;
        _workflowViewModel.Layout.OriginAlign = OriginAligns.TopLeft;
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        ReLayout();

        _manager.Show(new Notification("OK", $"Generated {generated:N0} nodes in {canvasSize:N0}×{canvasSize:N0} space"));
    }
    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (Root_ScrollViewer.Offset.X <= 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(2, 0);
        }
        Root_ScrollViewer.Offset = new Vector(
            Math.Clamp(Root_ScrollViewer.Offset.X - 2, 0, double.MaxValue),
            Root_ScrollViewer.Offset.Y
            );
        ReLayout();
    }
    private void Button_Click1(object? sender, RoutedEventArgs e)
    {
        if (Root_ScrollViewer.Offset.Y <= 0)
        {
            _workflowViewModel.Layout.NegativeOffset += new Offset(0, 2);
        }
        Root_ScrollViewer.Offset = new Vector(
            Root_ScrollViewer.Offset.X,
            Math.Clamp(Root_ScrollViewer.Offset.Y - 2, 0, double.MaxValue)
            );
        ReLayout();
    }
    private void Button_Click2(object? sender, RoutedEventArgs e)
    {
        if (GetHorizontalScrollMaximum(Root_ScrollViewer) - Root_ScrollViewer.Offset.X <= 0)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(2, 0);
        }
        Root_ScrollViewer.Offset = new Vector(
            Math.Clamp(Root_ScrollViewer.Offset.X + 2, 0, double.MaxValue),
            Root_ScrollViewer.Offset.Y
            );
        ReLayout();
    }
    private void Button_Click3(object? sender, RoutedEventArgs e)
    {
        if (GetVerticalScrollMaximum(Root_ScrollViewer) - Root_ScrollViewer.Offset.Y <= 0)
        {
            _workflowViewModel.Layout.PositiveOffset += new Offset(0, 2);
        }
        Root_ScrollViewer.Offset = new Vector(
            Root_ScrollViewer.Offset.X,
            Math.Clamp(Root_ScrollViewer.Offset.Y + 2, 0, double.MaxValue)
            );
        ReLayout();
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

    private void Button_Click4(object? sender, RoutedEventArgs e)
    {
        _workflowViewModel.Layout.OriginScale.X += 0.1;
        _workflowViewModel.Layout.OriginScale.Y += 0.1;
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        ReLayout();
    }

    private void Button_Click5(object? sender, RoutedEventArgs e)
    {
        _workflowViewModel.Layout.OriginScale.X -= 0.1;
        _workflowViewModel.Layout.OriginScale.Y -= 0.1;
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        ReLayout();
    }

    private void Button_Click6(object? sender, RoutedEventArgs e)
    {
        UpdateVisibleRegion();
    }

    private void Button_Click7(object? sender, RoutedEventArgs e)
    {
        GC.Collect();
    }

    private void ReLayout()
    {
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
        var layoutOffset = vm.Layout.ActualOffset;

        double visibleLeft = scrollOffset.X - layoutOffset.Left;
        double visibleTop = scrollOffset.Y - layoutOffset.Top;

        if (vm.GetHelper() is TreeHelper helper)
        {
            helper.UpdateVisibleNodes(new Viewport(
                visibleLeft,
                visibleTop,
                viewport.Width,
                viewport.Height));
            VC.Text = $"{Root_Canvas.Children.Count}";
        }
    }
}