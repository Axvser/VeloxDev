// VeloxDev customization: The workflow surface. The adapter's WorkflowTreeView already assembles the chrome,
// grid, floating rulers, scrolled canvas, view pool and pan engine; this file supplies only the palette and the
// two view factories. Rename NodeView / LinkView below if you renamed those items.
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The workflow surface: the adapter's <see cref="WorkflowTreeView"/> with this project's palette and the
/// generated node and link views.
/// </summary>
public sealed class TemplateClass : WorkflowTreeView
{
    public TemplateClass()
    {
        SurfaceBackground = ParseColor("TemplateSurfaceBackground");
        SurfaceBorderBrush = ParseColor("TemplateSurfaceBorderBrush");
        SurfaceBorderThickness = int.Parse("TemplateSurfaceBorderThickness", CultureInfo.InvariantCulture);
        SurfaceCornerRadius = int.Parse("TemplateSurfaceCornerRadius", CultureInfo.InvariantCulture);
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
        => new LinkView();

    /// <inheritdoc />
    protected override void OnBuildLinkMenu(ContextMenuStrip menu, IWorkflowLinkViewModel link)
    {
        // The menu is rebuilt on every right press: add or remove entries here. The base adds "Delete".
        base.OnBuildLinkMenu(menu, link);
    }
}
