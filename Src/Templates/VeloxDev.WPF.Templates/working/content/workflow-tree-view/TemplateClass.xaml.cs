// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    private LinkInteraction? _linkInteraction;

    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    public TemplateClass()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachLinkInteraction();
        Loaded += (_, _) => AttachLinkInteraction();
        Unloaded += (_, _) => DetachLinkInteraction();

        if (Resources["LinkContextMenu"] is ContextMenu menu)
        {
            menu.Opened += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Opened, _menuPosition, _menuLink));
            menu.Closed += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
        }

        AttachLinkInteraction();
    }

    private void AttachLinkInteraction()
    {
        var interaction = DataContext is IWorkflowTreeViewModel tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(interaction, _linkInteraction))
        {
            return;
        }

        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
            _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
        }

        _linkInteraction = interaction;

        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested += OnContextMenuRequested;
            _linkInteraction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
        }
    }

    private void DetachLinkInteraction()
    {
        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
            _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
        }

        _linkInteraction = null;
    }

    // Shows the "LinkContextMenu" resource at the pressed link; add or remove its entries in XAML.
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // No menu on empty canvas.
        if (e.Link is null || DataContext is not IWorkflowTreeViewModel tree)
        {
            return;
        }

        if (Resources["LinkContextMenu"] is not ContextMenu menu)
        {
            return;
        }

        _menuLink = e.Link;
        _menuPosition = e.Position;
        menu.DataContext = e.Link;

        var local = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var device = PART_Canvas.PointToScreen(new Point(local.Horizontal, local.Vertical));
        var dpi = VisualTreeHelper.GetDpi(PART_Canvas);

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = device.X / dpi.DpiScaleX;
        menu.VerticalOffset = device.Y / dpi.DpiScaleY;
        menu.IsOpen = true;
    }

    // The link this menu acts on has left the tree, so the hub asks for the menu to come down — it cannot close
    // the host's popup itself. Closing reports Closed, which releases the suspended hover as usual.
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link))
        {
            return;
        }

        if (Resources["LinkContextMenu"] is ContextMenu menu)
        {
            menu.IsOpen = false;
        }
    }
}
