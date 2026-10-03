using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public partial class TreeView : UserControl
{
    private LinkInteraction? _linkInteraction;

    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    public TreeView()
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
        }

        _linkInteraction = interaction;

        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested += OnContextMenuRequested;
        }
    }

    private void DetachLinkInteraction()
    {
        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
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
}
