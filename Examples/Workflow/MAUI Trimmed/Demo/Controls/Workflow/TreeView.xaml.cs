// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class TreeView : ContentView
{
    // 右键菜单挂在树的 hub 上；换树/摘树时先解绑，旧 hub 才不会继续握着这个控件的委托。
    private LinkInteraction? _interaction;

    // 非 Windows 那份弹出层当前作用的那条连线；没有弹层时为 null。
    private IWorkflowLinkViewModel? _menuLink;

    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();

        // The tree is assigned to this control by the page; propagate it explicitly so the HUD's
        // BindingContextChanged fires even if inheritance doesn't reach the nested overlay.
        BindingContextChanged += (_, _) =>
        {
            InfoOverlay.BindingContext = BindingContext;
            UpdateItemsSource();
            UpdateInteraction();
        };
    }

    /// <summary>
    /// View-model collection fed to the canvas <c>ViewPool</c>: the helper's visible set as-is, so one
    /// view is materialized per visible node <b>and</b> per visible link.
    /// </summary>
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(INotifyCollectionChanged),
        typeof(TreeView),
        null);

    public INotifyCollectionChanged? ItemsSource
    {
        get => (INotifyCollectionChanged?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private void UpdateItemsSource()
        => ItemsSource = (BindingContext as IWorkflowTreeViewModel)?.GetHelper()?.VisibleItems;

    // 一棵树一个 hub：换树时旧的退订、新的订阅。空白画布（Link 为 null）不弹菜单。
    private void UpdateInteraction()
    {
        if (_interaction is not null)
        {
            _interaction.ContextMenuRequested -= OnContextMenuRequested;
            _interaction = null;
        }

        if (BindingContext is IWorkflowTreeViewModel tree)
        {
            _interaction = LinkInteraction.For(tree);
            _interaction.ContextMenuRequested += OnContextMenuRequested;
        }
    }

    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is null)
        {
            return;
        }

        ShowLinkMenu(e.Link, e.Position);
    }

    // 条目自带 Command 就用它（绑定上下文是那条连线，弹出前设好）；没有就落到默认动作：删掉这条连线。
    private static void RunItem(MenuFlyoutItem item, IWorkflowLinkViewModel link)
    {
        if (item.Command is { } command)
        {
            var parameter = item.CommandParameter ?? link;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }

            return;
        }

        if (link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }
    }

#if WINDOWS
    // 菜单条目在 XAML 的 LinkContextMenu 里声明；这里只管定位与弹出。
    // 画布坐标 → 视口像素：px = Ruler + 锚点 + 内容偏移 − 滚动偏移（与链接层绘制/命中共用同一条换算）。
    private void ShowLinkMenu(IWorkflowLinkViewModel link, Anchor position)
    {
        if (PART_GridDecorator.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement host)
        {
            return;
        }

        var ruler = Math.Max(0d, PART_GridDecorator.RulerThickness);
        var x = ruler + position.Horizontal + PART_GridDecorator.ContentOffsetX - PART_GridDecorator.ScrollOffsetX;
        var y = ruler + position.Vertical + PART_GridDecorator.ContentOffsetY - PART_GridDecorator.ScrollOffsetY;

        var flyout = BuildPlatformMenu((MenuFlyout)Resources["LinkContextMenu"], link);

        // 开合报回 hub：菜单开着时指针飞到菜单上，也不该清掉这次选中的连线。
        flyout.Closed += (_, _) => _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, position, link));
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, position, link));

        flyout.ShowAt(host, new Windows.Foundation.Point(x, y));
    }

    // MAUI 没有能在指定点弹出的跨平台菜单；只有 Windows 的原生 MenuFlyout 能做到，所以把声明的条目翻成它。
    private static Microsoft.UI.Xaml.Controls.MenuFlyout BuildPlatformMenu(MenuFlyout declared, IWorkflowLinkViewModel link)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        foreach (var element in declared)
        {
            switch (element)
            {
                // MenuFlyoutSeparator derives from MenuFlyoutItem, so it must be matched first.
                case MenuFlyoutSeparator:
                    flyout.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
                    break;

                case MenuFlyoutItem item:
                    item.BindingContext = link;
                    var native = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                    };
                    native.Click += (_, _) => RunItem(item, link);
                    flyout.Items.Add(native);
                    break;
            }
        }

        return flyout;
    }
#else
    // 非 Windows 没有能在指定点弹出的跨平台菜单，所以把声明的条目填进 PART_LinkMenuLayer 这个浮层里，
    // 落在长按处。长按本身由链接层翻译成右键交给 hub；这里只负责呈现。
    private void ShowLinkMenu(IWorkflowLinkViewModel link, Anchor position)
    {
        _menuLink = link;

        var declared = (MenuFlyout)Resources["LinkContextMenu"];
        PART_LinkMenuItems.Children.Clear();
        foreach (var element in declared)
        {
            switch (element)
            {
                case MenuFlyoutSeparator:
                    PART_LinkMenuItems.Children.Add(new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#40FFFFFF"),
                        Margin = new Thickness(6, 2),
                    });
                    break;

                case MenuFlyoutItem item:
                    item.BindingContext = link;
                    var button = new Button
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                        BackgroundColor = Colors.Transparent,
                        TextColor = Colors.White,
                        HeightRequest = 36,
                        Padding = new Thickness(12, 0),
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    var captured = item;
                    button.Clicked += (_, _) => SelectMenuItem(captured);
                    PART_LinkMenuItems.Children.Add(button);
                    break;
            }
        }

        var ruler = Math.Max(0d, PART_GridDecorator.RulerThickness);
        var x = ruler + position.Horizontal + PART_GridDecorator.ContentOffsetX - PART_GridDecorator.ScrollOffsetX;
        var y = ruler + position.Vertical + PART_GridDecorator.ContentOffsetY - PART_GridDecorator.ScrollOffsetY;

        PART_LinkMenuHost.Margin = new Thickness(Math.Max(0d, x), Math.Max(0d, y), 0, 0);
        PART_LinkMenuLayer.IsVisible = true;
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, position, link));
    }

    private void SelectMenuItem(MenuFlyoutItem item)
    {
        var link = _menuLink;
        DismissLinkMenu();
        if (link is not null)
        {
            RunItem(item, link);
        }
    }
#endif

    // 收起弹出层，并告诉 hub 菜单已经关掉。关闭时位置没有意义。
    private void DismissLinkMenu()
    {
        if (!PART_LinkMenuLayer.IsVisible)
        {
            return;
        }

        PART_LinkMenuLayer.IsVisible = false;
        var link = _menuLink;
        _menuLink = null;
        if (link is not null)
        {
            _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, new Anchor(), link));
        }
    }

    private void OnLinkMenuScrimTapped(object? sender, TappedEventArgs e) => DismissLinkMenu();
}
