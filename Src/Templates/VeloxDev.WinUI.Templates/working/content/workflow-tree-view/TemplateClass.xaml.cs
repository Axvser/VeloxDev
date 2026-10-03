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

        // 连线交互中枢由 Core 按树缓存（LinkInteraction.For）：树换了、或表面离开可视树时重订一次。
        Loaded += (_, _) => AttachLinkInteraction();
        Unloaded += (_, _) => DetachLinkInteraction();
        DataContextChanged += (_, _) => AttachLinkInteraction();
    }

    private LinkInteraction? _linkInteraction;
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    // 中枢是一棵树一个的共享实例，引用没变就不动，避免重复订阅。
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
        }
    }

    private void DetachLinkInteraction()
    {
        if (_linkInteraction is not null)
        {
            _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
            _linkInteraction = null;
        }
    }

    // 右键落在表面上：只有表面同时知道被按到的那条连线（画布坐标）与它自己在屏幕上的位置。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // 空白画布不弹菜单；被别的订阅者否决的那一次也不弹。
        if (e.Link is not { } link || e.Handle.PreventDefault || _linkInteraction is null)
        {
            return;
        }

        _menuLink = link;
        _menuPosition = e.Position;

        // 画布坐标 → 表面坐标：把画布那一段渲染变换反过来用，滚动与缩放因此自动跟上。
        var point = PART_Canvas.TransformToVisual(PART_SurfaceBorder)
            .TransformPoint(new Windows.Foundation.Point(e.Position.Horizontal, e.Position.Vertical));

        var menu = (MenuFlyout)Resources["LinkContextMenu"];
        menu.XamlRoot = XamlRoot;
        _linkInteraction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, link));
        menu.ShowAt(PART_SurfaceBorder, new FlyoutShowOptions { Position = point });
    }

    private void OnDeleteLink(object sender, RoutedEventArgs e)
    {
        if (_menuLink is { } link && link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }
    }

    // 收起时报回中枢，挂起状态由它自己放开 —— 表面不用记账。
    private void OnLinkMenuClosed(object? sender, object e)
    {
        _linkInteraction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
    }
}
