using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Demo.Views.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
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
    private IWorkflowTreeViewModel? _tree;

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

        var viewer = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.None, // surface handles mouse-pan itself
        };
        // Attach before SetTree so the surface has a scroll viewer to compute its viewport from the
        // moment the tree is set — the first Virtualize then runs immediately (full-canvas fallback
        // until the viewer measures) instead of waiting for a ScrollChanged that may never fire.
        surface.AttachScrollViewer(viewer);
        surface.SetTree(tree);
        // The surface behavior resolves the workflow tree from DataContext (SetTree only stores _tree).
        surface.DataContext = tree;
        _tree = tree;
        // Ctrl + mouse-wheel zoom (the Core Anchor/Size getters collapse the nodes toward the origin).
        WorkflowSurfaceBehavior.SetZoomEnabled(surface, true);

        var minimap = new MinimapOverlay
        {
            WorkflowTree = tree,
            ScrollViewer = viewer,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 16, 0),
        };

        // Feed the minimap's offsets/viewport on every scroll or model change (the base's
        // WorkflowMinimapOverlay repaints and drag-pans from these values). The grid + ruler band
        // are drawn by the TreeView's own OnRender (GridDecorator), like the other GUI adapters.
        void RefreshOverlays()
        {
            minimap.ContentOffsetX = surface.OriginX;
            minimap.ContentOffsetY = surface.OriginY;
            minimap.ScrollOffsetX = viewer.HorizontalOffset;
            minimap.ScrollOffsetY = viewer.VerticalOffset;
            minimap.ViewportWidth = viewer.ViewportWidth;
            minimap.ViewportHeight = viewer.ViewportHeight;
        }

        viewer.ScrollChanged += (_, _) => RefreshOverlays();
        surface.Changed += RefreshOverlays;
        RefreshOverlays();

        var root = new Grid();
        root.Children.Add(viewer);
        root.Children.Add(minimap);

        Content = root;
    }

    /// <summary>Window-level preview key: fires for every key regardless of which child has focus.
    /// Zoom the workspace with + / - ; each node collapses toward the world origin by 1/scale
    /// (the Core Anchor/Size getters).</summary>
    protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // Ctrl + '+' zooms in, Ctrl + '-' zooms out (mirrors Ctrl + wheel; plain +/- stays unhandled
        // so it can't fire by accident). Scale is a collapse factor — higher Scale renders nodes smaller
        // (zoom out) — so zoom-in divides Scale and zoom-out multiplies it.
        if (_tree is not null && modifiers == ModifierKeys.Control)
        {
            if (key == Key.Add || key == Key.OemPlus)
            {
                ZoomBy(_tree, 1 / 1.1);
                return true;
            }

            if (key == Key.Subtract || key == Key.OemMinus)
            {
                ZoomBy(_tree, 1.1);
                return true;
            }
        }

        return base.OnPreviewWindowKeyDown(key, modifiers, isRepeat);
    }

    /// <summary>Window-level preview wheel: fires for every wheel event regardless of focus/routing.
    /// Ctrl + wheel zooms the workspace (each node collapses toward the origin by 1/scale).</summary>
    protected override bool OnPreviewWindowMouseWheel(int delta, Point position)
    {
        if (_tree is not null && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(_tree, delta > 0 ? 1 / 1.1 : 1.1);
            return true;
        }

        return base.OnPreviewWindowMouseWheel(delta, position);
    }

    private static void ZoomBy(IWorkflowTreeViewModel tree, double factor)
    {
        var next = Math.Max(0.1, Math.Min(10, tree.Layout.Scale.Horizontal * factor));
        tree.Layout.Scale = new Scale(next, next);
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
