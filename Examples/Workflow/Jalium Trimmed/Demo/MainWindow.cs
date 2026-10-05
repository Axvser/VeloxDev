using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Demo.Views.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using System;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;
// Jalium also ships a TreeView control, so alias the generated workflow surface.
using WorkflowTreeView = Demo.Views.Workflow.TreeView;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo;

/// <summary>Composed like the other GUI adapters' Trimmed demos: the tree view template's surface (which declares
/// its grid decorator, scroll viewer, canvas and minimap in markup) plus a demo-only info HUD.</summary>
internal sealed class MainWindow : Window
{
    private IWorkflowTreeViewModel? _tree;
    private readonly WorkflowTreeView _surface;
    private readonly ScrollViewer _viewer;

    public MainWindow()
    {
        Title = "VeloxDev Workflow - Jalium Trimmed";
        Width = 1100;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var tree = new TreeViewModel();
        LoadTree(tree);

        // 表面是一个 UserControl：模板声明自己的部件，WorkflowSurfaceBehavior 按名字解析并驱动它们。
        // 树经 DataContext 交进去 —— 与其余六家同一条契约。
        _surface = new WorkflowTreeView
        {
            DataContext = tree,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _viewer = (ScrollViewer)_surface.FindName("PART_ScrollViewer")!;
        _tree = tree;

        // Realtime canvas-info decorator layer (floating text HUD): anchored bottom-left, hit-test
        // transparent. It subscribes to the Core model itself and is fed the same offsets as the minimap,
        // so the read-out stays live while panning / zooming / dragging beneath it.
        var info = new InfoOverlay
        {
            WorkflowTree = tree,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 18),
        };

        // Feed the info HUD on every scroll or model change. The minimap is fed by WorkflowSurfaceBehavior.
        void RefreshOverlays()
        {
            info.ContentOffsetX = tree.Layout.ActualOffset.Horizontal;
            info.ContentOffsetY = tree.Layout.ActualOffset.Vertical;
            info.ScrollOffsetX = _viewer.HorizontalOffset;
            info.ScrollOffsetY = _viewer.VerticalOffset;
            info.ViewportWidth = _viewer.ViewportWidth;
            info.ViewportHeight = _viewer.ViewportHeight;
        }

        // SizeChanged catches the viewer's first measure (Jalium may not fire ScrollChanged on the
        // initial layout, which is why the surface falls back to the whole canvas until it is measured).
        _viewer.ScrollChanged += (_, _) => RefreshOverlays();
        _viewer.SizeChanged += (_, _) => RefreshOverlays();
        if (tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged += (_, _) => RefreshOverlays();
        }
        RefreshOverlays();

        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(info);

        Content = root;
    }

    private static void LoadTree(TreeViewModel tree)
    {
        var size = new Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Boolean routes", Size = size, Anchor = new Anchor { Horizontal = 80, Vertical = 80 } },
            new NodeViewModel { Name = "Voltage routes", Size = size, Anchor = new Anchor { Horizontal = 400, Vertical = 220 } },
            new NodeViewModel { Name = "Model routes", Size = size, Anchor = new Anchor { Horizontal = 720, Vertical = 80 } },
        };
        foreach (var node in nodes)
        {
            tree.CreateNodeCommand.Execute(node);
        }

        nodes[0].OutputSlots.SetSelector(typeof(bool));
        nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
        nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
        nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
    }
}
