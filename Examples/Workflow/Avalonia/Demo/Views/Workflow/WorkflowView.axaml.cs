using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Demo.ViewModels;
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
            (point.X - _workflowViewModel.NegativeOffset.Left)*_workflowViewModel.Scale,
            (point.Y - _workflowViewModel.NegativeOffset.Top)*_workflowViewModel.Scale,
            0));
    }

    private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        _workflowViewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private void SimulateData(object? sender, RoutedEventArgs e)
    {
        var slot1 = new SlotViewModel()
        {
            Offset = new Offset(170, 60),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneBoth,
        };
        var slot2 = new SlotViewModel()
        {
            Offset = new Offset(170, 120),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleTargets,
        };
        var slot3 = new SlotViewModel()
        {
            Offset = new Offset(10, 100),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneBoth,
        };
        var slot4 = new SlotViewModel()
        {
            Offset = new Offset(10, 200),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleSources,
        };
        var node1 = new NodeViewModel()
        {
            Size = new Size(200, 200),
            Anchor = new Anchor(50, 50, 1)
        };
        var node2 = new NodeViewModel()
        {
            Size = new Size(300, 300),
            Anchor = new Anchor(250, 250, 1)
        };

        // 控制器节点，仅用于启动、终结
        var node3 = new ControllerViewModel()
        {
            Size = new Size(400, 200),
            Anchor = new Anchor(400, 400, 1)
        };
        var slot5 = new SlotViewModel()
        {
            Offset = new Offset(335, 55),
            Size = new Size(30, 30),
            Channel = SlotChannel.MultipleTargets,
        };

        // 给 Tree 挂载 Node
        _workflowViewModel.GetHelper().CreateNode(node1);
        _workflowViewModel.GetHelper().CreateNode(node2);
        _workflowViewModel.GetHelper().CreateNode(node3);

        // 给 Node 挂载 Slot
        node1.GetHelper().CreateSlot(slot1);
        node1.GetHelper().CreateSlot(slot2);
        node2.GetHelper().CreateSlot(slot3);
        node2.GetHelper().CreateSlot(slot4);
        node3.GetHelper().CreateSlot(slot5);

        // 清理历史栈，避免非法的重做与撤销
        _workflowViewModel.GetHelper().ClearHistory();

        // 使用数据上下文
        DataContext = _workflowViewModel;
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (Root_ScrollViewer.Offset.X <= 0)
        {
            _workflowViewModel.NegativeOffset += new Offset(2, 0);
            CanvasTransform = new TransformGroup()
            {
                Children = [
                    new TranslateTransform(
                        _workflowViewModel.NegativeOffset.Left,
                        _workflowViewModel.NegativeOffset.Top
                        )
                    ]
            };
        }
        else
        {
            Root_ScrollViewer.Offset = new Vector(
                Math.Clamp(Root_ScrollViewer.Offset.X - 2, 0, double.MaxValue),
                Root_ScrollViewer.Offset.Y
                );
        }
    }
    private void Button_Click1(object? sender, RoutedEventArgs e)
    {
        if (Root_ScrollViewer.Offset.Y <= 0)
        {
            _workflowViewModel.NegativeOffset += new Offset(0, 2);
            CanvasTransform = new TransformGroup()
            {
                Children = [
                    new TranslateTransform(
                        _workflowViewModel.NegativeOffset.Left,
                        _workflowViewModel.NegativeOffset.Top
                        )
                    ]
            };
        }
        else
        {
            Root_ScrollViewer.Offset = new Vector(
                Root_ScrollViewer.Offset.X,
                Math.Clamp(Root_ScrollViewer.Offset.Y - 2, 0, double.MaxValue)
                );
        }

    }
    private void Button_Click2(object? sender, RoutedEventArgs e)
    {
        if (GetHorizontalScrollMaximum(Root_ScrollViewer) - Root_ScrollViewer.Offset.X <= 0)
        {
            _workflowViewModel.PositiveOffset += new Offset(2, 0);
            CanvasTransform = new TransformGroup()
            {
                Children = [
                    new TranslateTransform(
                        _workflowViewModel.NegativeOffset.Left,
                        _workflowViewModel.NegativeOffset.Top
                        )
                    ]
            };
            Root_ScrollViewer.Offset = new Vector(
                Math.Clamp(Root_ScrollViewer.Offset.X + 2, 0, double.MaxValue),
                Root_ScrollViewer.Offset.Y
                );
        }
        else
        {
            Root_ScrollViewer.Offset = new Vector(
                Math.Clamp(Root_ScrollViewer.Offset.X + 2, 0, double.MaxValue),
                Root_ScrollViewer.Offset.Y
                );
        }
    }
    private void Button_Click3(object? sender, RoutedEventArgs e)
    {
        if (GetVerticalScrollMaximum(Root_ScrollViewer)-Root_ScrollViewer.Offset.Y <= 0)
        {
            _workflowViewModel.PositiveOffset += new Offset(0, 2);
            CanvasTransform = new TransformGroup()
            {
                Children = [
                    new TranslateTransform(
                        _workflowViewModel.NegativeOffset.Left,
                        _workflowViewModel.NegativeOffset.Top
                        )
                    ]
            };
            Root_ScrollViewer.Offset = new Vector(
                Root_ScrollViewer.Offset.X,
                Math.Clamp(Root_ScrollViewer.Offset.Y + 2, 0, double.MaxValue)
                );
        }
        else
        {
            Root_ScrollViewer.Offset = new Vector(
                Root_ScrollViewer.Offset.X,
                Math.Clamp(Root_ScrollViewer.Offset.Y + 2, 0, double.MaxValue)
                );
        }
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
        _workflowViewModel.Scale += 0.1;
        GlobalScale = new TransformGroup()
        {
            Children = [
                new ScaleTransform(_workflowViewModel.Scale, _workflowViewModel.Scale)
                ]
        };
    }

    private void Button_Click5(object? sender, RoutedEventArgs e)
    {
        _workflowViewModel.Scale -= 0.1;

        GlobalScale = new TransformGroup()
        {
            Children = [
                new ScaleTransform(_workflowViewModel.Scale, _workflowViewModel.Scale)
                ]
        };
    }
}