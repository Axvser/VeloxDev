// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public sealed partial class TemplateClass : UserControl
{
    public TemplateClass()
    {
        InitializeComponent();

        ((MenuFlyout)Resources["LinkContextMenu"]).Closed += OnLinkMenuClosed;

        // Re-subscribe whenever the tree changes or the surface enters/leaves the visual tree.
        Loaded += (_, _) => AttachLinkInteraction();
        Unloaded += (_, _) => DetachLinkInteraction();
        DataContextChanged += (_, _) => AttachLinkInteraction();
    }

    private LinkInteraction? _linkInteraction;
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    // One hub per tree; re-subscribe only when the instance changed.
    private void AttachLinkInteraction()
    {
        var tree = DataContext as IWorkflowTreeViewModel;
        var interaction = tree is null ? null : LinkInteraction.For(tree);
        if (ReferenceEquals(_linkInteraction, interaction))
        {
            return;
        }

        DetachLinkInteraction();
        _linkInteraction = interaction;
        if (interaction is not null)
        {
            interaction.ContextMenuRequested += OnContextMenuRequested;
            interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
        }
    }

    private void DetachLinkInteraction()
    {
        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
            _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
            _linkInteraction = null;
        }
    }

    // Only the surface knows both the link under the pointer and its own position.
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is not { } link || _linkInteraction is null)
        {
            return;
        }

        _menuLink = link;
        _menuPosition = e.Position;

        // Canvas coordinates -> surface coordinates, so scrolling and zooming are accounted for.
        var point = PART_Canvas.TransformToVisual(PART_SurfaceBorder)
            .TransformPoint(new Windows.Foundation.Point(e.Position.Horizontal, e.Position.Vertical));

        var menu = (MenuFlyout)Resources["LinkContextMenu"];
        menu.XamlRoot = XamlRoot;

        // A resource flyout sits outside the visual tree, so give its items the link explicitly.
        foreach (var item in menu.Items)
        {
            if (item is FrameworkElement element)
            {
                element.DataContext = link;
            }
        }

        _linkInteraction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, link));
        menu.ShowAt(PART_SurfaceBorder, new FlyoutShowOptions { Position = point });
    }

    // The link the open menu was about has left the tree: the hub asks the host to close its own popup.
    // Closing it reports Closed as usual, which releases the suspension.
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        if (Resources["LinkContextMenu"] is MenuFlyout menu) menu.Hide();
    }

    // Report the close so the hub releases the suspended hover.
    private void OnLinkMenuClosed(object? sender, object e)
    {
        _linkInteraction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
    }
}
