// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    // 右键菜单挂在树的 hub 上；换树/摘树时先解绑，旧 hub 才不会继续握着这个控件的委托。
    private LinkInteraction? _interaction;

    public TemplateClass()
    {
        InitializeComponent();
        // The tree is assigned to this control by the host; propagate it to the node-only pool source
        // and re-point the link-menu handler at the new tree's interaction hub.
        BindingContextChanged += (_, _) =>
        {
            UpdateNodeItemsSource();
            UpdateInteraction();
        };
    }

    /// <summary>
    /// View-model collection fed to the canvas <c>ViewPool</c>. Mirrors
    /// <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link view
    /// models — links are rendered by the shared link layer, so the pool must not
    /// materialize one per-link view (the per-link views also each filled the whole
    /// canvas and hit the Win2D texture cap on deep zoom).
    /// </summary>
    public static readonly BindableProperty NodeItemsSourceProperty = BindableProperty.Create(
        nameof(NodeItemsSource),
        typeof(INotifyCollectionChanged),
        typeof(TemplateClass),
        null);

    public INotifyCollectionChanged? NodeItemsSource
    {
        get => (INotifyCollectionChanged?)GetValue(NodeItemsSourceProperty);
        set => SetValue(NodeItemsSourceProperty, value);
    }

    private void UpdateNodeItemsSource()
    {
        if (NodeItemsSource is NodeOnlyVisibleItems wrapper)
        {
            wrapper.Detach();
        }

        var visible = (BindingContext as IWorkflowTreeViewModel)?.GetHelper()?.VisibleItems;
        NodeItemsSource = visible is null ? null : new NodeOnlyVisibleItems(visible);
    }

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
        if (e.Link is null || e.Handle.PreventDefault)
        {
            return;
        }

#if WINDOWS
        ShowLinkMenu(e.Link, e.Position);
#endif
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

    // 条目自带 Command 就用它（参数默认是被点的连线）；没有就落到默认动作：删掉这条连线。
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
#endif

    /// <summary>
    /// Mirrors <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link view
    /// models, so the node ViewPool only ever materializes node views. Links are rendered
    /// by the shared link layer instead of one GraphicsView per link.
    /// </summary>
    private sealed class NodeOnlyVisibleItems : ObservableCollection<IWorkflowViewModel>
    {
        private readonly ObservableCollection<IWorkflowViewModel> _source;

        public NodeOnlyVisibleItems(ObservableCollection<IWorkflowViewModel> source)
        {
            _source = source;
            _source.CollectionChanged += OnSourceChanged;
            foreach (var item in source)
            {
                if (item is not IWorkflowLinkViewModel)
                {
                    Add(item);
                }
            }
        }

        /// <summary>Unsubscribes from the source so this wrapper can be garbage-collected on session change.</summary>
        public void Detach() => _source.CollectionChanged -= OnSourceChanged;

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in e.NewItems ?? Array.Empty<object>())
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Add((IWorkflowViewModel)item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems ?? Array.Empty<object>())
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Remove((IWorkflowViewModel)item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Clear();
                    foreach (var item in _source)
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Add(item);
                        }
                    }
                    break;
            }
        }
    }
}
