using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Demo.Views.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
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

    /// <summary>Window-level preview key: fires for every key regardless of which child has focus.
    /// Zoom the workspace with Ctrl + / - ; the viewport center is held fixed (ViewportCenter zoom keeps
    /// the world point under the viewport center on-screen while scaling).</summary>
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
    /// Ctrl + wheel zooms the workspace while holding the viewport center fixed.</summary>
    protected override bool OnPreviewWindowMouseWheel(int delta, Point position)
    {
        if (_tree is not null && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(_tree, delta > 0 ? 1 / 1.1 : 1.1);
            return true;
        }

        return base.OnPreviewWindowMouseWheel(delta, position);
    }

    /// <summary>
    /// Zooms about the world point currently under the viewport center so that point stays on-screen
    /// (the Core <see cref="ZoomCenter.ViewportCenter"/> contract). A plain Scale change would collapse
    /// every node toward the world origin, so a node centered under the viewport would visibly drift
    /// off-center on every notch — capture the pivot, collapse about it and re-center the scroll.
    /// Scale is a collapse factor: higher Scale renders nodes smaller (zoom out), so zoom-in divides
    /// Scale and zoom-out multiplies it.
    /// </summary>
    private void ZoomBy(IWorkflowTreeViewModel tree, double factor)
    {
        var next = Math.Max(0.1, Math.Min(10, tree.Layout.Scale.Horizontal * factor));
        var layout = tree.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                _viewer.HorizontalOffset, _viewer.VerticalOffset, _viewer.ViewportWidth, _viewer.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);

            // Grow the left/top overscroll cover (ActualOffset == NegativeOffset) so negative-anchor
            // nodes stay reachable at the new collapse factor; on this default positive-anchor graph the
            // guard is a no-op. ActualOffset re-raises ActualSize/ActualOffset → the surface resizes and
            // re-virtualizes synchronously, so the UpdateLayout below adopts the grown extent.
            WorkflowSurfaceMath.EnsureNegativeCover(tree);

            // Re-layout so the ScrollViewer adopts the new extent (zoom-in auto-extends the canvas)
            // BEFORE reading ScrollableWidth/Height. Otherwise the clamp lands against the stale extent
            // and the next wheel tick re-captures the off-center pivot — the compounding drift reads
            // as zoom jitter.
            _surface.UpdateLayout();
            _viewer.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, _viewer.ViewportWidth, _viewer.ViewportHeight);
            var maxH = _viewer.ScrollableWidth;
            var maxV = _viewer.ScrollableHeight;

            // Overscroll-expand the canvas so the pivot is always reachable; a plain clamp would push
            // the pivot off-center and drift on each notch. The canvas geometry is untouched by the
            // zoom (ActualOffset == NegativeOffset, fixed) — only the scroll moves.
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                _surface.UpdateLayout();
                _viewer.UpdateLayout();
                maxH = _viewer.ScrollableWidth;
                maxV = _viewer.ScrollableHeight;
            }

            var committedX = WorkflowSurfaceMath.ClampValue(tx, 0, maxH);
            var committedY = WorkflowSurfaceMath.ClampValue(ty, 0, maxV);
            _viewer.ScrollToHorizontalOffset(committedX);
            _viewer.ScrollToVerticalOffset(committedY);

            // Re-run virtualization on the committed collapsed geometry now instead of waiting for the
            // helper's ~10 fps dirty tick (a stale viewport there would leave deep-zoom links culled for
            // ~100 ms after each zoom notch). Pass the committed scroll target — reading the viewer's
            // offset right after ScrollTo can see a not-yet-applied (pre-zoom) value.
            WorkflowSurfaceBehavior.NotifyZoomCommitted(_surface, committedX, committedY);
        }
        else
        {
            tree.Layout.Scale = new Scale(next, next);
            if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
            {
                // The cover grew the canvas; adopt the new extent so the viewer's scroll range is current.
                _surface.UpdateLayout();
                _viewer.UpdateLayout();
            }
            WorkflowSurfaceBehavior.NotifyZoomCommitted(_surface);
        }
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
