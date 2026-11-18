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
    private TreeViewModel? _boundTree;
    private bool _syncingSelection;

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
    {
        var view = (ElementListView)d;
        if (view._boundTree is not null) view._boundTree.PropertyChanged -= view.OnTreePropertyChanged;
        view._boundTree = e.NewValue as TreeViewModel;
        if (view._boundTree is not null) view._boundTree.PropertyChanged += view.OnTreePropertyChanged;
        view._catalog.SetTree(e.NewValue as IWorkflowTreeViewModel);
        view.SyncSelection();
    }

    // 三个列表共用树上的一个选中项。绑定 SelectedItem 三个列表会互相清空对方，形成一条
    // 设置→清空→再设置的回路；用事件 + 守卫只走一个方向。
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListBox list) return;
        if (list.SelectedItem is not WorkflowElementEntry entry) return;

        _syncingSelection = true;
        try
        {
            if (!ReferenceEquals(list, NodeList)) NodeList.SelectedItem = null;
            if (!ReferenceEquals(list, SlotList)) SlotList.SelectedItem = null;
            if (!ReferenceEquals(list, LinkList)) LinkList.SelectedItem = null;
            if (_boundTree is not null) _boundTree.SelectedElement = entry;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnTreePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeViewModel.SelectedElement)) SyncSelection();
    }

    private void SyncSelection()
    {
        if (_syncingSelection) return;

        _syncingSelection = true;
        try
        {
            var selected = _boundTree?.SelectedElement;
            NodeList.SelectedItem = selected is not null && NodeList.Items.Contains(selected) ? selected : null;
            SlotList.SelectedItem = selected is not null && SlotList.Items.Contains(selected) ? selected : null;
            LinkList.SelectedItem = selected is not null && LinkList.Items.Contains(selected) ? selected : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

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

        Delete(entry);
        StatusText.Text = $"已删除 {entry.Label}";
    }

    /// <summary>Centers the entry in its surface; the inspector's own Locate button reaches it through here.</summary>
    public bool Locate(WorkflowElementEntry entry) => _catalog.Locate(entry);

    /// <summary>Deletes the entry and releases it if the inspector was describing it.</summary>
    public void Delete(WorkflowElementEntry entry)
    {
        _catalog.Delete(entry);
        if (Tree is TreeViewModel tree && ReferenceEquals(tree.SelectedElement, entry))
            tree.SelectedElement = null;
    }
}
