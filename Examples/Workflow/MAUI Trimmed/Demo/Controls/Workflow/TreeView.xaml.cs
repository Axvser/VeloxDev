// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
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
            UpdateItemsSource();
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
}
