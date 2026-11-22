using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>One row in a <see cref="TreeView"/>: a header, an expanded state and child items.</summary>
/// <remarks>
/// The item is painted as a row by its owning <see cref="TreeView"/> and never creates a window. Its
/// <see cref="Bounds"/> start at the depth indentation; the tree draws the selection and hover bands across the full
/// row before the item paints itself.
/// </remarks>
public class TreeViewItem : VisualElement
{
    /// <summary>The text shown in the row.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(TreeViewItem),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether the child items are shown.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(TreeViewItem),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The font used to draw the header, or <see langword="null"/> to use the owning tree's font.</summary>
    public static readonly DependencyProperty FontProperty = DependencyProperty.Register(
        nameof(Font), typeof(Font), typeof(TreeViewItem),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The colour of the header text; <see cref="Color.Empty"/> uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Color), typeof(TreeViewItem),
        new PropertyMetadata(Color.Empty) { AffectsRender = true });

    /// <summary>Creates a collapsed item with no children.</summary>
    public TreeViewItem()
    {
        IsHitTestVisible = false;
        Padding = new XamlThickness(18f, 5f, 8f, 5f);
    }

    /// <summary>The text shown in the row.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty)!;
        set => SetValue(HeaderProperty, value ?? string.Empty);
    }

    /// <summary>Whether the child items are shown.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The font used to draw the header, or <see langword="null"/> to use the owning tree's font.</summary>
    public Font? Font
    {
        get => (Font?)GetValue(FontProperty);
        set => SetValue(FontProperty, value);
    }

    /// <summary>The colour of the header text; <see cref="Color.Empty"/> uses the active theme.</summary>
    public Color Foreground
    {
        get => (Color)GetValue(ForegroundProperty)!;
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The child rows.</summary>
    public ObservableCollection<TreeViewItem> Items { get; } = [];

    /// <summary>The owning row, or <see langword="null"/> for a root.</summary>
    public TreeViewItem? ParentItem { get; internal set; }

    /// <summary>The nesting depth; assigned by the owning tree.</summary>
    public int Depth { get; internal set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var font = Font ?? (ParentElement as TreeView)?.Font ?? DefaultFont;
        var measured = VisualText.Measure(Header, font, 0f, wrap: false, RenderDpi);
        return new Size(
            (int)Math.Ceiling(measured.Width + Padding.Horizontal),
            (int)Math.Ceiling(measured.Height + Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var expanderX = Padding.Left - 12f;
        var centerY = Bounds.Height / 2f;
        if (Items.Count > 0)
        {
            using var brush = new SolidBrush(theme.MutedForeground);
            var points = IsExpanded
                ? new[]
                {
                    new PointF(expanderX - 1f, centerY - 2f),
                    new PointF(expanderX + 5f, centerY - 2f),
                    new PointF(expanderX + 2f, centerY + 2f),
                }
                : new[]
                {
                    new PointF(expanderX, centerY - 4f),
                    new PointF(expanderX + 4f, centerY),
                    new PointF(expanderX, centerY + 4f),
                };
            graphics.FillPolygon(brush, points);
        }

        if (string.IsNullOrEmpty(Header))
        {
            return;
        }

        var font = Font ?? (ParentElement as TreeView)?.Font ?? DefaultFont;
        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0, (int)Math.Ceiling(Bounds.Width - Padding.Horizontal)),
            Math.Max(0, (int)Math.Ceiling(Bounds.Height - Padding.Vertical)));
        VisualText.Draw(this,
            graphics,
            Header,
            font,
            Foreground.IsEmpty ? theme.Foreground : Foreground,
            content,
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);
    }
}

