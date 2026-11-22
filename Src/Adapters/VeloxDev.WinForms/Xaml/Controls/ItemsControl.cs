using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Repeats one template (or a text line) for every item in <see cref="ItemsSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Items stack vertically in source order unless <see cref="ItemsPanel"/> supplies a panel. With
/// <see cref="ItemTemplate"/> each item becomes the template's control and receives the item as its
/// <see cref="Control.DataContext"/>; without a template a <see cref="TextBlock"/> shows the item's string form.
/// </para>
/// <para>
/// <see cref="ItemContainerStyle"/> is applied to every generated XAML item container. When
/// <see cref="Virtualizing"/> is on, only the items intersecting the nearest <see cref="ScrollViewer"/> viewport are
/// realized; item heights are the fixed <see cref="ItemHeight"/>.
/// </para>
/// <para>This is the read-only repeat primitive; <see cref="ListBox"/> and <see cref="ComboBox"/> wrap native selectors.</para>
/// </remarks>
public class ItemsControl : Control
{
    /// <summary>The sequence used to produce one visual per item.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemsSourcePropertyChanged) { AffectsMeasure = true });

    /// <summary>The template used for each item; null draws the item's string form.</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemTemplatePropertyChanged) { AffectsMeasure = true });

    /// <summary>Chooses a template per item; a non-null result wins over <see cref="ItemTemplate"/>.</summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ItemTemplateSelector),
        typeof(TemplateSelector),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemTemplateSelectorPropertyChanged) { AffectsMeasure = true });

    /// <summary>The panel that hosts generated items, or null to stack them directly in this control.</summary>
    public static readonly DependencyProperty ItemsPanelProperty = DependencyProperty.Register(
        nameof(ItemsPanel),
        typeof(Control),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemsPanelPropertyChanged) { AffectsMeasure = true });

    /// <summary>Creates the hosting panel when <see cref="ItemsPanel"/> is not set.</summary>
    public static readonly DependencyProperty ItemsPanelTemplateProperty = DependencyProperty.Register(
        nameof(ItemsPanelTemplate),
        typeof(ItemsPanelTemplate),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemsPanelTemplatePropertyChanged) { AffectsMeasure = true });

    /// <summary>A style applied to every generated XAML item container.</summary>
    public static readonly DependencyProperty ItemContainerStyleProperty = DependencyProperty.Register(
        nameof(ItemContainerStyle),
        typeof(Style),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemContainerStylePropertyChanged) { AffectsMeasure = true });

    /// <summary>Chooses a style per generated item; a non-null result wins over <see cref="ItemContainerStyle"/>.</summary>
    public static readonly DependencyProperty ItemContainerStyleSelectorProperty = DependencyProperty.Register(
        nameof(ItemContainerStyleSelector),
        typeof(StyleSelector),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemContainerStyleSelectorPropertyChanged) { AffectsMeasure = true });

    /// <summary>Whether only the items inside the nearest scroll viewport are realized.</summary>
    public static readonly DependencyProperty VirtualizingProperty = DependencyProperty.Register(
        nameof(Virtualizing),
        typeof(bool),
        typeof(ItemsControl),
        new PropertyMetadata(false, OnVirtualizingPropertyChanged) { AffectsMeasure = true });

    /// <summary>The fixed row height used when <see cref="Virtualizing"/> is on; 24 pixels by default.</summary>
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(int),
        typeof(ItemsControl),
        new PropertyMetadata(24, OnItemHeightPropertyChanged) { AffectsMeasure = true });

    private readonly List<System.Windows.Forms.Control> _generated = [];
    private readonly Dictionary<int, System.Windows.Forms.Control> _virtualVisuals = [];
    private readonly List<object?> _items = [];
    private readonly ItemContainerGenerator _itemContainerGenerator = new();
    private INotifyCollectionChanged? _observed;
    private ScrollViewer? _scrollViewer;
    private Control? _itemsPanel;
    private Control? _templatePanel;
    private bool _clearingItems;
    private bool _updatingVirtualRange;
    private int _virtualFirst = -1;
    private int _virtualLast = -1;

    /// <summary>Creates the control with double buffering.</summary>
    public ItemsControl()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the sequence used to produce one visual per item.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Gets or sets the template used for each item.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Gets or sets the per-item template selector.</summary>
    /// <remarks>A selector that returns a template for an item overrides <see cref="ItemTemplate"/> for that item.</remarks>
    public TemplateSelector? ItemTemplateSelector
    {
        get => (TemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets the panel that hosts generated items.</summary>
    public Control? ItemsPanel
    {
        get => (Control?)GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    /// <summary>Gets or sets the template that creates the hosting panel when <see cref="ItemsPanel"/> is not set.</summary>
    public ItemsPanelTemplate? ItemsPanelTemplate
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelTemplateProperty);
        set => SetValue(ItemsPanelTemplateProperty, value);
    }

    /// <summary>Gets or sets the style applied to generated XAML item containers.</summary>
    public Style? ItemContainerStyle
    {
        get => (Style?)GetValue(ItemContainerStyleProperty);
        set => SetValue(ItemContainerStyleProperty, value);
    }

    /// <summary>Gets or sets the per-item style selector.</summary>
    /// <remarks>A selector that returns a style for an item overrides <see cref="ItemContainerStyle"/> for that item.</remarks>
    public StyleSelector? ItemContainerStyleSelector
    {
        get => (StyleSelector?)GetValue(ItemContainerStyleSelectorProperty);
        set => SetValue(ItemContainerStyleSelectorProperty, value);
    }

    /// <summary>The map from source items to the controls generated for them.</summary>
    public ItemContainerGenerator ItemContainerGenerator => _itemContainerGenerator;

    /// <summary>Gets or sets whether only the items inside the nearest scroll viewport are realized.</summary>
    public bool Virtualizing
    {
        get => (bool)GetValue(VirtualizingProperty)!;
        set => SetValue(VirtualizingProperty, value);
    }

    /// <summary>Gets or sets the fixed row height used while virtualizing.</summary>
    public int ItemHeight
    {
        get => (int)GetValue(ItemHeightProperty)!;
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>The number of item controls currently realized.</summary>
    public int RealizedItemCount => Virtualizing ? _virtualVisuals.Count : _generated.Count;

    /// <summary>The controls created for the current items.</summary>
    protected IReadOnlyList<System.Windows.Forms.Control> GeneratedItems => _generated;

    /// <inheritdoc />
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        HookScrollViewer();
    }

    /// <inheritdoc />
    protected override void OnAncestorChainChanged()
    {
        base.OnAncestorChainChanged();
        HookScrollViewer();
        if (Virtualizing && UpdateVirtualRange())
        {
            PerformLayout();
        }
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (_clearingItems)
        {
            return new Size(
                (int)Math.Ceiling(Padding.Horizontal),
                (int)Math.Ceiling(Padding.Vertical));
        }

        if (Virtualizing)
        {
            var width = proposedSize.Width > 0 ? proposedSize.Width : 0;
            return new Size(
                width + (int)Math.Ceiling(Padding.Horizontal),
                _items.Count * Math.Max(1, ItemHeight) + (int)Math.Ceiling(Padding.Vertical));
        }

        if (EffectiveItemsPanel is { } panel)
        {
            var measured = XamlLayout.Measure(panel, proposedSize);
            return new Size(
                measured.Width + (int)Math.Ceiling(Padding.Horizontal),
                measured.Height + (int)Math.Ceiling(Padding.Vertical));
        }

        var stackWidth = 0;
        var stackHeight = 0;
        foreach (var child in _generated)
        {
            var measured = XamlLayout.Measure(child, new Size(proposedSize.Width, 0));
            stackWidth = Math.Max(stackWidth, measured.Width);
            stackHeight += measured.Height;
        }

        return new Size(
            stackWidth + (int)Math.Ceiling(Padding.Horizontal),
            stackHeight + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_clearingItems)
        {
            return;
        }

        var content = XamlLayout.ContentBounds(this);

        if (Virtualizing)
        {
            UpdateVirtualRange();
            var height = Math.Max(1, ItemHeight);
            foreach (var pair in _virtualVisuals)
            {
                var bounds = new Rectangle(content.Left, content.Top + pair.Key * height, content.Width, height);
                if (pair.Value.Bounds != bounds)
                {
                    pair.Value.Bounds = bounds;
                }
            }

            return;
        }

        if (EffectiveItemsPanel is { } panel)
        {
            if (panel.Bounds != content)
            {
                panel.Bounds = content;
            }

            return;
        }

        var y = content.Top;
        foreach (var child in _generated)
        {
            var measured = XamlLayout.Measure(child, new Size(content.Width, 0));
            var bounds = new Rectangle(content.Left, y, content.Width, measured.Height);
            if (child.Bounds != bounds)
            {
                child.Bounds = bounds;
            }

            y += bounds.Height;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachSource();
            DetachScrollViewer();
            ClearItems();
            if (_itemsPanel is not null)
            {
                if (Controls.Contains(_itemsPanel))
                {
                    Controls.Remove(_itemsPanel);
                }

                _itemsPanel.Dispose();
                _itemsPanel = null;
            }

            DisposeTemplatePanel();
        }

        base.Dispose(disposing);
    }

    /// <summary>Removes and disposes every generated item control.</summary>
    protected void ClearItems()
    {
        if (_clearingItems)
        {
            return;
        }

        _clearingItems = true;
        try
        {
            var removed = new HashSet<System.Windows.Forms.Control>();
            foreach (var child in _generated.ToArray())
            {
                if (removed.Add(child))
                {
                    RemoveItemVisual(child);
                }
            }

            foreach (var pair in _virtualVisuals.ToArray())
            {
                if (removed.Add(pair.Value))
                {
                    RemoveItemVisual(pair.Value);
                }
            }

            _generated.Clear();
            _virtualVisuals.Clear();
            _virtualFirst = -1;
            _virtualLast = -1;
            _items.Clear();
            _itemContainerGenerator.Clear();
        }
        finally
        {
            _clearingItems = false;
        }
    }

    /// <summary>Rebuilds one control per item in <see cref="ItemsSource"/>.</summary>
    protected virtual void RebuildItems()
    {
        if (IsDisposed)
        {
            return;
        }

        ClearItems();
        if (ItemsSource is not { } source)
        {
            PerformLayout();
            Invalidate();
            return;
        }

        foreach (var item in source)
        {
            _items.Add(item);
        }

        if (Virtualizing)
        {
            UpdateVirtualRange();
            PerformLayout();
            Invalidate();
            return;
        }

        _itemContainerGenerator.SetItems(_items);
        for (var index = 0; index < _items.Count; index++)
        {
            var visual = CreateItemVisual(_items[index]);
            // 派生类可以返回 null：跳过视觉但保留条目下标，否则后面的映射整体错位。
            if (visual is not null)
            {
                _generated.Add(visual);
                ((System.Windows.Forms.Control?)EffectiveItemsPanel ?? this).Controls.Add(visual);
            }

            _itemContainerGenerator.SetContainer(index, visual);
        }

        _itemContainerGenerator.CompleteGeneration();
        PerformLayout();
        Invalidate();
    }

    /// <summary>Creates the visual for one item.</summary>
    /// <param name="item">The source item.</param>
    protected virtual System.Windows.Forms.Control CreateItemVisual(object? item)
    {
        System.Windows.Forms.Control content;
        var template = ItemTemplateSelector?.SelectTemplate(item) ?? ItemTemplate;
        if (template is { } selected)
        {
            content = selected.CreateContent();
            // 工厂返回 null 时退回文本行，而不是把 null 当成视图交给布局。
            if (content is null)
            {
                content = CreateTextVisual(item);
            }
            else if (content is Control xaml)
            {
                xaml.DataContext = item;
            }
            else
            {
#if NET8_0_OR_GREATER
                content.DataContext = item;
#else
                content.Tag = item;
#endif
            }
        }
        else
        {
            content = CreateTextVisual(item);
        }

        if (content is Control container)
        {
            var style = ItemContainerStyleSelector?.SelectStyle(item) ?? ItemContainerStyle;
            if (style is { } applied)
            {
                container.Style = applied;
            }
        }

        return content;
    }

    private bool UpdateVirtualRange()
    {
        if (_clearingItems || _updatingVirtualRange)
        {
            return false;
        }

        _updatingVirtualRange = true;
        try
        {
            return UpdateVirtualRangeCore();
        }
        finally
        {
            _updatingVirtualRange = false;
        }
    }

    private bool UpdateVirtualRangeCore()
    {
        if (!Virtualizing)
        {
            return false;
        }

        if (_items.Count == 0)
        {
            var hadVisuals = _virtualVisuals.Count > 0;
            foreach (var pair in _virtualVisuals)
            {
                RemoveItemVisual(pair.Value);
            }

            _virtualVisuals.Clear();
            SyncVirtualContainers();
            return hadVisuals;
        }

        var height = Math.Max(1, ItemHeight);
        var offset = _scrollViewer?.ScrollOffset.Y ?? 0d;

        // ScrollViewer.ViewportHeight=0 表示“用视口自己的客户区”，不是“用 ItemsControl 自己的高度”；
        // 虚拟列表本身通常撑到全部条目高，读它会得到整份内容、把所有行都实例化。
        var viewport = _scrollViewer is { } viewer
            ? viewer.ViewportHeight > 0 ? viewer.ViewportHeight : viewer.ClientSize.Height
            : ClientSize.Height;
        if (viewport <= 0)
        {
            viewport = height * 8;
        }

        var first = Math.Max(0, (int)Math.Floor(offset / height) - 2);
        var last = Math.Min(_items.Count - 1, (int)Math.Ceiling((offset + viewport) / height) + 2);

        // 拖条每像素都会触发 ViewChanged；可见下标范围没跨行时什么都不用做，避免逐像素重建/重排。
        if (first == _virtualFirst && last == _virtualLast)
        {
            return false;
        }

        _virtualFirst = first;
        _virtualLast = last;

        foreach (var pair in _virtualVisuals.ToArray())
        {
            if (pair.Key < first || pair.Key > last)
            {
                RemoveItemVisual(pair.Value);
                _virtualVisuals.Remove(pair.Key);
            }
        }

        for (var index = first; index <= last; index++)
        {
            if (_virtualVisuals.ContainsKey(index))
            {
                continue;
            }

            var visual = CreateItemVisual(_items[index]);
            if (visual is null)
            {
                continue;
            }

            _virtualVisuals[index] = visual;
            Controls.Add(visual);
        }

        _generated.Clear();
        _generated.AddRange(_virtualVisuals.Values);
        SyncVirtualContainers();
        return true;
    }

    // 虚拟模式下未实现的条目没有容器，但仍占一个下标。
    private void SyncVirtualContainers()
    {
        _itemContainerGenerator.SetItems(_items);
        foreach (var pair in _virtualVisuals)
        {
            _itemContainerGenerator.SetContainer(pair.Key, pair.Value);
        }

        _itemContainerGenerator.CompleteGeneration();
    }

    private void RemoveItemVisual(System.Windows.Forms.Control visual)
    {
        var parent = visual.Parent;
        if (parent is not null && parent.Controls.Contains(visual))
        {
            parent.Controls.Remove(visual);
        }

        visual.Dispose();
    }

    private System.Windows.Forms.Control CreateTextVisual(object? item)
    {
        return new TextBlock
        {
            Text = item?.ToString() ?? string.Empty,
            DataContext = item,
            AutoSize = false,
        };
    }

    private void AttachSource(IEnumerable? source)
    {
        DetachSource();
        if (source is INotifyCollectionChanged observable)
        {
            _observed = observable;
            observable.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachSource()
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnCollectionChanged;
            _observed = null;
        }
    }

    private void HookScrollViewer()
    {
        var next = FindScrollViewer();
        if (ReferenceEquals(next, _scrollViewer))
        {
            return;
        }

        DetachScrollViewer();
        _scrollViewer = next;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnScrollViewChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnScrollViewChanged;
            _scrollViewer = null;
        }
    }

    private ScrollViewer? FindScrollViewer()
    {
        for (var current = Parent; current is not null; current = current.Parent)
        {
            if (current is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    private void OnScrollViewChanged(object? sender, EventArgs e)
    {
        if (!Virtualizing || IsDisposed)
        {
            return;
        }

        if (UpdateVirtualRange())
        {
            PerformLayout();
        }
    }

    private Control? EffectiveItemsPanel => _itemsPanel ?? _templatePanel;

    private void SetItemsPanel(Control? panel)
    {
        if (ReferenceEquals(_itemsPanel, panel))
        {
            return;
        }

        ClearItems();
        if (_itemsPanel is not null)
        {
            if (Controls.Contains(_itemsPanel))
            {
                Controls.Remove(_itemsPanel);
            }

            _itemsPanel.Dispose();
        }

        _itemsPanel = panel;
        if (_itemsPanel is not null && !Controls.Contains(_itemsPanel))
        {
            Controls.Add(_itemsPanel);
        }

        // 显式面板优先；只有它为空时才让模板生成一个。
        RefreshTemplatePanel();
        RebuildItems();
    }

    private void RefreshTemplatePanel()
    {
        DisposeTemplatePanel();
        if (_itemsPanel is not null || ItemsPanelTemplate is not { } template)
        {
            return;
        }

        var panel = template.CreatePanel();
        if (panel is null)
        {
            return;
        }

        _templatePanel = panel;
        if (!Controls.Contains(panel))
        {
            Controls.Add(panel);
        }
    }

    private void DisposeTemplatePanel()
    {
        if (_templatePanel is null)
        {
            return;
        }

        if (Controls.Contains(_templatePanel))
        {
            Controls.Remove(_templatePanel);
        }

        _templatePanel.Dispose();
        _templatePanel = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildItems();

    private static void OnItemsSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ItemsControl control || control.IsDisposed)
        {
            return;
        }

        control.AttachSource(e.NewValue as IEnumerable);
        control.RebuildItems();
    }

    private static void OnItemTemplatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.RebuildItems();
        }
    }

    private static void OnItemsPanelPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.SetItemsPanel(e.NewValue as Control);
        }
    }

    private static void OnItemsPanelTemplatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            // 先摘掉旧面板里的条目，模板面板才会在空状态下被释放。
            control.ClearItems();
            control.RefreshTemplatePanel();
            control.RebuildItems();
        }
    }

    private static void OnItemTemplateSelectorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.RebuildItems();
        }
    }

    private static void OnItemContainerStylePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.RebuildItems();
        }
    }

    private static void OnItemContainerStyleSelectorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.RebuildItems();
        }
    }

    private static void OnVirtualizingPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control.HookScrollViewer();
            control.RebuildItems();
        }
    }

    private static void OnItemHeightPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsControl control && !control.IsDisposed)
        {
            control._virtualFirst = -1;
            control._virtualLast = -1;
            control.PerformLayout();
            control.Invalidate();
        }
    }
}

/// <summary>
/// Chooses a <see cref="Style"/> for one generated item container, the counterpart of a template selector.
/// </summary>
/// <remarks>A <see langword="null"/> result lets <see cref="ItemsControl.ItemContainerStyle"/> apply instead.</remarks>
public abstract class StyleSelector
{
    /// <summary>Returns the style for <paramref name="item"/>, or <see langword="null"/> when none applies.</summary>
    /// <param name="item">The source item about to receive a container.</param>
    public abstract Style? SelectStyle(object? item);
}
