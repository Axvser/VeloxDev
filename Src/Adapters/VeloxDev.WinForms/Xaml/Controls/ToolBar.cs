using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A strip of toolbar items laid out along one axis.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Items"/> accepts controls and plain objects. A control is hosted as a child window and gets its own
/// bounds; any other object is measured as text and painted in the strip. Controls declared directly as children are
/// also added to <see cref="Items"/>, so both markup forms work.
/// </para>
/// <para>
/// The surface and border come from <see cref="Theme.Current"/> unless an explicit background is set.
/// </para>
/// </remarks>
public class ToolBar : Control
{
    /// <summary>The direction items are laid out in; horizontal by default.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(System.Windows.Forms.Orientation),
        typeof(ToolBar),
        new PropertyMetadata(System.Windows.Forms.Orientation.Horizontal, OnLayoutPropertyChanged));

    private const int ItemSpacing = 4;

    private readonly ToolBarItemCollection _items;
    private readonly List<ItemSlot> _textSlots = [];
    private bool _syncingItems;

    /// <summary>Creates the toolbar with the double-buffered, self-painted style it needs.</summary>
    public ToolBar()
    {
        _items = new ToolBarItemCollection(this);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>The toolbar items in strip order.</summary>
    public IList<object> Items => _items;

    /// <summary>Gets or sets the direction items are laid out in.</summary>
    public System.Windows.Forms.Orientation Orientation
    {
        get => (System.Windows.Forms.Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(240, 36);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var horizontal = Orientation == System.Windows.Forms.Orientation.Horizontal;
        var padding = Padding;
        var width = 0;
        var height = 0;
        var any = false;

        foreach (var item in _items)
        {
            var size = MeasureItem(item);
            if (any)
            {
                if (horizontal)
                {
                    width += ItemSpacing;
                }
                else
                {
                    height += ItemSpacing;
                }
            }

            any = true;
            if (horizontal)
            {
                width += size.Width;
                height = Math.Max(height, size.Height);
            }
            else
            {
                height += size.Height;
                width = Math.Max(width, size.Width);
            }
        }

        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        _textSlots.Clear();
        var content = XamlLayout.ContentBounds(this);
        var horizontal = Orientation == System.Windows.Forms.Orientation.Horizontal;
        var x = content.Left;
        var y = content.Top;

        foreach (var item in _items)
        {
            if (item is System.Windows.Forms.Control child)
            {
                if (IsCollapsed(child))
                {
                    continue;
                }

                var available = horizontal
                    ? new Size(0, content.Height)
                    : new Size(content.Width, 0);
                var measured = XamlLayout.Measure(child, available);
                var width = child.Width > 0 ? child.Width : measured.Width;
                var height = child.Height > 0 ? child.Height : measured.Height;

                Rectangle bounds;
                if (horizontal)
                {
                    var top = content.Top + Math.Max(0, (content.Height - height) / 2);
                    bounds = new Rectangle(x, top, Math.Max(0, width), Math.Max(0, height));
                    x += Math.Max(0, width) + ItemSpacing;
                }
                else
                {
                    var left = content.Left + Math.Max(0, (content.Width - width) / 2);
                    bounds = new Rectangle(left, y, Math.Max(0, width), Math.Max(0, height));
                    y += Math.Max(0, height) + ItemSpacing;
                }

                if (child.Bounds != bounds)
                {
                    child.Bounds = bounds;
                }

                continue;
            }

            if (item is null)
            {
                continue;
            }

            var measuredText = MeasureItem(item);
            Rectangle textBounds;
            if (horizontal)
            {
                var top = content.Top + Math.Max(0, (content.Height - measuredText.Height) / 2);
                textBounds = new Rectangle(x, top, measuredText.Width, measuredText.Height);
                x += measuredText.Width + ItemSpacing;
            }
            else
            {
                var left = content.Left + Math.Max(0, (content.Width - measuredText.Width) / 2);
                textBounds = new Rectangle(left, y, measuredText.Width, measuredText.Height);
                y += measuredText.Height + ItemSpacing;
            }

            _textSlots.Add(new ItemSlot(item, textBounds));
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var theme = Theme.Current;
        var background = HasXamlValue(BackgroundProperty) ? Background : new SolidColorBrush(theme.Surface);
        XamlDrawing.PaintChromeWithBrushes(
            e.Graphics,
            ClientRectangle,
            background,
            theme.ControlBorder,
            new Thickness(1f),
            theme.CornerRadius);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsPainted || _textSlots.Count == 0)
        {
            return;
        }

        var foreground = HasXamlValue(ForegroundProperty) ? EffectiveForeground : Theme.Current.Foreground;
        foreach (var slot in _textSlots)
        {
            TextRenderer.DrawText(
                e.Graphics,
                slot.Item.ToString() ?? string.Empty,
                Font,
                slot.Bounds,
                foreground,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_syncingItems || e.Control is null || _items.Contains(e.Control))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _items.Add(e.Control);
        }
        finally
        {
            _syncingItems = false;
        }

        PerformLayout();
    }

    /// <inheritdoc />
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (_syncingItems || e.Control is null || !_items.Contains(e.Control))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _items.Remove(e.Control);
        }
        finally
        {
            _syncingItems = false;
        }

        PerformLayout();
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
            _syncingItems = true;
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    internal void OnItemInserted(object? item)
    {
        if (item is System.Windows.Forms.Control control && !_syncingItems && !Controls.Contains(control))
        {
            _syncingItems = true;
            try
            {
                Controls.Add(control);
            }
            finally
            {
                _syncingItems = false;
            }
        }

        PerformLayout();
        Invalidate();
    }

    internal void OnItemRemoved(object? item)
    {
        if (item is System.Windows.Forms.Control control && !_syncingItems && Controls.Contains(control))
        {
            _syncingItems = true;
            try
            {
                Controls.Remove(control);
            }
            finally
            {
                _syncingItems = false;
            }
        }

        PerformLayout();
        Invalidate();
    }

    private Size MeasureItem(object? item)
    {
        if (item is System.Windows.Forms.Control child)
        {
            var measured = XamlLayout.Measure(child, Size.Empty);
            return new Size(
                child.Width > 0 ? child.Width : measured.Width,
                child.Height > 0 ? child.Height : measured.Height);
        }

        var text = item?.ToString() ?? string.Empty;
        var size = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
        return new Size(size.Width + 8, size.Height + 4);
    }

    private static bool IsCollapsed(System.Windows.Forms.Control child)
        => child is Control xaml
            ? xaml.Visibility == Visibility.Collapsed
            : !child.Visible;

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ToolBar toolbar && !toolbar.IsDisposed)
        {
            toolbar.PerformLayout();
            toolbar.Parent?.PerformLayout();
            toolbar.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    private sealed class ItemSlot(object item, Rectangle bounds)
    {
        public object Item { get; } = item;

        public Rectangle Bounds { get; } = bounds;
    }

    // 集合本身负责让条目控件进出 Controls；OnControlAdded/Removed 负责反方向的同步。
    private sealed class ToolBarItemCollection(ToolBar owner) : Collection<object>
    {
        protected override void InsertItem(int index, object item)
        {
            base.InsertItem(index, item);
            owner.OnItemInserted(item);
        }

        protected override void RemoveItem(int index)
        {
            var item = this[index];
            base.RemoveItem(index);
            owner.OnItemRemoved(item);
        }

        protected override void SetItem(int index, object item)
        {
            var previous = this[index];
            base.SetItem(index, item);
            owner.OnItemRemoved(previous);
            owner.OnItemInserted(item);
        }

        protected override void ClearItems()
        {
            var previous = this.ToArray();
            base.ClearItems();
            foreach (var item in previous)
            {
                owner.OnItemRemoved(item);
            }
        }
    }
}