/// <summary>A hierarchical list of <see cref="TreeViewItem"/> rows with selection, expansion and keyboard navigation.</summary>
/// <remarks>
/// <para>
/// Only expanded rows are realized as children; the tree keeps a flat row order for arrangement and input. Put it in
/// a <see cref="ScrollViewer"/> for long directories. Expansion, selection and collection changes rebuild that flat
/// order and invalidate the measure.
/// </para>
/// <para>
/// The tree owns the row visuals it shows: an item can be collapsed and shown again, but it should not be shared by
/// two trees at the same time.
/// </para>
/// </remarks>
public class TreeView : Panel
{
    /// <summary>The horizontally selected row, or <see langword="null"/>.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(TreeViewItem), typeof(TreeView),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>The indentation added per nesting level, in pixels.</summary>
    public static readonly DependencyProperty IndentProperty = DependencyProperty.Register(
        nameof(Indent), typeof(double), typeof(TreeView),
        new PropertyMetadata(16d) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The font used by rows that do not set one.</summary>
    public static readonly DependencyProperty FontProperty = DependencyProperty.Register(
        nameof(Font), typeof(Font), typeof(TreeView),
        new PropertyMetadata(DefaultFont) { AffectsMeasure = true, AffectsRender = true });

    private readonly List<TreeViewItem> _rows = [];
    private readonly HashSet<TreeViewItem> _observing = [];
    private TreeViewItem? _hover;
    private bool _rebuilding;

    /// <summary>Creates a focusable tree with the current theme font.</summary>
    public TreeView()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(0f);
        Items.CollectionChanged += OnRootItemsChanged;
    }

    /// <summary>Raised after <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The root rows.</summary>
    public ObservableCollection<TreeViewItem> Items { get; } = [];

