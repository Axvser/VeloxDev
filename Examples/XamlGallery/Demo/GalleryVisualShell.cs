using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlBinding = VeloxDev.WinForms.Xaml.Binding;
using XamlColumnDefinition = VeloxDev.WinForms.Xaml.Controls.ColumnDefinition;
using XamlDockPlacement = VeloxDev.WinForms.Xaml.Controls.DockPlacement;
using XamlGridLength = VeloxDev.WinForms.Xaml.Controls.GridLength;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlRowDefinition = VeloxDev.WinForms.Xaml.Controls.RowDefinition;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace Demo;

/// <summary>The three-pane visual-backend gallery: catalog, preview and dependency-property inspector.</summary>
internal sealed class GalleryVisualShell
{
    private readonly GalleryModel _model;
    private readonly GalleryShellViewModel _vm;
    private readonly GalleryCardCatalog _catalog;
    private readonly Dictionary<VC.TreeViewItem, string> _entryItems = [];
    private readonly GalleryVisualInspector _inspector = new();
    private readonly VC.TreeView _catalogTree = new() { Focusable = true };
    private readonly VC.TextBox _search = new()
    {
        Placeholder = "Filter controls…",
        Height = 30,
        Margin = new XamlThickness(0f, 0f, 0f, 8f),
    };
    private readonly VC.ContentControl _previewHost = new();
    private VC.ScrollViewer _previewScroll = null!;
    private readonly VC.TextBlock _title = new();
    private readonly VC.TextBlock _summary = new();
    private readonly VC.TextBlock _usage = new();
    private readonly VC.TextBlock _status = new();
    private readonly VC.Expander _usageExpander;
    private readonly VC.Border _titleBar;
    private readonly VC.Border _statusBar;
    private readonly VC.Border _catalogPane;
    private readonly VC.Border _inspectorPane;
    private readonly VC.GridSplitter _leftSplitter;
    private readonly VC.GridSplitter _rightSplitter;
    private bool _syncingSelection;

