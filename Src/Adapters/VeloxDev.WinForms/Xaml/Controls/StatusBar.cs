using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// One item of a <see cref="StatusBar"/>: content shown inside the bar, or a vertical separator.
/// </summary>
/// <remarks>
/// The item behaves as a normal <see cref="ContentControl"/>. When <see cref="IsSeparator"/> is set it paints a
/// vertical rule instead, and the bar gives it a narrow fixed slot; a separator should not also carry content.
/// </remarks>
public class StatusBarItem : ContentControl
{
    /// <summary>Whether this item is a vertical separator instead of content.</summary>
    public static readonly DependencyProperty IsSeparatorProperty = DependencyProperty.Register(
        nameof(IsSeparator),
        typeof(bool),
        typeof(StatusBarItem),
        new PropertyMetadata(false));

    /// <summary>Creates the item with centred content and the double-buffered paint style it needs.</summary>
    public StatusBarItem()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets whether this item is a vertical separator.</summary>
    public bool IsSeparator
    {
        get => (bool)GetValue(IsSeparatorProperty)!;
        set => SetValue(IsSeparatorProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsPainted)
        {
            return;
        }

        if (!IsSeparator)
        {
            base.OnPaint(e);
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var x = bounds.Left + bounds.Width / 2;
        var top = bounds.Top + 4;
        var bottom = Math.Max(top, bounds.Bottom - 4);
        using var pen = new Pen(Theme.Current.ControlBorder);
        e.Graphics.DrawLine(pen, x, top, x, bottom);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}

/// <summary>
/// A horizontal strip of <see cref="StatusBarItem"/> controls.
/// </summary>
/// <remarks>
/// Items declared directly as children are also added to <see cref="Items"/>. The surface and border come from
/// <see cref="Theme.Current"/> unless an explicit background is set.
/// </remarks>
public class StatusBar : Control
{
    private const int SeparatorWidth = 8;

    private readonly StatusBarItemCollection _items;
    private bool _syncingItems;

    /// <summary>Creates the bar with the double-buffered, self-painted style it needs.</summary>
    public StatusBar()
    {
        _items = new StatusBarItemCollection(this);
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

    /// <summary>The items in the bar, from left to right.</summary>
    public IList<StatusBarItem> Items => _items;

    /// <inheritdoc />
    protected override Size DefaultSize => new(300, 28);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        var width = 0;
        var height = 0;

        foreach (var item in _items)
        {
            if (item is null)
            {
                continue;
            }

            if (item.IsSeparator)
            {
                width += SeparatorWidth;
                continue;
            }

            var measured = XamlLayout.Measure(item, new Size(0, proposedSize.Height));
            width += item.Width > 0 ? item.Width : measured.Width;
            height = Math.Max(height, item.Height > 0 ? item.Height : measured.Height);
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
        foreach (var item in _items)
        {
            if (item is null || item.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var width = item.IsSeparator
                ? SeparatorWidth
                : item.Width > 0
                    ? item.Width
                    : XamlLayout.Measure(item, new Size(0, content.Height)).Width;
            var bounds = new Rectangle(x, content.Top, Math.Max(0, width), Math.Max(0, content.Height));
            if (item.Bounds != bounds)
            {
                item.Bounds = bounds;
            }

            x += Math.Max(0, width);
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
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_syncingItems || e.Control is not StatusBarItem item || _items.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _items.Add(item);
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
        if (_syncingItems || e.Control is not StatusBarItem item || !_items.Contains(item))
        {
            return;
        }

        _syncingItems = true;
        try
        {
            _items.Remove(item);
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
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void OnItemInserted(StatusBarItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!_syncingItems && !Controls.Contains(item))
        {
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

        PerformLayout();
        Invalidate();
    }

    private void OnItemRemoved(StatusBarItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!_syncingItems && Controls.Contains(item))
        {
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

        PerformLayout();
        Invalidate();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    // 集合负责把条目挂成子窗口；直接声明在 XAML 里的条目由 OnControlAdded 反向补进集合。
    private sealed class StatusBarItemCollection(StatusBar owner) : Collection<StatusBarItem>
    {
        protected override void InsertItem(int index, StatusBarItem item)
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

        protected override void SetItem(int index, StatusBarItem item)
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
