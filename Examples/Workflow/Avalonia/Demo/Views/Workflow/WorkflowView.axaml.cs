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
using System.Diagnostics;
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
        var random = new Random();
        var workflowHelper = _workflowViewModel.GetHelper();

        // 定义一些可能的配置
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
        new Size(30, 30),
        new Size(20, 25),
        new Size(25, 20)
    };

        // 生成1000个节点
        for (int i = 0; i < 1000; i++)
        {
            // 随机生成节点
            var node = new NodeViewModel()
            {
                Size = new Size(random.Next(100, 401), random.Next(80, 301)),
                Anchor = new Anchor(
                    random.Next(0, 1920),  // 假设画布宽度1920
                    random.Next(0, 1080),  // 假设画布高度1080
                    random.Next(0, 5)     // 层级
                )
            };

            // 将节点添加到工作流
            workflowHelper.CreateNode(node);

            // 为每个节点随机生成1-4个插槽
            int slotCount = random.Next(1, 5);
            for (int j = 0; j < slotCount; j++)
            {
                var slot = new SlotViewModel()
                {
                    // 在节点内部随机位置
                    Offset = new Offset(
                        random.Next(0, (int)node.Size.Width - 30),  // 确保插槽不超出节点边界
                        random.Next(0, (int)node.Size.Height - 30)
                    ),
                    Size = slotSizes[random.Next(0, slotSizes.Length)],
                    Channel = slotTypes[random.Next(0, slotTypes.Length)]
                };

                node.GetHelper().CreateSlot(slot);
            }

            // 每生成100个节点清理一次历史，避免内存占用过高
            if (i % 100 == 0)
            {
                _workflowViewModel.GetHelper().ClearHistory();
            }
        }

        // 清理最终历史栈
        _workflowViewModel.GetHelper().ClearHistory();

        // 使用数据上下文
        DataContext = _workflowViewModel;

        // 配置布局
        _workflowViewModel.Layout.OriginAlign = OriginAligns.TopLeft;

        // 刷新逻辑布局
        _workflowViewModel.Layout.UpdateCommand.Execute(null);

        // 刷新UI布局
        ReLayout();
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
            var sw1 = Stopwatch.StartNew();
            var list = helper._spatialHashMap.Query(visibleLeft, visibleTop, viewport.Width, viewport.Height);
            sw1.Stop();
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                VMTime.Text = $"{sw1.ElapsedTicks * 1000000L / Stopwatch.Frequency} μs";
            });

            var sw2 = Stopwatch.StartNew();
            _workflowViewModel.VisibleNodes = [.. list];
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                sw2.Stop();
                VTime.Text = $"{sw2.ElapsedTicks * 1000000L / Stopwatch.Frequency} μs";
            });
        }
    }
}