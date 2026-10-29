using System.Windows;
using System.Windows.Controls;
using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Lists the tree's nodes, slots and links, with a Locate and a Delete action per row.
/// </summary>
/// <remarks>
/// The list state lives in <see cref="WorkflowElementCatalog"/>, shared with the other full demos; this control only
/// renders it and forwards the two actions.
/// </remarks>
public partial class ElementListView : UserControl
{
    public static readonly DependencyProperty TreeProperty = DependencyProperty.Register(
        nameof(Tree),
        typeof(IWorkflowTreeViewModel),
        typeof(ElementListView),
        new PropertyMetadata(null, OnTreeChanged));

    private readonly WorkflowElementCatalog _catalog = new();

    public ElementListView()
    {
        InitializeComponent();

        NodeList.ItemsSource = _catalog.Nodes;
        SlotList.ItemsSource = _catalog.Slots;
        LinkList.ItemsSource = _catalog.Links;
        _catalog.Changed += (_, _) => StatusText.Text = _catalog.Summary;
        StatusText.Text = _catalog.Summary;
    }

    /// <summary>The tree whose elements are listed.</summary>
    public IWorkflowTreeViewModel? Tree
    {
        get => (IWorkflowTreeViewModel?)GetValue(TreeProperty);
        set => SetValue(TreeProperty, value);
    }

    private static void OnTreeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ElementListView)d)._catalog.SetTree(e.NewValue as IWorkflowTreeViewModel);

    private void OnLocate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkflowElementEntry entry) return;

        StatusText.Text = _catalog.Locate(entry)
            ? $"已定位 {entry.Label}"
            : "没有可用的表面，或该元素还没有有限坐标";
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkflowElementEntry entry) return;

        _catalog.Delete(entry);
        StatusText.Text = $"已删除 {entry.Label}";
    }
}
