using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class TreeView : UserControl
{
    // The menu's items live in the XAML resource below — add or remove them there.
    private readonly ContextMenu? _linkMenu;

    // 当前这棵树的中枢：每棵树一个（LinkInteraction.For），换树时改订、离屏时退订。
    private LinkInteraction? _linkInteraction;

    // 菜单会复用：开合那一刻再读它针对的连线与画布坐标，不能提前绑定。
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.ScrollChanged += (_, _) => InfoOverlay.Refresh();

        _linkMenu = this.TryFindResource("WorkflowTreeMenu", out var resource) ? resource as ContextMenu : null;
        if (_linkMenu is not null)
        {
            // 菜单的开合报回 hub：它据此收放 IsSuspended，宿主不必自己记账。
            _linkMenu.Opened += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Opened, _menuPosition, _menuLink));
            _linkMenu.Closed += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
        }

        // 中枢跟着 DataContext 换；离屏退订，再上屏重新挂上。
        DataContextChanged += (_, _) => WireLinkInteraction();
        AttachedToVisualTree += (_, _) => WireLinkInteraction();
        DetachedFromVisualTree += (_, _) => UnwireLinkInteraction();
        WireLinkInteraction();
    }

    // For(tree) 拿到那棵树唯一的中枢，适配器转发进去的是同一个。
    private void WireLinkInteraction()
    {
        var interaction = DataContext is IWorkflowTreeViewModel tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(interaction, _linkInteraction)) return;

        UnwireLinkInteraction();
        _linkInteraction = interaction;
        if (interaction is null) return;

        interaction.ContextMenuRequested += OnContextMenuRequested;
    }

    private void UnwireLinkInteraction()
    {
        if (_linkInteraction is null) return;

        _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
        _linkInteraction = null;
    }

    // 右键落在表面上，而弹出要屏幕坐标；只有表面同时知道画布与屏幕两件事。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        if (e.Link is null || _linkMenu is null) return;
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        if (this.FindControl<Canvas>("PART_Canvas") is not { } canvas) return;

        // 画布坐标 → 屏幕上的一点：先按适配器那套逆变换（world + ActualOffset）回到画布局部，
        // 再由画布换到本控件（宿主）的坐标 —— 菜单的 PlacementRect 正是相对 PlacementTarget 的局部坐标。
        var screen = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var point = canvas.TranslatePoint(new Point(screen.Horizontal, screen.Vertical), this)
                    ?? new Point(screen.Horizontal, screen.Vertical);

        _menuLink = e.Link;
        _menuPosition = e.Position;

        _linkMenu.Placement = PlacementMode.AnchorAndGravity;
        _linkMenu.PlacementAnchor = PopupAnchor.TopLeft;
        _linkMenu.PlacementGravity = PopupGravity.BottomRight;
        _linkMenu.PlacementRect = new Rect(point.X, point.Y, 0, 0);
        _linkMenu.Open(this);
    }

    private void OnDeleteLinkClick(object? sender, RoutedEventArgs e)
    {
        if (_menuLink is { } link && link.DeleteCommand.CanExecute(null))
            link.DeleteCommand.Execute(null);
    }
}
