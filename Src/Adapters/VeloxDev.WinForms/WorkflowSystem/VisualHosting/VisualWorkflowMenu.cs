using System.Drawing;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using DrawingPointF = System.Drawing.PointF;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The context menu the visual surface opens on a link (or a node), built from the visual backend's
/// <see cref="VisualContextMenu"/> rows.
/// </summary>
/// <remarks>
/// The menu itself carries no policy: the surface creates one with <see cref="OpenForLink"/> or
/// <see cref="OpenForNode"/> and the host's builder fills the rows. When no builder is supplied, a single
/// <c>Delete</c> row executes <see cref="ILinkComponent.DeleteCommand"/>.
/// </remarks>
public sealed class VisualWorkflowMenu : VisualContextMenu
{
    /// <summary>Creates an empty menu.</summary>
    public VisualWorkflowMenu()
    {
    }

    /// <summary>Adds a command row and returns it.</summary>
    /// <param name="header">The row text.</param>
    /// <param name="command">The action run when the row is invoked, or <see langword="null"/>.</param>
    /// <param name="isEnabled">Whether the row can be invoked.</param>
    /// <param name="isChecked">Whether a check mark is drawn before the header.</param>
    /// <param name="gesture">The shortcut text drawn at the right edge, or <see langword="null"/>.</param>
    public VisualMenuItem AddItem(
        string header,
        Action? command = null,
        bool isEnabled = true,
        bool isChecked = false,
        string? gesture = null)
    {
        var item = new VisualMenuItem
        {
            Header = header,
            Command = command,
            IsEnabled = isEnabled,
            IsChecked = isChecked,
            InputGestureText = gesture,
        };
        Items.Add(item);
        return item;
    }

    /// <summary>Adds a separator row.</summary>
    public void AddSeparator() => Items.Add(new VisualMenuItem { IsSeparator = true });

    /// <summary>Opens this menu for a link at a surface point, filling it with the host's builder.</summary>
    /// <param name="target">The element the surface point is measured against.</param>
    /// <param name="surfacePoint">The point in surface coordinates.</param>
    /// <param name="link">The link the menu acts on.</param>
    /// <param name="builder">The host's row builder, or <see langword="null"/> for the default Delete row.</param>
    public void OpenForLink(
        VisualElement target,
        DrawingPointF surfacePoint,
        ILinkComponent link,
        Action<VisualWorkflowMenu, ILinkComponent>? builder)
    {
        if (builder is null)
        {
            AddItem("Delete", () =>
            {
                if (link.DeleteCommand.CanExecute(null))
                {
                    link.DeleteCommand.Execute(null);
                }
            });
        }
        else
        {
            builder(this, link);
        }

        Refresh();
        OpenAt(target, surfacePoint);
    }

    /// <summary>Opens this menu for a node at a surface point, filling it with the host's builder.</summary>
    /// <param name="target">The element the surface point is measured against.</param>
    /// <param name="surfacePoint">The point in surface coordinates.</param>
    /// <param name="node">The node the menu acts on.</param>
    /// <param name="builder">The host's row builder.</param>
    /// <returns><see langword="false"/> when the builder added nothing, in which case the menu is not opened.</returns>
    public bool OpenForNode(
        VisualElement target,
        DrawingPointF surfacePoint,
        INodeComponent node,
        Action<VisualWorkflowMenu, INodeComponent>? builder)
    {
        builder?.Invoke(this, node);
        if (Items.Count == 0)
        {
            return false;
        }

        Refresh();
        OpenAt(target, surfacePoint);
        return true;
    }
}
