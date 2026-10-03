// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    // The menu's items live in the XAML resource below — add or remove them there.
    private readonly ContextMenu? _linkMenu;

    private LinkInteraction? _linkInteraction;

    // The link the open menu acts on; the menu is reused, so it is reassigned just before each open.
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    public TemplateClass()
    {
        InitializeComponent();

        _linkMenu = this.TryFindResource("WorkflowTreeMenu", out var resource) ? resource as ContextMenu : null;
        if (_linkMenu is not null)
        {
            _linkMenu.Opened += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Opened, _menuPosition, _menuLink));
            _linkMenu.Closed += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
        }

        DataContextChanged += (_, _) => WireLinkInteraction();
        AttachedToVisualTree += (_, _) => WireLinkInteraction();
        DetachedFromVisualTree += (_, _) => UnwireLinkInteraction();
        WireLinkInteraction();
    }

    private void WireLinkInteraction()
    {
        var interaction = DataContext is IWorkflowTreeViewModel tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(interaction, _linkInteraction)) return;

        UnwireLinkInteraction();
        _linkInteraction = interaction;
        if (interaction is null) return;

        interaction.ContextMenuRequested += OnContextMenuRequested;
        interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
    }

    private void UnwireLinkInteraction()
    {
        if (_linkInteraction is null) return;

        _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
        _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
        _linkInteraction = null;
    }

    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is null || _linkMenu is null) return;
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        if (this.FindControl<Canvas>("PART_Canvas") is not { } canvas) return;

        var screen = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var point = canvas.TranslatePoint(new Point(screen.Horizontal, screen.Vertical), this)
                    ?? new Point(screen.Horizontal, screen.Vertical);

        _menuLink = e.Link;
        _menuPosition = e.Position;

        // The menu's DataContext is the link, so its items bind their commands straight to it.
        _linkMenu.DataContext = e.Link;
        _linkMenu.Placement = PlacementMode.AnchorAndGravity;
        _linkMenu.PlacementAnchor = PopupAnchor.TopLeft;
        _linkMenu.PlacementGravity = PopupGravity.BottomRight;
        _linkMenu.PlacementRect = new Rect(point.X, point.Y, 0, 0);
        _linkMenu.Open(this);
    }

    // The link this menu acts on has left the tree, so the hub asks for the menu to come down — it cannot
    // close the host's popup itself. Closing reports Closed, which releases the suspended hover as usual.
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _linkMenu?.Close();
    }
}
