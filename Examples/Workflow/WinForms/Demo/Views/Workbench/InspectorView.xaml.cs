using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using Demo.ViewModels;
using Easel;
using Easel.Controls;
using Easel.Visuals;
using Easel.Visuals.Controls;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.Views.Workbench;

/// <summary>
/// The inspector's 属性 page: the selected element, then the compiled structure. Run status, canvas totals, zoom
/// and the minimap toggle are absent — the status bar and the element page already report them.
/// </summary>
public sealed partial class InspectorView : WorkbenchViewBase
{
    private TreeViewModel? _tree;
    private WorkflowElementEntry? _selected;

    /// <summary>Creates the page from XAML.</summary>
    public InspectorView()
    {
        InitializeComponent();
        RefreshSelection();
        RefreshCompiled();
    }

    /// <summary>The tree the inspector describes; swapping demos rebinds every subscription.</summary>
    public TreeViewModel? Tree
    {
        get => _tree;
        set
        {
            if (ReferenceEquals(_tree, value))
            {
                return;
            }

            Detach();
            _tree = value;
            Attach();
            RefreshSelection();
            RefreshCompiled();
        }
    }

    private void Attach()
    {
        if (_tree is null)
        {
            return;
        }

        _tree.PropertyChanged += OnTreePropertyChanged;
        _tree.CompiledStructure.CollectionChanged += OnCompiledStructureChanged;
    }

    private void Detach()
    {
        if (_tree is not null)
        {
            _tree.PropertyChanged -= OnTreePropertyChanged;
            _tree.CompiledStructure.CollectionChanged -= OnCompiledStructureChanged;
        }

        if (_selected is not null)
        {
            _selected.PropertyChanged -= OnSelectedEntryChanged;
        }

        _selected = null;
        _tree = null;
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreeViewModel.SelectedElement) or nameof(TreeViewModel.HasSelectedElement))
        {
            WorkbenchDispatcher.Post(RefreshSelection);
        }
    }

    private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowElementEntry.DetailText))
        {
            WorkbenchDispatcher.Post(() => DetailText.Text = _selected?.DetailText ?? string.Empty);
        }
    }

    private void OnCompiledStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(RefreshCompiled);

    private void RefreshSelection()
    {
        if (_selected is not null)
        {
            _selected.PropertyChanged -= OnSelectedEntryChanged;
        }

        _selected = _tree?.SelectedElement;
        if (_selected is not null)
        {
            _selected.PropertyChanged += OnSelectedEntryChanged;
        }

        var hasSelection = _selected is not null;
        SelectedContent.Visibility = hasSelection ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        SelectedHint.Visibility = hasSelection ? Easel.Controls.Visibility.Collapsed : Easel.Controls.Visibility.Visible;

        NameLabel.Text = _selected?.Label ?? string.Empty;
        KindLabel.Text = _selected?.KindText ?? string.Empty;
        DetailText.Text = _selected?.DetailText ?? string.Empty;
    }

    private void RefreshCompiled()
    {
        CompiledRows.Children.Clear();
        var rows = _tree?.CompiledStructure;
        var count = rows?.Count ?? 0;

        if (rows is not null)
        {
            foreach (var row in rows)
            {
                CompiledRows.Add(CreateCompiledRow(row));
            }
        }

        CompiledCountLabel.Text = $"{count} 段";
        CompiledRows.Visibility = count > 0 ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        CompiledEmpty.Visibility = count == 0 ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
    }

    private static VisualElement CreateCompiledRow(CompiledOutlineRow row)
    {
        var grid = new Grid { Height = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58 + (row.Depth * 12)) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var kind = new TextBlock
        {
            Text = row.Kind,
            FontSize = 8,
            Foreground = WorkbenchPalette.Warning,
            VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
            Margin = new Thickness(row.Depth * 12, 0, 0, 0),
        };
        Grid.SetColumn(kind, 0);
        grid.Add(kind);

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 8,
            Foreground = WorkbenchPalette.TextPrimary,
            VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Add(label);

        return new Border
        {
            Background = new SolidColorBrush(WorkbenchPalette.PanelRaised),
            Padding = new Thickness(0),
            CornerRadius = 0,
            Height = 18,
            Child = grid,
        };
    }

    private void OnLocate(object? sender, EventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        switch (entry.Element)
        {
            case INodeComponent node:
                node.Locate();
                break;
            case ISlotComponent slot:
                slot.Locate();
                break;
            case ILinkComponent link:
                link.Locate();
                break;
        }
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        switch (entry.Element)
        {
            case INodeComponent node:
                node.GetHelper().Delete();
                break;
            case ISlotComponent slot:
                slot.GetHelper().Delete();
                break;
            case ILinkComponent link:
                link.GetHelper().Delete();
                break;
        }

        if (_tree is not null && ReferenceEquals(_tree.SelectedElement, entry))
        {
            _tree.SelectedElement = null;
        }
    }
}
