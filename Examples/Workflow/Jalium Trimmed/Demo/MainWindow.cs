using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Demo.Views.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
// Jalium also ships a TreeView control, so alias the generated workflow surface.
using WorkflowTreeView = Demo.Views.Workflow.TreeView;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo;

/// <summary>Composed like the other GUI adapters' Trimmed demos: a dark surface (the
/// workflow-tree-view template's TreeView, which draws its grid + ruler band internally) in a
/// ScrollViewer, plus a content-fit minimap (the minimap-overlay template's MinimapOverlay,
/// subclassing the adapter's base).</summary>
internal sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "VeloxDev Workflow - Jalium Trimmed";
        Width = 1100;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var tree = new TreeViewModel();
        LoadTree(tree);

        var surface = new WorkflowTreeView
        {
            TemplateSelector = TemplateSelector.CreateSelector(),
        };
        surface.SetTree(tree);

        var viewer = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.None, // surface handles mouse-pan itself
        };
        surface.AttachScrollViewer(viewer);

        var minimap = new MinimapOverlay
        {
            WorkflowTree = tree,
            ScrollViewer = viewer,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 16, 0),
        };

        // Feed the minimap's offsets/viewport on every scroll or model change (the base's
        // WorkflowMinimapOverlay repaints and drag-pans from these values). Under zoom the minimap's
        // ScrollOffset/Viewport are in WORLD units (scroll ÷ scale), so the viewport rect tracks zoom.
        // The grid + ruler band are drawn by the TreeView's own OnRender (GridDecorator).
        void RefreshOverlays()
        {
            minimap.ContentOffsetX = surface.OriginX;
            minimap.ContentOffsetY = surface.OriginY;
            minimap.ScrollOffsetX = viewer.HorizontalOffset / surface.Scale;
            minimap.ScrollOffsetY = viewer.VerticalOffset / surface.Scale;
            minimap.ViewportWidth = viewer.ViewportWidth / surface.Scale;
            minimap.ViewportHeight = viewer.ViewportHeight / surface.Scale;
        }

        viewer.ScrollChanged += (_, _) => RefreshOverlays();
        surface.Changed += RefreshOverlays;
        RefreshOverlays();

        var root = new Grid();
        root.Children.Add(viewer);
        root.Children.Add(minimap);

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
