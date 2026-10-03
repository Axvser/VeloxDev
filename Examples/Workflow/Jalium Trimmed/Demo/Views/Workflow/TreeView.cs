// VeloxDev customization: The node-editor surface. The adapter's WorkflowTreeView owns the pooling, the viewport
// bookkeeping, the gestures (pan, node drag, connection) and the rendering; this file says how it looks and wires
// the sibling items it composes. Rename SlotView / GridDecorator / TemplateSelector below if you renamed those.
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface: the adapter's <see cref="WorkflowTreeView"/> with this project's palette, port layout,
/// grid and view selector.
/// </summary>
public sealed class TreeView : WorkflowTreeView
{
    public TreeView()
    {
        SurfaceBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);
        ConnectingLinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
        PortLayout = SlotView.Layout;
        GridDecorator = new GridDecorator();
        TemplateSelector = Demo.Views.Workflow.TemplateSelector.CreateSelector();
    }

    /// <inheritdoc />
    protected override void OnBuildLinkMenu(ContextMenu menu, IWorkflowLinkViewModel link)
    {
        // The menu is rebuilt on every right press: add or remove entries here. The base adds "Delete".
        base.OnBuildLinkMenu(menu, link);
    }
}