    /// <summary>Builds the shell for an existing view model and selection.</summary>
    /// <param name="model">The shared sample data used by the card factories.</param>
    /// <param name="vm">The shell state; it survives palette rebuilds.</param>
    /// <param name="catalog">The card metadata/factory pairs.</param>
    public GalleryVisualShell(GalleryModel model, GalleryShellViewModel vm, GalleryCardCatalog catalog)
    {
        _model = model;
        _vm = vm;
        _catalog = catalog;
        var theme = Theme.Current;

        _title.Font = new Font("Segoe UI Semibold", 16f);
        _title.Foreground = theme.Foreground;
        _summary.Foreground = theme.MutedForeground;
        _summary.Wrap = true;
        _usage.Font = new Font("Consolas", 10f);
        _usage.Foreground = theme.Foreground;
        _usage.Wrap = true;
        _status.Foreground = theme.MutedForeground;
        _usageExpander = new VC.Expander { Header = "Usage", IsExpanded = false, Content = _usage };

        _titleBar = BuildTitleBar(theme);
        _statusBar = BuildStatusBar(theme);
        _catalogPane = BuildCatalogPane(theme);
        _inspectorPane = BuildInspectorPane(theme);
        _leftSplitter = new VC.GridSplitter
        {
            Target = _catalogPane,
            Orientation = Orientation.Vertical,
            Width = 6,
            Height = 200,
        };
        _rightSplitter = new VC.GridSplitter
        {
            Target = _inspectorPane,
            Orientation = Orientation.Vertical,
            Width = 6,
            Height = 200,
        };

        var preview = BuildPreviewPane(theme);
        var root = new VC.DockPanel { Background = theme.WindowBackground, LastChildFill = true };
        root.Add(_titleBar);
        VC.DockPanel.SetDock(_titleBar, XamlDockPlacement.Top);
        root.Add(_statusBar);
        VC.DockPanel.SetDock(_statusBar, XamlDockPlacement.Bottom);
        root.Add(_catalogPane);
        VC.DockPanel.SetDock(_catalogPane, XamlDockPlacement.Left);
        root.Add(_leftSplitter);
        VC.DockPanel.SetDock(_leftSplitter, XamlDockPlacement.Left);
        root.Add(_inspectorPane);
        VC.DockPanel.SetDock(_inspectorPane, XamlDockPlacement.Right);
        root.Add(_rightSplitter);
        VC.DockPanel.SetDock(_rightSplitter, XamlDockPlacement.Right);
        root.Add(preview);

        Root = root;
        _search.SetBinding(
            VC.TextBox.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.FilterText))
            {
                Source = _vm,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        _title.SetBinding(
            VC.TextBlock.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.Title)) { Source = _vm });
        _summary.SetBinding(
            VC.TextBlock.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.Summary)) { Source = _vm });
        _usage.SetBinding(
            VC.TextBlock.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.Usage)) { Source = _vm });
        _status.SetBinding(
            VC.TextBlock.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.StatusText)) { Source = _vm });

        _catalogTree.SelectionChanged += OnCatalogSelectionChanged;
        _vm.CatalogChanged += OnCatalogChanged;
        _vm.PreviewChanged += OnPreviewChanged;
        RebuildCatalog();
        if (_vm.SelectedCard is { } initial)
        {
            ShowCard(initial);
        }
    }

    /// <summary>The visual root of the gallery.</summary>
    public VisualElement Root { get; }

    /// <summary>Every card in catalog order.</summary>
    public IReadOnlyList<GalleryCard> Cards => _catalog.Cards;

    /// <summary>The selected card id, or <see langword="null"/>.</summary>
    public string? SelectedId => _vm.SelectedCard?.Id;

    /// <summary>The current catalog filter text.</summary>
    public string FilterText => _vm.FilterText;

    /// <summary>Selects a card and updates the preview and inspector.</summary>
    /// <param name="id">The card id.</param>
    /// <returns><see langword="false"/> when the id is unknown.</returns>
    public bool SelectEntry(string id)
    {
        if (!_vm.SelectCard(id))
        {
            return false;
        }

        var item = _vm.SelectedCard is { } card
            ? _entryItems.FirstOrDefault(pair => pair.Value == card.Id).Key
            : null;
        if (item is null)
        {
            RebuildCatalog();
            item = _vm.SelectedCard is { } retry
                ? _entryItems.FirstOrDefault(pair => pair.Value == retry.Id).Key
                : null;
        }

        if (item is not null)
        {
            _catalogTree.Reveal(item);
        }

        return true;
    }

    /// <summary>Scrolls the preview viewport.</summary>
    /// <param name="offset">The vertical offset in pixels.</param>
    public void SetPreviewScrollOffset(float offset) => _previewScroll.VerticalOffset = offset;

    /// <summary>Hides the title, catalog, inspector and status bar so only the preview remains.</summary>
    /// <param name="visible">Whether the chrome is shown.</param>
    public void SetChromeVisible(bool visible)
    {
        var visibility = visible ? XamlVisibility.Visible : XamlVisibility.Collapsed;
        _titleBar.Visibility = visibility;
        _statusBar.Visibility = visibility;
        _catalogPane.Visibility = visibility;
        _inspectorPane.Visibility = visibility;
        _leftSplitter.Visibility = visibility;
        _rightSplitter.Visibility = visibility;
        Root.InvalidateMeasure();
    }

    /// <summary>Finds a card by id.</summary>
    /// <param name="id">The card id.</param>
    public GalleryCard? FindCard(string id) => _catalog.Find(id);

    private VC.Border BuildTitleBar(ThemePalette theme)
    {
        var stack = new VC.StackPanel { Spacing = 3 };
        stack.Add(new VC.TextBlock
        {
            Text = "Modern WinForms Gallery",
            Font = new Font("Segoe UI Semibold", 18f),
            Foreground = theme.Foreground,
        });
        stack.Add(new VC.TextBlock
        {
            Text = "A self-drawn WinForms gallery · one HWND · a visual control tree",
            Foreground = theme.MutedForeground,
        });

        var badge = new VC.Border
        {
            Background = theme.Accent,
            CornerRadius = 10f,
            Padding = new XamlThickness(10f, 4f),
            Child = new VC.TextBlock
            {
                Text = "visual backend",
                Foreground = theme.AccentForeground,
            },
        };
        var toggle = new VC.Button { Height = 32 };
        toggle.SetBinding(
            VC.Button.TextProperty,
            new XamlBinding(nameof(GalleryShellViewModel.ThemeModeLabel)) { Source = _vm });
        toggle.SetBinding(
            VC.Button.CommandProperty,
            new XamlBinding(nameof(GalleryShellViewModel.CycleThemeCommand)) { Source = _vm });

        var actions = new VC.StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = XamlHorizontalAlignment.Right,
            VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
        };
        actions.Add(badge);
        actions.Add(toggle);

        var grid = new VC.Grid();
        grid.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Star });
        grid.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Auto });
        grid.Add(stack);
        grid.Add(actions);
        VC.Grid.SetColumn(actions, 1);

        return new VC.Border
        {
            Height = 78,
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(0f, 0f, 0f, 1f),
            Padding = new XamlThickness(22f, 14f),
            Child = grid,
        };
    }

    private VC.Border BuildStatusBar(ThemePalette theme)
    {
        return new VC.Border
        {
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(0f, 1f, 0f, 0f),
            Padding = new XamlThickness(22f, 5f),
            Child = _status,
        };
    }

    private VC.Border BuildCatalogPane(ThemePalette theme)
    {
        var scroll = new VC.ScrollViewer { Content = _catalogTree };
        var pane = new VC.DockPanel { LastChildFill = true };
        pane.Add(_search);
        VC.DockPanel.SetDock(_search, XamlDockPlacement.Top);
        pane.Add(scroll);

        return new VC.Border
        {
            Width = 300,
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(0f, 0f, 1f, 0f),
            Padding = new XamlThickness(14f),
            Child = pane,
        };
    }

    private VC.Border BuildInspectorPane(ThemePalette theme)
    {
        var header = new VC.TextBlock
        {
            Text = "Properties",
            Font = new Font("Segoe UI Semibold", 11f),
            Foreground = theme.Accent,
            Margin = new XamlThickness(0f, 0f, 0f, 8f),
        };
        var scroll = new VC.ScrollViewer { Content = _inspector.Root };
        var pane = new VC.DockPanel { LastChildFill = true };
        pane.Add(header);
        VC.DockPanel.SetDock(header, XamlDockPlacement.Top);
        pane.Add(scroll);

        return new VC.Border
        {
            Width = 340,
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f, 0f, 0f, 0f),
            Padding = new XamlThickness(14f),
            Child = pane,
        };
    }

    private VC.Border BuildPreviewPane(ThemePalette theme)
    {
        var header = new VC.StackPanel { Spacing = 4, Margin = new XamlThickness(0f, 0f, 0f, 10f) };
        header.Add(_title);
        header.Add(_summary);

        var frame = new VC.Border
        {
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 12f,
            Padding = new XamlThickness(18f),
            Child = _previewHost,
        };
        var scroll = new VC.ScrollViewer { Content = frame };
        _previewScroll = scroll;
        var pane = new VC.DockPanel { LastChildFill = true, Padding = new XamlThickness(18f) };
        pane.Add(header);
        VC.DockPanel.SetDock(header, XamlDockPlacement.Top);
        pane.Add(_usageExpander);
        VC.DockPanel.SetDock(_usageExpander, XamlDockPlacement.Bottom);
        pane.Add(scroll);
        return new VC.Border { Background = theme.WindowBackground, Child = pane };
    }

    private void OnCatalogSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection
            || _catalogTree.SelectedItem is not { } item
            || !_entryItems.TryGetValue(item, out var id))
        {
            return;
        }

        _vm.SelectCard(id);
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => RebuildCatalog();

    private void OnPreviewChanged(object? sender, GalleryCardInfo? info)
    {
        if (info is null)
        {
            ClearPreview();
        }
        else
        {
            ShowCard(info);
        }
    }

    private void RebuildCatalog()
    {
        _entryItems.Clear();
        _catalogTree.Items.Clear();
        foreach (var group in _vm.VisibleGroups)
        {
            var groupItem = new VC.TreeViewItem { Header = group.Name, IsExpanded = true };
            foreach (var info in group.Cards)
            {
                var item = new VC.TreeViewItem { Header = info.Title };
                _entryItems[item] = info.Id;
                groupItem.Items.Add(item);
            }

            _catalogTree.Items.Add(groupItem);
        }

        var selected = _vm.SelectedCard is { } card
            ? _entryItems.FirstOrDefault(pair => pair.Value == card.Id).Key
            : null;
        _syncingSelection = true;
        try
        {
            _catalogTree.SelectedItem = selected ?? _entryItems.Keys.FirstOrDefault();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void ShowCard(GalleryCardInfo info)
    {
        Root.Surface?.ClosePopups();
        _usageExpander.IsExpanded = false;
        _previewScroll.VerticalOffset = 0;

        var previous = _previewHost.Content as VisualElement;
        var next = _catalog.Find(info.Id)?.Create(_model);
        _previewHost.Content = next;
        previous?.Dispose();
        _inspector.SetTarget(next);
    }

    private void ClearPreview()
    {
        Root.Surface?.ClosePopups();
        _usageExpander.IsExpanded = false;
        _previewScroll.VerticalOffset = 0;
        _inspector.SetTarget(null);

        var previous = _previewHost.Content as VisualElement;
        _previewHost.Content = null;
        previous?.Dispose();
    }
}
