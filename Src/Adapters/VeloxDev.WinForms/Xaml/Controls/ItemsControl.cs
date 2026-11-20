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
/// Items stack vertically in source order. With <see cref="ItemTemplate"/> each item becomes the template's control
/// and receives the item as its <see cref="Control.DataContext"/>; without a template a <see cref="TextBlock"/> shows
/// the item's string form. The control subscribes to <see cref="INotifyCollectionChanged"/> and rebuilds when the
/// source changes, but it does not virtualize: every item has a control.
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
        new PropertyMetadata(null, OnItemsSourcePropertyChanged));

    /// <summary>The template used for each item; null draws the item's string form.</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(ItemsControl),
        new PropertyMetadata(null, OnItemTemplatePropertyChanged));

    private readonly List<System.Windows.Forms.Control> _generated = [];
    private INotifyCollectionChanged? _observed;

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

    /// <summary>The controls created for the current items.</summary>
    protected IReadOnlyList<System.Windows.Forms.Control> GeneratedItems => _generated;

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = 0;
        var height = 0;
        foreach (var child in _generated)
        {
            var measured = XamlLayout.Measure(child, new Size(proposedSize.Width, 0));
            width = Math.Max(width, measured.Width);
            height += measured.Height;
        }

        return new Size(
            width + (int)Math.Ceiling(Padding.Horizontal),
            height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
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
            ClearItems();
        }

        base.Dispose(disposing);
    }

    /// <summary>Removes and disposes every generated item control.</summary>
    protected void ClearItems()
    {
        foreach (var child in _generated)
        {
            if (Controls.Contains(child))
            {
                Controls.Remove(child);
            }

            child.Dispose();
        }

        _generated.Clear();
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
            var visual = CreateItemVisual(item);
            // 派生类可以返回 null；把它留在 _generated 里，下一次布局量到它就会空引用。
            if (visual is null)
            {
                continue;
            }

            _generated.Add(visual);
            Controls.Add(visual);
        }

        PerformLayout();
        Invalidate();
    }

    /// <summary>Creates the visual for one item.</summary>
    /// <param name="item">The source item.</param>
    protected virtual System.Windows.Forms.Control CreateItemVisual(object? item)
    {
        if (ItemTemplate is { } template)
        {
            var content = template.CreateContent();
            // 工厂返回 null 时退回文本行，而不是把 null 当成视图交给布局。
            if (content is null)
            {
                return CreateTextVisual(item);
            }

            if (content is Control xaml)
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

            return content;
        }

        return CreateTextVisual(item);
    }

    private static TextBlock CreateTextVisual(object? item)
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
}
