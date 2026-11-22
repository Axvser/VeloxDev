using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
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
    private readonly Action? _rebuild;
    private readonly Dictionary<string, GalleryCard> _byId;
    private readonly Dictionary<VC.TreeViewItem, string> _entryItems = [];
    private readonly GalleryVisualInspector _inspector = new();
    private readonly VC.TreeView _catalog = new() { Focusable = true };
    private readonly VC.TextBox _search = new() { Placeholder = "Filter controls…", Height = 30 };
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
    private string? _selectedId;

    /// <summary>Builds the shell and selects the initial card.</summary>
    /// <param name="model">The shared sample data.</param>
    /// <param name="rebuild">Rebuilds the gallery after a theme change; may be <see langword="null"/>.</param>
    /// <param name="initialId">The card to select, or <see langword="null"/> for the first card.</param>
    /// <param name="filter">The initial catalog filter.</param>
    public GalleryVisualShell(GalleryModel model, Action? rebuild, string? initialId = null, string? filter = null)
    {
        _model = model;
        _rebuild = rebuild;
        Cards = GalleryVisualCards.Create(model, rebuild);
        _byId = Cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);
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
        if (!string.IsNullOrEmpty(filter))
        {
            _search.Text = filter;
        }

        _catalog.SelectionChanged += OnCatalogSelectionChanged;
        _search.PropertyChanged += OnSearchChanged;
        RebuildCatalog();
        SelectEntry(initialId ?? Cards[0].Id);
    }

    /// <summary>The visual root of the gallery.</summary>
    public VisualElement Root { get; }

    /// <summary>Every card in catalog order.</summary>
    public IReadOnlyList<GalleryCard> Cards { get; }

    /// <summary>The selected card id, or <see langword="null"/>.</summary>
    public string? SelectedId => _selectedId;

    /// <summary>The current catalog filter text.</summary>
    public string FilterText => _search.Text;

    /// <summary>Selects a card and updates the preview and inspector.</summary>
    /// <param name="id">The card id.</param>
    /// <returns><see langword="false"/> when the id is unknown.</returns>
    public bool SelectEntry(string id)
    {
        if (!_byId.ContainsKey(id))
        {
            return false;
        }

        if (!_entryItems.ContainsValue(id))
        {
            _search.Text = string.Empty;
            RebuildCatalog();
        }

        var item = _entryItems.FirstOrDefault(pair => pair.Value == id).Key;
        if (item is null)
        {
            return false;
        }

        if (ReferenceEquals(_catalog.SelectedItem, item))
        {
            ShowCard(id);
        }
        else
        {
            _catalog.Reveal(item);
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
    public GalleryCard? FindCard(string id) => _byId.TryGetValue(id, out var card) ? card : null;

    private VC.Border BuildTitleBar(ThemePalette theme)
    {
        var stack = new VC.StackPanel { Spacing = 3 };
        stack.Add(new VC.TextBlock
        {
            Text = "VeloxDev Visual Gallery",
            Font = new Font("Segoe UI Semibold", 18f),
            Foreground = theme.Foreground,
        });
        stack.Add(new VC.TextBlock
        {
            Text = "One HWND · a visual control tree · self-drawn UI",
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
        var toggle = new VC.Button
        {
            Text = Theme.Mode == ThemeMode.Dark ? "Light+ mode" : "Dark+ mode",
            Height = 32,
        };
        if (_rebuild is not null)
        {
            toggle.Click += (_, _) => _rebuild();
        }

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
        _status.Text = "Ready";
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
        var header = new VC.TextBlock
        {
            Text = "Catalog",
            Font = new Font("Segoe UI Semibold", 11f),
            Foreground = theme.Accent,
            Margin = new XamlThickness(0f, 0f, 0f, 8f),
        };
        var scroll = new VC.ScrollViewer { Content = _catalog };
        var pane = new VC.DockPanel { LastChildFill = true };
        pane.Add(header);
        VC.DockPanel.SetDock(header, XamlDockPlacement.Top);
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
        if (_catalog.SelectedItem is { } item && _entryItems.TryGetValue(item, out var id))
        {
            ShowCard(id);
        }
    }

    private void OnSearchChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.Property == VC.TextBox.TextProperty)
        {
            RebuildCatalog();
        }
    }

    private void RebuildCatalog()
    {
        _entryItems.Clear();
        _catalog.Items.Clear();
        var filter = _search.Text.Trim();
        foreach (var group in Cards.GroupBy(static card => card.Group, StringComparer.Ordinal))
        {
            var matches = group.Where(card => Matches(card, filter)).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var groupItem = new VC.TreeViewItem { Header = group.Key, IsExpanded = true };
            foreach (var card in matches)
            {
                var item = new VC.TreeViewItem { Header = card.Title };
                _entryItems[item] = card.Id;
                groupItem.Items.Add(item);
            }

            _catalog.Items.Add(groupItem);
        }

        var selected = _selectedId is not null
            ? _entryItems.FirstOrDefault(pair => pair.Value == _selectedId).Key
            : null;
        _catalog.SelectedItem = selected ?? _entryItems.Keys.FirstOrDefault();
        if (_catalog.SelectedItem is null)
        {
            _previewHost.Content = null;
            _title.Text = "No matches";
            _summary.Text = "Clear the filter to show the catalog again.";
            _inspector.SetTarget(null);
        }
    }

    private static bool Matches(GalleryCard card, string filter)
        => filter.Length == 0
           || card.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Group.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void ShowCard(string id)
    {
        if (!_byId.TryGetValue(id, out var card))
        {
            return;
        }

        Root.Surface?.ClosePopups();
        _selectedId = id;
        _title.Text = card.Title;
        _summary.Text = card.Summary;
        _usage.Text = card.Usage;
        _usageExpander.IsExpanded = false;
        _previewScroll.VerticalOffset = 0;

        var previous = _previewHost.Content as VisualElement;
        var next = card.Create(_model);
        _previewHost.Content = next;
        previous?.Dispose();
        _inspector.SetTarget(next);
        _status.Text = $"{card.Group} · {card.Id}";
    }
}
