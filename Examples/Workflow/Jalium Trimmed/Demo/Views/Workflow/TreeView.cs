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
        // The menu is rebuilt on every right press. Nothing is here by default — the entries are this project's.
        // ⚠ 本平台的菜单项点完不自己收：不显式 `menu.Close()`，删掉连线后菜单还杵在画布上。
        var item = new MenuItem { Header = "Delete" };
        item.Click += (_, _) =>
        {
            menu.Close();
            link.DeleteCommand.Execute(null);
        };
        menu.Items.Add(item);
    }
}