    /// <summary>The selected row, or <see langword="null"/>.</summary>
    public TreeViewItem? SelectedItem
    {
        get => (TreeViewItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>The indentation added per nesting level, in pixels.</summary>
    public double Indent
    {
        get => (double)GetValue(IndentProperty)!;
        set => SetValue(IndentProperty, value);
    }

    /// <summary>The font used by rows that do not set one.</summary>
    public Font Font
    {
        get => (Font)GetValue(FontProperty)!;
        set
        {
            if (value is not null)
            {
                SetValue(FontProperty, value);
            }
        }
    }

    /// <summary>The realized rows in display order.</summary>
    public IReadOnlyList<TreeViewItem> Rows => _rows;

    /// <summary>Expands every ancestor of <paramref name="item"/> and selects it.</summary>
    /// <param name="item">The row to reveal.</param>
    public void Reveal(TreeViewItem item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        for (var parent = item.ParentItem; parent is not null; parent = parent.ParentItem)
        {
            parent.IsExpanded = true;
        }

        Rebuild();
        SelectedItem = item;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0;
        var height = 0;
        foreach (var row in _rows)
        {
            var measured = row.Measure(new Size(availableSize.Width, 0));
            width = Math.Max(width, measured.Width);
            height += measured.Height;
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var y = 0f;
        foreach (var row in _rows)
        {
            var indent = (float)(Math.Max(0, row.Depth) * Math.Max(0, Indent));
            var height = row.DesiredSize.Height;
            row.Arrange(new RectangleF(indent, y, Math.Max(0f, finalSize.Width - indent), height));
            y += height;
        }
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        base.RenderContent(graphics);
        var theme = Theme.Current;
        foreach (var row in _rows)
        {
            var band = new RectangleF(0f, row.Bounds.Y, Bounds.Width, row.Bounds.Height);
            if (ReferenceEquals(row, SelectedItem))
            {
                using var selected = new SolidBrush(theme.Selection);
                graphics.FillRectangle(selected, band);
            }
            else if (ReferenceEquals(row, _hover))
            {
                using var hover = new SolidBrush(theme.SurfaceAlt);
                graphics.FillRectangle(hover, band);
            }
        }

        if (IsFocused && SelectedItem is { } selectedRow)
        {
            using var focus = new Pen(theme.FocusRing, 1f);
            graphics.DrawRectangle(
                focus,
                0.5f,
                selectedRow.Bounds.Y + 0.5f,
                Math.Max(0f, Bounds.Width - 1f),
                Math.Max(0f, selectedRow.Bounds.Height - 1f));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var local = args.GetPosition(this);
        var row = RowAt(local.Y);
        if (row is null)
        {
            return;
        }

        Focus();
        var expanderLeft = (float)(Math.Max(0, row.Depth) * Math.Max(0, Indent));
        if (row.Items.Count > 0 && local.X >= expanderLeft - 4f && local.X <= expanderLeft + 16f)
        {
            row.IsExpanded = !row.IsExpanded;
        }
        else
        {
            SelectedItem = row;
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var row = RowAt(args.GetPosition(this).Y);
        if (!ReferenceEquals(row, _hover))
        {
            _hover = row;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        if (_hover is not null)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled || _rows.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : _rows.IndexOf(SelectedItem);
        switch (args.KeyCode)
        {
            case Keys.Down:
                SelectIndex(index + 1);
                args.Handled = true;
                break;
            case Keys.Up:
                SelectIndex(index < 0 ? 0 : index - 1);
                args.Handled = true;
                break;
            case Keys.Home:
                SelectIndex(0);
                args.Handled = true;
                break;
            case Keys.End:
                SelectIndex(_rows.Count - 1);
                args.Handled = true;
                break;
            case Keys.Right when SelectedItem is { } right:
                if (right.Items.Count > 0 && !right.IsExpanded)
                {
                    right.IsExpanded = true;
                }
                else if (right.Items.Count > 0)
                {
                    SelectedItem = right.Items[0];
                }

                args.Handled = true;
                break;
            case Keys.Left when SelectedItem is { } left:
                if (left.IsExpanded)
                {
                    left.IsExpanded = false;
                }
                else if (left.ParentItem is { } parent)
                {
                    SelectedItem = parent;
                }

                args.Handled = true;
                break;
            case Keys.Enter when SelectedItem is { } enter && enter.Items.Count > 0:
                enter.IsExpanded = !enter.IsExpanded;
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, SelectedItemProperty))
        {
            if (e.NewValue is TreeViewItem next)
            {
                ScrollIntoView(next);
            }

            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Items.CollectionChanged -= OnRootItemsChanged;
            foreach (var item in _observing)
            {
                Observe(item, subscribe: false);
            }

            _observing.Clear();
        }

        base.Dispose(disposing);
    }

    private void OnRootItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnItemChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.Property == TreeViewItem.IsExpandedProperty || e.Property == TreeViewItem.HeaderProperty)
        {
            Rebuild();
        }
    }

    private void OnItemItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is TreeViewItem owner)
        {
            ObserveChildren(owner);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            foreach (var item in Items)
            {
                Observe(item, subscribe: true);
            }

            _rows.Clear();
            foreach (var item in Items)
            {
                Flatten(item, 0);
            }

            // 行视觉归树所有：先把上一轮的行摘掉，再按新的可见顺序挂回去。
            foreach (var child in Children.ToArray())
            {
                if (child is TreeViewItem old)
                {
                    RemoveChild(old);
                }
            }

            foreach (var row in _rows)
            {
                Add(row);
            }
        }
        finally
        {
            _rebuilding = false;
        }

        InvalidateMeasure();
        if (SelectedItem is { } selected && !_rows.Contains(selected))
        {
            SelectedItem = null;
        }
    }

    private void Flatten(TreeViewItem item, int depth)
    {
        item.Depth = depth;
        _rows.Add(item);
        if (!item.IsExpanded)
        {
            return;
        }

        foreach (var child in item.Items)
        {
            child.ParentItem = item;
            Observe(child, subscribe: true);
            Flatten(child, depth + 1);
        }
    }

    private void Observe(TreeViewItem item, bool subscribe)
    {
        if (subscribe)
        {
            if (!_observing.Add(item))
            {
                return;
            }

            item.PropertyChanged += OnItemChanged;
            item.Items.CollectionChanged += OnItemItemsChanged;
            return;
        }

        item.PropertyChanged -= OnItemChanged;
        item.Items.CollectionChanged -= OnItemItemsChanged;
        _observing.Remove(item);
        foreach (var child in item.Items)
        {
            Observe(child, subscribe: false);
        }
    }

    private void ObserveChildren(TreeViewItem item)
    {
        foreach (var child in item.Items)
        {
            Observe(child, subscribe: true);
        }
    }

    private TreeViewItem? RowAt(float y)
    {
        foreach (var row in _rows)
        {
            if (y >= row.Bounds.Y && y < row.Bounds.Bottom)
            {
                return row;
            }
        }

        return null;
    }

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        SelectedItem = _rows[index];
    }

    private void ScrollIntoView(TreeViewItem item)
    {
        for (var current = ParentElement; current is not null; current = current.ParentElement)
        {
            if (current is ScrollViewer viewer)
            {
                viewer.ScrollIntoView(item);
                return;
            }
        }
    }
}
