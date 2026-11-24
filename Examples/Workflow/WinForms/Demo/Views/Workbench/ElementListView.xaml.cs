using System.Drawing;
using Demo.ViewModels;
using Easel;
using Easel.Controls;
using Easel.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using Button = Easel.Visuals.Controls.Button;

namespace Demo.Views.Workbench;

/// <summary>
/// The explorer's element table: nodes, slots and links in three tabs, each row carrying Locate/Delete. The
/// catalog owns the rows; this view only renders them and forwards the two actions.
/// </summary>
public sealed partial class ElementListView : WorkbenchViewBase
{
    private readonly WorkflowElementCatalog _catalog = new();
    private WorkflowElementKind _kind = WorkflowElementKind.Node;
    private WorkflowElementEntry? _selected;
    private TreeViewModel? _tree;
    private bool _suppress;

    /// <summary>Creates the list from XAML.</summary>
    public ElementListView()
    {
        InitializeComponent();
        _catalog.Changed += (_, _) => WorkbenchDispatcher.Post(RefreshRows);
        RefreshRows();
    }

    /// <summary>Raised when a row is picked; the host feeds it to the tree.</summary>
    public event EventHandler<WorkflowElementEntry?>? SelectedElementChanged;

    /// <summary>The tree the list describes; swapping trees rebuilds the catalog.</summary>
    public void SetTree(ITreeComponent? tree)
    {
        _tree = tree as TreeViewModel;
        _catalog.SetTree(tree);
        RefreshRows();
    }

    /// <summary>Highlights <paramref name="entry"/> without raising an event back.</summary>
    public void SetSelectedElement(WorkflowElementEntry? entry)
    {
        _suppress = true;
        try
        {
            _selected = entry;
        }
        finally
        {
            _suppress = false;
        }

        ApplySelection();
    }

    private void OnKindClick(object? sender, EventArgs e)
    {
        _kind = ReferenceEquals(sender, SlotTab)
            ? WorkflowElementKind.Slot
            : ReferenceEquals(sender, LinkTab)
                ? WorkflowElementKind.Link
                : WorkflowElementKind.Node;

        NodeTab.IsActive = _kind == WorkflowElementKind.Node;
        SlotTab.IsActive = _kind == WorkflowElementKind.Slot;
        LinkTab.IsActive = _kind == WorkflowElementKind.Link;
        RefreshRows();
    }

    private void RefreshRows()
    {
        IReadOnlyList<WorkflowElementEntry> entries = _kind switch
        {
            WorkflowElementKind.Slot => _catalog.Slots,
            WorkflowElementKind.Link => _catalog.Links,
            _ => _catalog.Nodes,
        };

        RowStack.Children.Clear();
        foreach (var entry in entries)
        {
            RowStack.Add(CreateRow(entry));
        }

        StatusLine.Text = _catalog.Summary;
        ApplySelection();
    }

    private Border CreateRow(WorkflowElementEntry entry)
    {
        var row = new Border
        {
            Height = 27,
            Background = new SolidColorBrush(WorkbenchPalette.Surface),
            Padding = new Thickness(6, 0, 2, 0),
            Cursor = System.Windows.Forms.Cursors.Hand,
            DataContext = entry,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = entry.Label,
            FontSize = 9,
            Foreground = WorkbenchPalette.TextSecondary,
            VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Add(label);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
        };
        actions.Add(CreateAction("定位", WorkbenchPalette.AccentText, () =>
        {
            var found = _catalog.Locate(entry);
            StatusLine.Text = found
                ? $"{_catalog.Summary} · 已定位 {entry.Label}"
                : $"{_catalog.Summary} · 未能定位 {entry.Label}";
        }));
        actions.Add(CreateAction("删除", WorkbenchPalette.Danger, () =>
        {
            _catalog.Delete(entry);
            StatusLine.Text = $"{_catalog.Summary} · 已删除 {entry.Label}";
        }));
        Grid.SetColumn(actions, 1);
        grid.Add(actions);

        row.Child = grid;
        row.PointerEntered += (_, _) =>
        {
            if (!ReferenceEquals(_selected, entry)) row.Background = new SolidColorBrush(WorkbenchPalette.Hover);
        };
        row.PointerExited += (_, _) =>
        {
            if (!ReferenceEquals(_selected, entry)) row.Background = new SolidColorBrush(WorkbenchPalette.Surface);
        };
        row.PointerPressed += (_, args) =>
        {
            if (args.Button != System.Windows.Forms.MouseButtons.Left)
            {
                return;
            }

            Choose(entry);
            args.Handled = true;
        };

        return row;
    }

    private static Button CreateAction(string text, Color color, Action action)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 7.5,
            Foreground = color,
            Background = null,
            BorderThickness = default,
            Padding = new Thickness(4, 1, 4, 1),
            MinHeight = 18,
            CornerRadius = 3,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Choose(WorkflowElementEntry entry)
    {
        if (_suppress)
        {
            return;
        }

        _selected = entry;
        ApplySelection();
        SelectedElementChanged?.Invoke(this, entry);
    }

    private void ApplySelection()
    {
        foreach (var child in RowStack.Children)
        {
            if (child is not Border row || row.DataContext is not WorkflowElementEntry entry)
            {
                continue;
            }

            row.Background = new SolidColorBrush(
                ReferenceEquals(_selected, entry) ? WorkbenchPalette.Hover : WorkbenchPalette.Surface);
        }
    }
}
