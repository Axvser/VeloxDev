using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;

namespace Demo.Views;

/// <summary>The three-pane shell: catalog tree, preview and selected-element inspector.</summary>
public sealed partial class GalleryView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private readonly GalleryModel _model = new();
    private readonly GalleryTreeBuilder _tree;
    private readonly GalleryPreviewController _preview;
    private readonly GallerySelection _selection;
    private readonly GalleryInspector _inspector;
    private GalleryEntry? _currentEntry;
    private System.Windows.Forms.Control? _selectionRoot;
    private bool _advanced;

    /// <summary>Creates the view, binds the model and shows the first entry.</summary>
    public GalleryView()
    {
        InitializeComponent();
        DataContext = _model;
        Font = new Font("Segoe UI", 9.5f);
        ApplyPalette();

        _tree = new GalleryTreeBuilder(catalogTree);
        _preview = new GalleryPreviewController(previewHost);
        _selection = new GallerySelection();
        _selection.Attach(previewHost, previewAdorner.AdornerLayer);
        _selection.SelectionChanged += OnPreviewSelectionChanged;

        // The layer's colour-key fill is clipped to a thin outline by GallerySelection, so this only
        // has to match the preview surface around the accent rectangle.
        previewAdorner.AdornerLayer.ColorKey = Theme.Current.Surface;
        _inspector = new GalleryInspector(inspectorRows, inspectorScroll);
        _inspector.Status += OnInspectorStatus;

        _tree.Build(string.Empty);
        UpdateCatalogCount();
        UpdateStatus();

        Theme.Changed += OnThemeChanged;
    }

    /// <summary>The entry currently shown in the preview, or null when nothing is selected.</summary>
    public string? CurrentEntryId => _currentEntry?.Id;

    /// <summary>Selects an entry or a group overview by its stable id.</summary>
    public bool SelectEntry(string id)
    {
        var entry = GalleryCatalog.Find(id);
        if (entry is null)
        {
            return false;
        }

        _tree.TrySelect(entry.Id);
        ShowEntry(entry);
        return true;
    }

    /// <summary>Returns the capturable targets in display order.</summary>
    public IReadOnlyList<GalleryEntry> EnumerateEntries(bool includeGroups)
        => GalleryCatalog.Enumerate(includeGroups).ToList();

    /// <summary>Shows the first entry once the shell has been laid out at its real size.</summary>
    internal void EnsureInitialSelection()
    {
        if (_currentEntry is null && _tree.VisibleLeafCount > 0)
        {
            SelectEntry("layout.stack-panel");
        }
    }

    /// <summary>Removes the accent outline without changing the preview or the inspector.</summary>
    public void ClearSelection() => _selection.ClearVisual();

    /// <summary>Scrolls the current preview to a pixel offset.</summary>
    public void SetPreviewScrollOffset(int offset) => _preview.SetScrollOffset(offset);

    /// <summary>Hides the title strip, side panes and status strip for content-only captures.</summary>
    public void SetChromeVisible(bool visible)
    {
        var visibility = visible
            ? VeloxDev.WinForms.Xaml.Controls.Visibility.Visible
            : VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed;
        titleStrip.Visibility = visibility;
        catalogPane.Visibility = visibility;
        catalogSplitter.Visibility = visibility;
        inspectorPane.Visibility = visibility;
        inspectorSplitter.Visibility = visibility;
        statusBar.Visibility = visibility;
        titleStrip.Visible = visible;
        catalogPane.Visible = visible;
        catalogSplitter.Visible = visible;
        inspectorPane.Visible = visible;
        inspectorSplitter.Visible = visible;
        statusBar.Visible = visible;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
            _inspector.Status -= OnInspectorStatus;
            _selection.SelectionChanged -= OnPreviewSelectionChanged;
            _selection.Dispose();
            _preview.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowEntry(GalleryEntry entry)
    {
        _currentEntry = entry;
        _preview.ShowEntry(entry, _model);

        entryBreadcrumb.Text = entry.IsPage && entry.CardName.Length == 0
            ? $"{entry.Number} · {entry.Title.ToUpperInvariant()} — SECTION OVERVIEW"
            : $"{entry.Number} · {entry.GroupTitle.ToUpperInvariant()} / {entry.Title.ToUpperInvariant()}";
        entryTitle.Text = entry.Title;
        entrySummary.Text = Shorten(entry.Summary, 56);
        entryKindText.Text = entry.IsPage ? "Page" : "Card";
        usageText.Text = entry.Snippet;
        _selectionRoot = _preview.DefaultSelection;

        _selection.SetDefault(_preview.DefaultSelection);
        _selection.TrackScroll(_preview.ScrollHosts);
        _selection.Select(_preview.DefaultSelection);
        UpdateStatus();

        if (entry.IsPage && entry.CardName.Length > 0 && IsHandleCreated)
        {
            var id = entry.Id;
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && _currentEntry?.Id == id)
                {
                    _preview.ScrollToCard();
                    _selection.RefreshAdorner();
                }
            }));
        }
    }

    private void OnCatalogSelectionChanged(object? sender, EventArgs e)
    {
        if (_tree.Selected is not { } item)
        {
            return;
        }

        if (item.Entry is { } entry)
        {
            ShowEntry(entry);
        }
        else if (item.Group is { } group)
        {
            ShowEntry(group.Page);
        }
    }

    private void OnCatalogSearchTextChanged(object? sender, EventArgs e)
    {
        var current = _currentEntry?.Id;
        _tree.Build(catalogSearch.Text);
        UpdateCatalogCount();

        if (current is not null && _tree.Contains(current))
        {
            _tree.TrySelect(current);
        }
        else
        {
            _tree.ClearSelection();
        }
    }

    private void OnPreviewSelectionChanged(object? sender, EventArgs e)
    {
        var selected = _selection.Selected;
        _inspector.Bind(selected, previewHost);
        UpdateInspectorHeader(selected);
        UpdateStatus();
    }

    private void UpdateInspectorHeader(System.Windows.Forms.Control? selected)
    {
        if (selected is null || selected.IsDisposed)
        {
            inspectorTypeText.Text = "No selection";
            inspectorPathText.Text = "Click an element in the preview.";
            return;
        }

        var origin = selected is XamlControl ? "VeloxDev" : "native";
        inspectorTypeText.Text = $"{origin} · {selected.GetType().Name}";
        inspectorPathText.Text = BuildPath(selected);
    }

    private static string Shorten(string text, int maximum)
        => text.Length <= maximum ? text : text[..(maximum - 1)].TrimEnd() + "…";

    private string BuildPath(System.Windows.Forms.Control selected)
    {
        var parts = new Stack<string>();
        for (var current = selected; current is not null && !ReferenceEquals(current, previewHost); current = current.Parent)
        {
            parts.Push(current.GetType().Name);
            if (ReferenceEquals(current, _selectionRoot))
            {
                break;
            }
        }

        return parts.Count == 0 ? selected.GetType().Name : string.Join(" › ", parts);
    }

    private void UpdateStatus()
    {
        statusLeft.Text = $"{_tree.VisibleLeafCount} entries · {GalleryCatalog.Groups.Count} groups";
        statusRight.Text = _currentEntry is null
            ? string.Empty
            : $"{_currentEntry.Number} · {(_selection.Selected?.GetType().Name ?? "—")}";
        titleStatus.Text = $"{GalleryCatalog.Groups.Sum(g => g.Entries.Count)} cards · {GalleryCatalog.Groups.Count} groups";
        UpdateCatalogCount();
    }

    private void UpdateCatalogCount()
    {
        catalogCount.Text = _tree.Filter.Length == 0
            ? $"{_tree.VisibleLeafCount} entries"
            : $"{_tree.VisibleLeafCount} matching entries";
    }

    private void OnInspectorStatus(string message) => inspectorHint.Text = message;

    private void OnPropertySearchTextChanged(object? sender, EventArgs e)
    {
        _inspector.Filter = propertySearch.Text;
        _inspector.Rebuild();
    }

    private void OnAdvancedClick(object? sender, EventArgs e)
    {
        _advanced = !_advanced;
        _inspector.Advanced = _advanced;
        advancedButton.Content = _advanced ? "Show common properties" : "Show all properties";
        _inspector.Rebuild();
    }

    private void OnCopyUsageClick(object? sender, EventArgs e)
    {
        if (usageText.Text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(usageText.Text);
            inspectorHint.Text = "Usage copied to the clipboard.";
        }
        catch (Exception ex)
        {
            inspectorHint.Text = ex.Message;
        }
    }

    private void OnResetPreviewClick(object? sender, EventArgs e)
    {
        if (_currentEntry is not null)
        {
            ShowEntry(_currentEntry);
        }
    }

    private void OnThemeToggleClick(object? sender, EventArgs e)
    {
        Theme.Mode = Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        Application.SetColorMode(Theme.Mode == ThemeMode.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        ApplyPalette();
        Theme.ApplyTo(this);
        ThemeStyles.ApplyTo(this);
        previewAdorner.AdornerLayer.ColorKey = Theme.Current.Surface;
        _selection.RefreshAdorner();
        Invalidate(true);

        // Native controls and the preview sections were built under the previous process colour mode. Rebuild
        // both after the current event finishes so a section button can finish its click handler.
        if (!IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            var current = _currentEntry?.Id;
            _tree.Build(catalogSearch.Text);
            UpdateCatalogCount();
            if (current is not null && !SelectEntry(current))
            {
                SelectEntry("layout.stack-panel");
            }

            _inspector.Rebuild();
            Invalidate(true);
        }));
    }

    // 调色板只落在一个地方：资源键被整体替换，树里的 DynamicResource 订阅会自己刷新。
    private void ApplyPalette()
    {
        var palette = Theme.Current;
        Resources["WindowBrush"] = new SolidColorBrush(palette.WindowBackground);
        Resources["HeaderBrush"] = ColorMix(palette.WindowBackground, palette.SurfaceAlt, 0.55f);
        Resources["SurfaceBrush"] = new SolidColorBrush(palette.Surface);
        Resources["CardBrush"] = ColorMix(palette.Surface, palette.SurfaceAlt, 0.32f);
        Resources["FieldBrush"] = new SolidColorBrush(palette.ControlBackground);
        Resources["BorderBrush"] = new SolidColorBrush(palette.ControlBorder);
        Resources["TextBrush"] = new SolidColorBrush(palette.Foreground);
        Resources["MutedBrush"] = new SolidColorBrush(palette.MutedForeground);
        Resources["AccentBrush"] = new SolidColorBrush(palette.Accent);
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(51, palette.Accent));
        Resources["SelectionBrush"] = new SolidColorBrush(palette.Selection);
        Resources["SuccessBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x3D, 0xD6, 0x8C));
        Resources["WarningBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xB4, 0x4B));
        Resources["DangerBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
        Resources["AccentGradient"] = CreateGradient(palette.Accent, ColorMix(palette.Accent, Color.Magenta, 0.35f));

        themeToggleButton.Content = Theme.Mode == ThemeMode.Dark ? "Light mode" : "Dark mode";
    }

    private static SolidColorBrush ColorMix(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new SolidColorBrush(Color.FromArgb(
            0xFF,
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount)));
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end)
    {
        var brush = new LinearGradientBrush { StartPoint = new PointF(0f, 0f), EndPoint = new PointF(1f, 1f) };
        brush.GradientStops.Add(new GradientStop(start, 0f));
        brush.GradientStops.Add(new GradientStop(end, 1f));
        return brush;
    }
}

/// <summary>The type ramp shared by the shell and every section.</summary>
public static class GalleryTypography
{
    /// <summary>The application title.</summary>
    public static Font Title { get; } = new("Segoe UI Semibold", 19f, FontStyle.Bold);

    /// <summary>The header subtitle.</summary>
    public static Font Subtitle { get; } = new("Segoe UI", 9.75f);

    /// <summary>A section title.</summary>
    public static Font SectionTitle { get; } = new("Segoe UI Semibold", 16f, FontStyle.Bold);

    /// <summary>A card title.</summary>
    public static Font CardTitle { get; } = new("Segoe UI Semibold", 11f, FontStyle.Bold);

    /// <summary>The default body text.</summary>
    public static Font Body { get; } = new("Segoe UI", 9.5f);

    /// <summary>The monospace face used by code samples.</summary>
    public static Font Mono { get; } = new("Cascadia Mono", 9f);

    /// <summary>A compact label used by badges and readouts.</summary>
    public static Font Caption { get; } = new("Segoe UI", 8.75f);
}
