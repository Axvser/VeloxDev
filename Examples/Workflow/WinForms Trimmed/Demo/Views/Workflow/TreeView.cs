// VeloxDev customization: The workflow surface. The adapter's WorkflowTreeView already assembles the
// chrome, grid, floating rulers, scrolled canvas, view pool and pan engine; this file supplies only the
// palette, the two view factories, and the demo's HUD. Wire a tree through the ViewModel property.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface: the adapter's <see cref="WorkflowTreeView"/> with this demo's palette, its
/// generated node and link views, and the canvas-info HUD.
/// </summary>
public sealed class TreeView : WorkflowTreeView
{
    private readonly InfoOverlay _infoOverlay = new();

    public TreeView()
    {
        SurfaceBackground = ParseColor("#1E1E1E");
        SurfaceBorderBrush = ParseColor("#33FFFFFF");
        SurfaceBorderThickness = 1;
        SurfaceCornerRadius = 3;

        // Realtime canvas-info HUD: floating bottom-left, above the scroll viewer.
        Controls.Add(_infoOverlay);
        _infoOverlay.BringToFront();
    }

    /// <inheritdoc />
    protected override Control CreateNodeView(IWorkflowNodeViewModel node)
    {
        var view = new NodeView { ViewModel = node };
        // The minimap reads node anchors directly; dragging a node changes the anchor without panning,
        // so repaint it as the node moves.
        view.AnchorChanged += () =>
        {
            if (!IsDisposed && PART_MinimapOverlay is not null)
            {
                PART_MinimapOverlay.Invalidate();
            }
        };
        return view;
    }

    /// <inheritdoc />
    protected override Control CreateLinkView(IWorkflowLinkViewModel link)
        => new LinkView { ViewModel = link };

    /// <inheritdoc />
    protected override void OnBuildLinkMenu(ContextMenuStrip menu, IWorkflowLinkViewModel link)
    {
        // The menu is rebuilt on every right press: add or remove entries here. The base adds "Delete".
        base.OnBuildLinkMenu(menu, link);
    }

    /// <inheritdoc />
    protected override void OnTreeAttached(IWorkflowTreeViewModel? tree) => _infoOverlay.Bind(tree);

    /// <inheritdoc />
    protected override void OnSurfaceRefreshed() => _infoOverlay.UpdateText();
}
