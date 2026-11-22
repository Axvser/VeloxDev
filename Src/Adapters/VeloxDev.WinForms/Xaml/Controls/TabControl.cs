using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// One tab of a <see cref="TabControl"/>: a header drawn by the host and a single content child.
/// </summary>
/// <remarks>
/// The item is meaningful inside a <see cref="TabControl"/>; the host paints the header, lays the item out in the
/// content area and shows only the selected item. A standalone <see cref="TabItem"/> behaves as a plain content control.
/// </remarks>
public class TabItem : ContentControl
{
    /// <summary>The text shown in the tab header.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(TabItem),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    /// <summary>Whether this tab is the selected one; the host keeps exactly one item selected.</summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(TabItem),
        new PropertyMetadata(false, OnIsSelectedPropertyChanged));

    /// <summary>Gets or sets the text shown in the tab header.</summary>
    public string Header
    {
        get => (string?)GetValue(HeaderProperty) ?? string.Empty;
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Gets or sets whether this tab is the selected one.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    // 宿主；离开集合后为 null。
    internal TabControl? Owner { get; set; }

    internal void PaintHeader(Graphics graphics, Rectangle bounds, bool selected, bool hover)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var theme = Theme.Current;
        var background = selected
            ? theme.Surface
            : hover
                ? theme.ControlHover
                : Color.Transparent;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (background.A > 0)
        {
            using var path = XamlDrawing.RoundedRectangle(
                new RectangleF(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                theme.CornerRadius);
            using var brush = new SolidBrush(background);
            graphics.FillPath(brush, path);
        }

        var foreground = selected || hover ? theme.Foreground : theme.MutedForeground;
        var textBounds = new Rectangle(bounds.Left + 12, bounds.Top, Math.Max(0, bounds.Width - 24), bounds.Height);
        TextRenderer.DrawText(
            graphics,
            Header,
            Font,
            textBounds,
            foreground,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (selected)
        {
            using var accent = new SolidBrush(theme.Accent);
            graphics.FillRectangle(accent, bounds.Left + 10, bounds.Bottom - 2, Math.Max(0, bounds.Width - 20), 2);
        }
    }

    private static void OnHeaderPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TabItem item && !item.IsDisposed)
        {
            item.Owner?.OnItemHeaderChanged();
            item.Invalidate();
        }
    }

    private static void OnIsSelectedPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TabItem item && !item.IsDisposed)
        {
            item.Owner?.OnItemSelectionChanged(item);
            item.Invalidate();
        }
    }
}

