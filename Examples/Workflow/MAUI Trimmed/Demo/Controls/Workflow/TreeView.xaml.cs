// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class TreeView : ContentView
{
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
            UpdateNodeItemsSource();
        };
    }

    /// <summary>
    /// View-model collection fed to the canvas <c>ViewPool</c>. Mirrors
    /// <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link view
    /// models — links are rendered by the shared link overlay, so the pool must not
    /// materialize one per-link view (the per-link views also each filled the whole
    /// canvas and hit the Win2D texture cap on deep zoom).
    /// </summary>
    public static readonly BindableProperty NodeItemsSourceProperty = BindableProperty.Create(
        nameof(NodeItemsSource),
        typeof(INotifyCollectionChanged),
        typeof(TreeView),
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

    /// <summary>
    /// Mirrors <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link view
    /// models, so the node ViewPool only ever materializes node views. Links are rendered
    /// by the shared link overlay instead of one GraphicsView per link.
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