/// <summary>
/// Arranges a row of <see cref="ToolBar"/> controls.
/// </summary>
/// <remarks>Toolbars declared directly as children are also added to <see cref="ToolBars"/>.</remarks>
public class ToolBarTray : Control
{
    private const int TraySpacing = 6;

    private readonly ToolBarCollection _toolBars;
    private bool _syncingItems;

    /// <summary>Creates the tray with double buffering.</summary>
    public ToolBarTray()
    {
        _toolBars = new ToolBarCollection(this);
        DoubleBuffered = true;
    }

    /// <summary>The toolbars in tray order.</summary>
    public IList<ToolBar> ToolBars => _toolBars;

    /// <inheritdoc />
    protected override Size DefaultSize => new(240, 40);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        var width = 0;
        var height = 0;
        var any = false;

        foreach (var toolbar in _toolBars)
        {
            if (toolbar is null)
            {
                continue;
            }

            var measured = XamlLayout.Measure(toolbar, new Size(0, proposedSize.Height));
            if (any)
            {
                width += TraySpacing;
            }

            any = true;
            width += toolbar.Width > 0 ? toolbar.Width : measured.Width;
            height = Math.Max(height, toolbar.Height > 0 ? toolbar.Height : measured.Height);
        }

        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = XamlLayout.ContentBounds(this);
        var x = content.Left;
        foreach (var toolbar in _toolBars)
        {
            if (toolbar is null || toolbar.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var measured = XamlLayout.Measure(toolbar, new Size(0, content.Height));
            var width = toolbar.Width > 0 ? toolbar.Width : measured.Width;
            var bounds = new Rectangle(
                x,
                content.Top,
                Math.Max(0, width),
                Math.Max(0, content.Height));
            if (toolbar.Bounds != bounds)
            {
                toolbar.Bounds = bounds;
            }

            x += Math.Max(0, width) + TraySpacing;
        }
    }

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_syncingItems || e.Control is not ToolBar toolbar || _toolBars.Contains(toolbar))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _toolBars.Add(toolbar);
        }
        finally
        {
            _syncingItems = false;
        }

        PerformLayout();
    }

    /// <inheritdoc />
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (_syncingItems || e.Control is not ToolBar toolbar || !_toolBars.Contains(toolbar))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _toolBars.Remove(toolbar);
        }
        finally
        {
            _syncingItems = false;
        }

        PerformLayout();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _syncingItems = true;
        }

        base.Dispose(disposing);
    }

    private void OnToolBarInserted(ToolBar? toolbar)
    {
        if (toolbar is null)
        {
            return;
        }

        if (!_syncingItems && !Controls.Contains(toolbar))
        {
            _syncingItems = true;
            try
            {
                Controls.Add(toolbar);
            }
            finally
            {
                _syncingItems = false;
            }
        }

        PerformLayout();
    }

    private void OnToolBarRemoved(ToolBar? toolbar)
    {
        if (toolbar is null)
        {
            return;
        }

        if (!_syncingItems && Controls.Contains(toolbar))
        {
            _syncingItems = true;
            try
            {
                Controls.Remove(toolbar);
            }
            finally
            {
                _syncingItems = false;
            }
        }

        PerformLayout();
    }

    // 集合负责把 toolbar 挂成子窗口；直接声明在 XAML 里的 toolbar 由 OnControlAdded 反向补进集合。
    private sealed class ToolBarCollection(ToolBarTray owner) : Collection<ToolBar>
    {
        protected override void InsertItem(int index, ToolBar item)
        {
            base.InsertItem(index, item);
            owner.OnToolBarInserted(item);
        }

        protected override void RemoveItem(int index)
        {
            var item = this[index];
            base.RemoveItem(index);
            owner.OnToolBarRemoved(item);
        }

        protected override void SetItem(int index, ToolBar item)
        {
            var previous = this[index];
            base.SetItem(index, item);
            owner.OnToolBarRemoved(previous);
            owner.OnToolBarInserted(item);
        }

        protected override void ClearItems()
        {
            var previous = this.ToArray();
            base.ClearItems();
            foreach (var toolbar in previous)
            {
                owner.OnToolBarRemoved(toolbar);
            }
        }
    }
}
