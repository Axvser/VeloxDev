// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    // The link menu lives on the tree's interaction hub; unbind it before rebinding so an old hub does not
    // keep holding this control's delegate.
    private LinkInteraction? _interaction;

    // The link the open popup acts on; null while no popup is shown. Also the guard for
    // ContextMenuDismissRequested, so a dismissal only closes the menu that matches.
    private IWorkflowLinkViewModel? _menuLink;

#if WINDOWS
    // The native flyout currently up (Windows only); Core cannot close it, so the host hides it and
    // clears this in the flyout's Closed handler.
    private Microsoft.UI.Xaml.Controls.MenuFlyout? _openFlyout;
#endif

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

    // One hub per tree: unsubscribe the old tree, subscribe the new one. An empty canvas (Link is null)
    // opens no menu.
    private void UpdateInteraction()
    {
        if (_interaction is not null)
        {
            _interaction.ContextMenuRequested -= OnContextMenuRequested;
            _interaction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
            _interaction = null;
        }

        if (BindingContext is IWorkflowTreeViewModel tree)
        {
            _interaction = LinkInteraction.For(tree);
            _interaction.ContextMenuRequested += OnContextMenuRequested;
            _interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
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

    // An entry uses its own Command when it has one (its BindingContext is the link, set before showing);
    // otherwise it falls back to deleting the link.
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
    // The entries are declared in the LinkContextMenu resource; this only places and opens them.
    // Canvas-local anchor -> viewport pixels: px = Ruler + anchor + ContentOffset − ScrollOffset,
    // the identity the link layer draws and hit-tests with.
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

        _menuLink = link;
        _openFlyout = flyout;

        // Report open/close to the hub so the pointer travelling onto the menu does not clear the link.
        flyout.Closed += (_, _) =>
        {
            _openFlyout = null;
            _menuLink = null;
            _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, position, link));
        };
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, position, link));

        flyout.ShowAt(host, new Windows.Foundation.Point(x, y));
    }

    // The link this menu acts on left the tree: Core cannot close the native flyout, so hide it here.
    // Closed then reports ContextMenuPhase.Closed as usual, releasing the suspension.
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _openFlyout?.Hide();
    }

    // MAUI has no cross-platform menu that opens at a point; only the Windows native MenuFlyout can, so the
    // declared entries are translated into it.
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
    // MAUI has no cross-platform point popup, so the declared entries are materialized into the
    // PART_LinkMenuLayer overlay and placed at the anchor. The long press that asks for the menu is raised
    // by the link layer as a right press; this is only the presentation.
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

    // The link this menu acts on left the tree: Core cannot close this overlay, so dismiss it here.
    // Dismissal reports ContextMenuPhase.Closed as usual, releasing the suspension.
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        DismissLinkMenu();
    }
#endif

    // Hides the popup and tells the hub the menu is gone. The position is ignored when closing.
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
