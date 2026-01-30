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
using System.Collections.Generic;
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
        }
    }

    private async void SimulateData(object? sender, RoutedEventArgs e)
    {
        const int totalNodes = 1_000_000; // 总量
        const int batchSize = 10_000; // 每批处理数量
        const double gridSize = 150; // 标准网格大小
        const double jitter = 30; 
        double canvasSize = gridSize * Math.Sqrt(totalNodes); // ≈150,000

        var random = new Random(12345); // 固定种子（注意：多线程下需谨慎，但此处单线程使用）
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

        int gridCount = (int)Math.Ceiling(Math.Sqrt(totalNodes));
        int generated = 0;
        int i = 0, j = 0;

        // 获取 ViewModel 和 Helper（必须在 UI 线程获取引用）
        var workflowViewModel = _workflowViewModel;
        var workflowHelper = workflowViewModel.GetHelper();

        // 先设置画布大小等基础属性（UI 线程）
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            workflowViewModel.Layout.OriginSize = new Size(150000, 150000);
            workflowViewModel.Layout.OriginAlign = OriginAligns.TopLeft;
            DataContext = workflowViewModel;
        });

        // 后台生成节点
        await Task.Run(async () =>
        {
            while (generated < totalNodes)
            {
                var batchGenerated = 0;
                var nodesInBatch = new List<NodeViewModel>();

                // 构建一批节点（纯数据，不涉及 UI）
                while (batchGenerated < batchSize && generated < totalNodes)
                {
                    if (j >= gridCount)
                    {
                        j = 0;
                        i++;
                    }
                    if (i >= gridCount) break;

                    double baseX = i * gridSize;
                    double baseY = j * gridSize;

                    double x = baseX + jitter - random.NextDouble() * jitter * 2;
                    double y = baseY + jitter - random.NextDouble() * jitter * 2;

                    x = Math.Max(0, Math.Min(x, canvasSize - 1));
                    y = Math.Max(0, Math.Min(y, canvasSize - 1));

                    var node = new NodeViewModel()
                    {
                        Size = new Size(
                            width: 80 + random.Next(0, 80),
                            height: 60 + random.Next(0, 60)
                        ),
                        Anchor = new Anchor(x, y, 0)
                    };

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

                    nodesInBatch.Add(node);
                    generated++;
                    batchGenerated++;
                    j++;
                }

                // 将这批节点添加到 ViewModel（必须回到 UI 线程）
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var node in nodesInBatch)
                    {
                        workflowHelper.CreateNode(node);
                    }
                    workflowHelper.ClearHistory(); // 清理历史
                });

                // 显示进度通知（每批一次）
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _manager.Show(new Notification("Progress", $"Generated {generated:N0} / {totalNodes:N0} nodes"));
                });

                // 小延迟，让 UI 有机会响应（可选，但提升流畅度）
                await Task.Delay(50); // 50ms 间隔
            }

            // 最终更新布局
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                workflowViewModel.Layout.UpdateCommand.Execute(null);
                ReLayout();
                _manager.Show(new Notification("OK", $"Completed! Generated {generated:N0} nodes in {canvasSize:N0}×{canvasSize:N0} space"));
            });
        });
    }
}