/// <summary>
/// Shows one <see cref="TabItem"/> at a time with a horizontal header strip, like a WPF tab control.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Items"/> holds the tabs. A tab declared directly as a child element is also added to the collection, so
/// both the property-element form and direct children work. The first tab is selected when the collection is first
/// populated.
/// </para>
/// <para>
/// The header strip, selection underline and body colours come from <see cref="Theme.Current"/>.
/// </para>
/// </remarks>
public class TabControl : Control
{
    /// <summary>The zero-based index of the selected tab, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(TabControl),
        new PropertyMetadata(-1, OnSelectedIndexPropertyChanged, CoerceSelectedIndex));

    /// <summary>The selected tab, or null when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(TabItem),
        typeof(TabControl),
        new PropertyMetadata(null, OnSelectedItemPropertyChanged));

    private const int HeaderHeight = 32;

    private bool _syncingItems;
    private bool _syncingSelection;
    private bool _selectionInitialized;
    private int _hoverIndex = -1;

    /// <summary>Creates the tab control with the double-buffered, self-painted style it needs.</summary>
    public TabControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        ((TabItemCollection)Items).Owner = this;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> or <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The tabs, in header order.</summary>
    public IList<TabItem> Items { get; } = new TabItemCollection();

    /// <summary>Gets or sets the zero-based index of the selected tab, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Gets or sets the selected tab, or null for none.</summary>
    public TabItem? SelectedItem
    {
        get => (TabItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        var horizontalPadding = (int)Math.Ceiling(padding.Horizontal);
        var verticalPadding = (int)Math.Ceiling(padding.Vertical);
        var headerWidth = 0;
        var contentWidth = 0;
        var contentHeight = 0;
        var available = new Size(
            Math.Max(0, proposedSize.Width - horizontalPadding),
            Math.Max(0, proposedSize.Height - HeaderHeight - verticalPadding));

        foreach (var item in Items)
        {
            if (item is null)
            {
                continue;
            }

            headerWidth += GetHeaderWidth(item);
            var measured = XamlLayout.Measure(item, available);
            contentWidth = Math.Max(contentWidth, measured.Width);
            contentHeight = Math.Max(contentHeight, measured.Height);
        }

        return new Size(
            Math.Max(headerWidth, contentWidth) + horizontalPadding,
            HeaderHeight + contentHeight + verticalPadding);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = DisplayRectangle;
        var headerHeight = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        var area = new Rectangle(
            content.Left,
            content.Top + headerHeight,
            content.Width,
            Math.Max(0, content.Height - headerHeight));

        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            if (item is null)
            {
                continue;
            }

            var selected = index == SelectedIndex;
            var visible = selected && item.Visibility == Visibility.Visible;

            // Visible 的 getter 会叠加父链状态；这里必须按本控件自己的取舍写入，否则父窗体不可见时永远读回 false。
            item.Visible = visible;
            if (visible && item.Bounds != area)
            {
                item.Bounds = area;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var theme = Theme.Current;
        var background = HasXamlValue(BackgroundProperty) && Background is { } custom
            ? custom.ApproximateColor
            : theme.Surface;
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, ClientRectangle);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsPainted)
        {
            return;
        }

        var theme = Theme.Current;
        var content = DisplayRectangle;
        var headerHeight = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        if (headerHeight <= 0)
        {
            return;
        }

        var strip = new Rectangle(content.Left, content.Top, content.Width, headerHeight);
        using (var stripBrush = new SolidBrush(theme.SurfaceAlt))
        {
            e.Graphics.FillRectangle(stripBrush, strip);
        }

        using (var separator = new Pen(theme.ControlBorder))
        {
            e.Graphics.DrawLine(separator, strip.Left, strip.Bottom - 1, strip.Right, strip.Bottom - 1);
        }

        var bounds = GetHeaderBounds();
        for (var index = 0; index < bounds.Count && index < Items.Count; index++)
        {
            var item = Items[index];
            if (item is null)
            {
                continue;
            }

            item.PaintHeader(e.Graphics, bounds[index], index == SelectedIndex, index == _hoverIndex);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled)
        {
            return;
        }

        var index = HitTestHeader(e.Location);
        if (index < 0)
        {
            return;
        }

        Focus();
        SelectedIndex = index;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = HitTestHeader(e.Location);
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Focused || !IsEnabled || Items.Count == 0)
        {
            return;
        }

        if (e.KeyCode == Keys.Right)
        {
            SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left)
        {
            SelectedIndex = Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_syncingItems || e.Control is not TabItem item || Items.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            Items.Add(item);
        }
        finally
        {
            _syncingItems = false;
        }
    }

    /// <inheritdoc />
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (_syncingItems || e.Control is not TabItem item || !Items.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            Items.Remove(item);
        }
        finally
        {
            _syncingItems = false;
        }
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        if (ReferenceEquals(property, FontProperty) && !IsDisposed)
        {
            PerformLayout();
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 基类销毁子控件会触发 Controls 变更；先把两条同步路径关上，避免在销毁途中重排或改选中项。
            _syncingItems = true;
            _syncingSelection = true;
            Theme.Changed -= OnThemeChanged;
            foreach (var item in Items)
            {
                if (item is not null)
                {
                    item.Owner = null;
                }
            }
        }

        base.Dispose(disposing);
    }

    internal void OnItemHeaderChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        PerformLayout();
        Invalidate();
    }

    internal void OnItemSelectionChanged(TabItem item)
    {
        if (_syncingSelection || IsDisposed || item is null)
        {
            return;
        }

        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (item.IsSelected)
        {
            if (SelectedIndex != index)
            {
                SetValue(SelectedIndexProperty, index);
            }
        }
        else if (SelectedIndex == index)
        {
            SetValue(SelectedIndexProperty, -1);
        }
    }

    private void OnItemInserted(TabItem item)
    {
        if (item is null)
        {
            return;
        }

        item.Owner = this;
        AddHostedItem(item);

        if (!_selectionInitialized)
        {
            _selectionInitialized = true;
            if (SelectedIndex < 0)
            {
                SetValue(SelectedIndexProperty, 0);
                return;
            }
        }

        if (SelectedIndex >= Items.Count)
        {
            SetValue(SelectedIndexProperty, Math.Max(0, Items.Count - 1));
            return;
        }

        ApplySelection();
    }

    private void OnItemRemoved(TabItem item)
    {
        RemoveHostedItem(item);
        if (Items.Count == 0)
        {
            _selectionInitialized = false;
            if (SelectedIndex != -1)
            {
                SetValue(SelectedIndexProperty, -1);
                return;
            }
        }
        else if (SelectedIndex >= Items.Count)
        {
            SetValue(SelectedIndexProperty, Items.Count - 1);
            return;
        }

        ApplySelection();
    }

    private void OnItemReplaced(TabItem oldItem, TabItem newItem)
    {
        RemoveHostedItem(oldItem);
        OnItemInserted(newItem);
    }

    private void OnItemsCleared(TabItem[] oldItems)
    {
        foreach (var item in oldItems)
        {
            RemoveHostedItem(item);
        }

        _selectionInitialized = false;
        if (SelectedIndex != -1)
        {
            SetValue(SelectedIndexProperty, -1);
            return;
        }

        ApplySelection();
    }

    private void AddHostedItem(TabItem item)
    {
        if (Controls.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            Controls.Add(item);
        }
        finally
        {
            _syncingItems = false;
        }
    }

    private void RemoveHostedItem(TabItem item)
    {
        if (item is null)
        {
            return;
        }

        item.Owner = null;
        if (!Controls.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            Controls.Remove(item);
        }
        finally
        {
            _syncingItems = false;
        }
    }

    private void ApplySelection()
    {
        if (_syncingSelection || IsDisposed)
        {
            return;
        }

        var index = SelectedIndex;
        if (index < -1)
        {
            index = -1;
        }

        if (index >= Items.Count)
        {
            index = Items.Count - 1;
        }

        var selected = index >= 0 && index < Items.Count ? Items[index] : null;

        // DP 回调在值已经写入之后才到达，不能拿前后的属性值判断；看各个条目的 IsSelected 是否真的会变。
        var changed = SelectedIndex != index || !ReferenceEquals(SelectedItem, selected);
        for (var current = 0; current < Items.Count; current++)
        {
            var item = Items[current];
            if (item is not null && item.IsSelected != ReferenceEquals(item, selected))
            {
                changed = true;
                break;
            }
        }

        _syncingSelection = true;
        try
        {
            for (var current = 0; current < Items.Count; current++)
            {
                var item = Items[current];
                if (item is null)
                {
                    continue;
                }

                var isSelected = ReferenceEquals(item, selected);
                if (item.IsSelected != isSelected)
                {
                    item.IsSelected = isSelected;
                }
            }

            if (SelectedIndex != index)
            {
                SetValue(SelectedIndexProperty, index);
            }

            if (!ReferenceEquals(SelectedItem, selected))
            {
                SetValue(SelectedItemProperty, selected);
            }

            PerformLayout();
            Invalidate();
        }
        finally
        {
            _syncingSelection = false;
        }

        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<Rectangle> GetHeaderBounds()
    {
        var content = DisplayRectangle;
        var headerHeight = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        var bounds = new List<Rectangle>(Items.Count);
        var x = content.Left;
        foreach (var item in Items)
        {
            if (item is null)
            {
                bounds.Add(Rectangle.Empty);
                continue;
            }

            var width = Math.Min(GetHeaderWidth(item), Math.Max(0, content.Right - x));
            bounds.Add(new Rectangle(x, content.Top, Math.Max(0, width), Math.Max(0, headerHeight)));
            x += Math.Max(0, width);
        }

        return bounds;
    }

    private int GetHeaderWidth(TabItem item)
    {
        var measured = TextRenderer.MeasureText(
            item.Header,
            item.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix);
        return Math.Max(64, measured.Width + 24);
    }

    private int HitTestHeader(Point location)
    {
        var bounds = GetHeaderBounds();
        for (var index = 0; index < bounds.Count; index++)
        {
            if (bounds[index].Contains(location))
            {
                return index;
            }
        }

        return -1;
    }

    private static object? CoerceSelectedIndex(System.Windows.Forms.Control target, object? baseValue)
    {
        if (target is not TabControl control || baseValue is not int index)
        {
            return baseValue;
        }

        if (control.Items.Count == 0)
        {
            return -1;
        }

        if (index < -1)
        {
            return -1;
        }

        return Math.Min(index, control.Items.Count - 1);
    }

    private static void OnSelectedIndexPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TabControl control && !control.IsDisposed)
        {
            control.ApplySelection();
        }
    }

    private static void OnSelectedItemPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TabControl control || control.IsDisposed || control._syncingSelection)
        {
            return;
        }

        var item = e.NewValue as TabItem;
        var index = item is null ? -1 : control.Items.IndexOf(item);
        if (index < 0)
        {
            index = -1;
        }

        if (control.SelectedIndex != index)
        {
            control.SetValue(SelectedIndexProperty, index);
            return;
        }

        control.ApplySelection();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    // 集合本身负责与宿主同步：新增的页要成为子窗口，移除的页要离开控件树。
    private sealed class TabItemCollection : Collection<TabItem>
    {
        internal TabControl? Owner { get; set; }

        protected override void InsertItem(int index, TabItem item)
        {
            base.InsertItem(index, item);
            Owner?.OnItemInserted(item);
        }

        protected override void RemoveItem(int index)
        {
            var item = this[index];
            base.RemoveItem(index);
            Owner?.OnItemRemoved(item);
        }

        protected override void SetItem(int index, TabItem item)
        {
            var previous = this[index];
            base.SetItem(index, item);
            Owner?.OnItemReplaced(previous, item);
        }

        protected override void ClearItems()
        {
            var previous = this.ToArray();
            base.ClearItems();
            Owner?.OnItemsCleared(previous);
        }
    }
}
