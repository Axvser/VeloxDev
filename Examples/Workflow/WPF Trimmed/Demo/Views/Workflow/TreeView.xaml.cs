using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public partial class TreeView : UserControl
{
    // 当前这棵树的中枢：每棵树一个（见 LinkInteraction.For），DataContext 换树时改订，离屏时退订。
    private LinkInteraction? _linkInteraction;

    // 本次菜单对应的连线与画布坐标：条目由 XAML 声明，动作靠菜单的 DataContext 拿到这条连线。
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    public TreeView()
    {
        InitializeComponent();

        // 中枢跟着 DataContext 换；离屏退订，再上屏重新挂上 —— 否则控件离屏后仍从旧树收事件。
        DataContextChanged += (_, _) => AttachLinkInteraction();
        Loaded += (_, _) => AttachLinkInteraction();
        Unloaded += (_, _) => DetachLinkInteraction();

        // 菜单的开合报回 hub：它据此收放 IsSuspended，宿主不必自己记账。
        if (Resources["LinkContextMenu"] is ContextMenu menu)
        {
            menu.Opened += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Opened, _menuPosition, _menuLink));
            menu.Closed += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
        }

        AttachLinkInteraction();
    }

    // For(tree) 拿到那棵树唯一的中枢，适配器转发进去的是同一个。
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

    // 右键落在表面上，而弹出要屏幕坐标；只有表面同时知道画布与屏幕两件事，所以菜单由表面弹。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
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

        // 画布坐标 → 设备坐标：先按适配器那套逆变换（world + ActualOffset）回到画布局部，再由画布
        // 换到屏幕；AbsolutePoint 用 DIP，所以最后按 DPI 折回去。
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
