using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// The shared dependency-property surface of <see cref="ListBox"/> and <see cref="ComboBox"/>.
/// </summary>
/// <remarks>
/// The native selector owns scrolling and selection; this class mirrors <see cref="ItemsSource"/>,
/// <see cref="SelectedItem"/>, <see cref="SelectedIndex"/> and <see cref="DisplayMemberPath"/> into it. A selection
/// made in the native control writes the two selection properties back, so a two-way binding has a source of truth.
/// </remarks>
public abstract class Selector : Control
{
    /// <summary>The sequence shown by the selector.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(Selector),
        new PropertyMetadata(null, OnItemsSourcePropertyChanged));

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(Selector),
        new PropertyMetadata(null, OnSelectedItemPropertyChanged));

    /// <summary>The selected index, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(Selector),
        new PropertyMetadata(-1, OnSelectedIndexPropertyChanged));

    /// <summary>The property name used to display an item; empty means <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(Selector),
        new PropertyMetadata(string.Empty, OnDisplayMemberPathPropertyChanged));

    /// <summary>The property path used to derive <see cref="SelectedValue"/> from <see cref="SelectedItem"/>.</summary>
    public static readonly DependencyProperty SelectedValuePathProperty = DependencyProperty.Register(
        nameof(SelectedValuePath),
        typeof(string),
        typeof(Selector),
        new PropertyMetadata(string.Empty, OnSelectedValuePathPropertyChanged));

    /// <summary>The value of <see cref="SelectedItem"/> read through <see cref="SelectedValuePath"/>.</summary>
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue),
        typeof(object),
        typeof(Selector),
        new PropertyMetadata(null, OnSelectedValuePropertyChanged));

    /// <summary>The border stroke colour; a translucent black by default.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(Selector),
        new PropertyMetadata(Color.FromArgb(0x66, 0, 0, 0), OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(Selector),
        new PropertyMetadata(new Thickness(1f), OnVisualPropertyChanged));

    private readonly List<object?> _selectedItems = [];
    private INotifyCollectionChanged? _observed;

    // 原生选择变化回写 DP 时，DP 回调不能再推回原生控件。
    private bool syncing;

    // SelectedValue 由 SelectedItem 反推时，不能再按值反选一次。
    private bool syncingValue;

    /// <summary>Gets or sets the sequence shown by the selector.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Gets or sets the selected item.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets or sets the selected index.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Gets or sets the display property name; empty uses <see cref="object.ToString"/>.</summary>
    public string DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty) ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>Gets or sets the property path used to derive <see cref="SelectedValue"/>.</summary>
    /// <remarks>An empty path means the selected item is the selected value.</remarks>
    public string SelectedValuePath
    {
        get => (string?)GetValue(SelectedValuePathProperty) ?? string.Empty;
        set => SetValue(SelectedValuePathProperty, value);
    }

    /// <summary>Gets or sets the selected value, matched through <see cref="SelectedValuePath"/>.</summary>
    /// <remarks>Assigning a value selects the first item whose path value equals it; an unmatched assignment keeps the current selection.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    /// <summary>The items selected in the native selector.</summary>
    /// <remarks>
    /// The collection is a live snapshot: the base selector mirrors its single selected item, while a multi-select
    /// selector such as <see cref="ListBox"/> mirrors every selected item after a native selection change.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IList SelectedItems => _selectedItems;

    /// <summary>The storage behind <see cref="SelectedItems"/>, for derived selectors that mirror a multi-selection.</summary>
    protected IList SelectedItemsCore => _selectedItems;

    /// <summary>Gets or sets the border stroke colour.</summary>
    public Color BorderBrush
    {
        get => (Color)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side, in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>The number of items currently held by the native selector.</summary>
    public int ItemCount => NativeItems.Count;

    /// <summary>Raised when the native selector reports a selection change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The native selector this class mirrors.</summary>
    protected abstract ListControl NativeSelector { get; }

    /// <summary>The native item collection.</summary>
    protected abstract System.Collections.IList NativeItems { get; }

    /// <summary>The item selected in the native selector.</summary>
    protected abstract object? NativeSelectedItem { get; }

    /// <summary>Suspends native redraw while items are rebuilt.</summary>
    protected abstract void BeginNativeUpdate();

    /// <summary>Resumes native redraw after items are rebuilt.</summary>
    protected abstract void EndNativeUpdate();

    /// <summary>Subscribes to the native selection-changed event.</summary>
    protected abstract void AttachSelectionChanged(EventHandler handler);

    /// <summary>Unsubscribes from the native selection-changed event.</summary>
    protected abstract void DetachSelectionChanged(EventHandler handler);

    /// <summary>Connects the native selector's events after the derived constructor has created it.</summary>
    protected void InitializeSelector()
    {
        TabStop = false;
        NativeSelector.TabStop = true;
        AttachSelectionChanged(OnNativeSelectionChanged);
        NativeSelector.KeyDown += (_, e) => OnKeyDown(e);
        NativeSelector.KeyUp += (_, e) => OnKeyUp(e);
        NativeSelector.MouseDown += (_, e) => OnMouseDown(e);
        NativeSelector.MouseUp += (_, e) => OnMouseUp(e);
        NativeSelector.MouseMove += (_, e) => OnMouseMove(e);
        NativeSelector.MouseEnter += (_, e) => OnMouseEnter(e);
        NativeSelector.MouseLeave += (_, e) => OnMouseLeave(e);
        NativeSelector.MouseWheel += (_, e) => OnMouseWheel(e);
        NativeSelector.GotFocus += (_, e) => OnGotFocus(e);
        NativeSelector.LostFocus += (_, e) => OnLostFocus(e);
        ApplySelectorProperties();
    }

    /// <summary>Moves focus to the native selector.</summary>
    /// <returns><see langword="true"/> when the selector received focus.</returns>
    public new bool Focus() => Focusable && NativeSelector.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = NativeSelector.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            Math.Max(23, preferred.Height) + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
        var border = BorderThickness;
        var inset = new Rectangle(
            content.Left + (int)Math.Ceiling(border.Left),
            content.Top + (int)Math.Ceiling(border.Top),
            Math.Max(0, content.Width - (int)Math.Ceiling(border.Horizontal)),
            Math.Max(0, content.Height - (int)Math.Ceiling(border.Vertical)));
        if (NativeSelector.Bounds != inset)
        {
            NativeSelector.Bounds = inset;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var border = BorderThickness;
        if (BorderBrush.A <= 0 || border.IsEmpty)
        {
            return;
        }

        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1f), Math.Max(0, Height - 1f));
        var thickness = Math.Max(Math.Max(border.Left, border.Right), Math.Max(border.Top, border.Bottom));
        if (bounds.Width <= 0f || bounds.Height <= 0f || thickness <= 0f)
        {
            return;
        }

        using var path = XamlDrawing.RoundedRectangle(bounds, 0f);
        using var pen = new Pen(BorderBrush, Math.Min(thickness, Math.Min(bounds.Width, bounds.Height) / 2f));
        e.Graphics.DrawPath(pen, path);
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);

        if (ReferenceEquals(property, BackColorProperty) && newValue is Color background && !background.IsEmpty)
        {
            NativeSelector.BackColor = XamlDrawing.ToOpaque(background, SystemColors.Window);
        }
        else if (ReferenceEquals(property, ForeColorProperty) && newValue is Color foreground && !foreground.IsEmpty)
        {
            NativeSelector.ForeColor = foreground;
        }
        else if (ReferenceEquals(property, FontProperty) && newValue is Font font)
        {
            NativeSelector.Font = font;
        }
        else if (ReferenceEquals(property, IsEnabledProperty) && newValue is bool enabled)
        {
            NativeSelector.Enabled = enabled;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachSource();
            DetachSelectionChanged(OnNativeSelectionChanged);
            if (Controls.Contains(NativeSelector))
            {
                Controls.Remove(NativeSelector);
            }

            NativeSelector.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Rebuilds the native item collection from <see cref="ItemsSource"/>.</summary>
    protected void RebuildItems()
    {
        if (IsDisposed)
        {
            return;
        }

        var previous = SelectedItem;
        syncing = true;
        try
        {
            BeginNativeUpdate();
            NativeItems.Clear();
            if (ItemsSource is { } source)
            {
                foreach (var item in source)
                {
                    NativeItems.Add(item);
                }
            }
        }
        finally
        {
            EndNativeUpdate();
            syncing = false;
        }

        if (previous is not null)
        {
            var index = NativeItems.IndexOf(previous);
            if (index >= 0)
            {
                syncing = true;
                try
                {
                    NativeSelector.SelectedIndex = index;
                }
                finally
                {
                    syncing = false;
                }
            }
        }

        ApplySelectorProperties();
        if (SelectedItem is null && SelectedValue is not null)
        {
            TrySelectByValue();
        }

        UpdateSelectedItems();
        PerformLayout();
        Invalidate();
    }

    private void ApplySelectorProperties()
    {
        if (syncing || IsDisposed)
        {
            return;
        }

        syncing = true;
        try
        {
            NativeSelector.DisplayMember = DisplayMemberPath;
            NativeSelector.BackColor = HasXamlValue(BackColorProperty)
                ? XamlDrawing.ToOpaque(Background, SystemColors.Window)
                : SystemColors.Window;
            NativeSelector.ForeColor = ForeColor;
            NativeSelector.Font = Font;
            NativeSelector.Enabled = IsEnabled;

            if (SelectedItem is { } item)
            {
                var index = NativeItems.IndexOf(item);
                if (index >= 0)
                {
                    if (NativeSelector.SelectedIndex != index)
                    {
                        NativeSelector.SelectedIndex = index;
                    }

                    if (SelectedIndex != index)
                    {
                        SetValue(SelectedIndexProperty, index);
                    }
                }
            }
            else if (SelectedIndex >= 0 && SelectedIndex < NativeItems.Count)
            {
                if (NativeSelector.SelectedIndex != SelectedIndex)
                {
                    NativeSelector.SelectedIndex = SelectedIndex;
                }

                var selected = NativeSelectedItem;
                if (!Equals(SelectedItem, selected))
                {
                    SetValue(SelectedItemProperty, selected);
                }
            }
            else if (NativeSelector.SelectedIndex != -1 && SelectedIndex < 0 && SelectedItem is null)
            {
                NativeSelector.SelectedIndex = -1;
            }
        }
        finally
        {
            syncing = false;
        }

        UpdateSelectedValueFromItem();
    }

    /// <summary>Copies the native selection into <see cref="SelectedItems"/>.</summary>
    protected virtual void UpdateSelectedItems()
    {
        _selectedItems.Clear();
        if (NativeSelectedItem is { } item)
        {
            _selectedItems.Add(item);
        }
    }

    private void UpdateSelectedValueFromItem()
    {
        if (syncing || syncingValue || IsDisposed)
        {
            return;
        }

        var item = SelectedItem;
        object? value = item;
        if (item is not null && SelectedValuePath is { Length: > 0 } path)
        {
            // 路径解析失败时不要把 null 当成 SelectedValue，保留调用方写的值。
            if (!TryResolveValue(item, path, out value))
            {
                return;
            }
        }

        if (Equals(SelectedValue, value))
        {
            return;
        }

        syncingValue = true;
        try
        {
            SetValue(SelectedValueProperty, value);
        }
        finally
        {
            syncingValue = false;
        }
    }

    private void TrySelectByValue()
    {
        if (IsDisposed || syncing || syncingValue)
        {
            return;
        }

        if (ItemsSource is { } source)
        {
            foreach (var item in source)
            {
                if (MatchesSelectedValue(item))
                {
                    SelectedItem = item;
                    return;
                }
            }
        }

        if (SelectedValue is null)
        {
            // 显式写入 null 表示清空选择；找不到匹配项时保持原选择。
            SelectedItem = null;
            SelectedIndex = -1;
        }
    }

    private bool MatchesSelectedValue(object? item)
    {
        if (SelectedValuePath is { Length: > 0 } path)
        {
            return TryResolveValue(item, path, out var value) && Equals(value, SelectedValue);
        }

        return Equals(item, SelectedValue);
    }

    private static bool TryResolveValue(object? item, string path, out object? value)
    {
        value = null;
        if (item is null)
        {
            return false;
        }

        var bindingPath = new BindingPath(path);
        return bindingPath.TryResolve(item, out value, out _, out _);
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

    private void OnNativeSelectionChanged(object? sender, EventArgs e)
    {
        if (syncing || IsDisposed)
        {
            return;
        }

        syncing = true;
        try
        {
            SetValue(SelectedIndexProperty, NativeSelector.SelectedIndex);
            SetValue(SelectedItemProperty, NativeSelectedItem);
        }
        finally
        {
            syncing = false;
        }

        UpdateSelectedItems();
        UpdateSelectedValueFromItem();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnItemsSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Selector selector || selector.IsDisposed)
        {
            return;
        }

        selector.AttachSource(e.NewValue as IEnumerable);
        selector.RebuildItems();
    }

    private static void OnSelectedItemPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Selector selector && !selector.IsDisposed)
        {
            selector.ApplySelectorProperties();
        }
    }

    private static void OnSelectedIndexPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Selector selector && !selector.IsDisposed)
        {
            selector.ApplySelectorProperties();
        }
    }

    private static void OnDisplayMemberPathPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Selector selector && !selector.IsDisposed)
        {
            selector.ApplySelectorProperties();
        }
    }

    private static void OnSelectedValuePathPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Selector selector && !selector.IsDisposed)
        {
            selector.UpdateSelectedValueFromItem();
        }
    }

    private static void OnSelectedValuePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Selector selector && !selector.IsDisposed && !selector.syncingValue)
        {
            selector.TrySelectByValue();
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
            control.Invalidate();
        }
    }
}

/// <summary>
/// A native list box with dependency-property items, selection and display settings.
/// </summary>
/// <remarks>The item visuals come from the native list box; use <see cref="ItemsControl"/> with an item template when every item needs a custom control.</remarks>
public class ListBox : Selector
{
    /// <summary>How many items can be selected at once.</summary>
    public static readonly DependencyProperty SelectionModeProperty = DependencyProperty.Register(
        nameof(SelectionMode),
        typeof(System.Windows.Forms.SelectionMode),
        typeof(ListBox),
        new PropertyMetadata(System.Windows.Forms.SelectionMode.One, OnSelectionModeChanged));

    private readonly System.Windows.Forms.ListBox listBox;

    /// <summary>Creates the list box.</summary>
    public ListBox()
    {
        listBox = new System.Windows.Forms.ListBox
        {
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
        };
        Controls.Add(listBox);
        InitializeSelector();
    }

    /// <summary>Gets or sets how many items can be selected.</summary>
    public System.Windows.Forms.SelectionMode SelectionMode
    {
        get => (System.Windows.Forms.SelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>Selects every item when <see cref="SelectionMode"/> allows multiple selections.</summary>
    public void SelectAll()
    {
        if (listBox.SelectionMode == System.Windows.Forms.SelectionMode.One)
        {
            return;
        }

        listBox.BeginUpdate();
        try
        {
            for (var index = 0; index < listBox.Items.Count; index++)
            {
                listBox.SetSelected(index, true);
            }
        }
        finally
        {
            listBox.EndUpdate();
        }

        UpdateSelectedItems();
    }

    /// <inheritdoc />
    protected override void UpdateSelectedItems()
    {
        SelectedItemsCore.Clear();
        foreach (var item in listBox.SelectedItems)
        {
            SelectedItemsCore.Add(item);
        }
    }

    /// <inheritdoc />
    protected override ListControl NativeSelector => listBox;

    /// <inheritdoc />
    protected override System.Collections.IList NativeItems => listBox.Items;

    /// <inheritdoc />
    protected override object? NativeSelectedItem => listBox.SelectedItem;

    /// <inheritdoc />
    protected override void BeginNativeUpdate() => listBox.BeginUpdate();

    /// <inheritdoc />
    protected override void EndNativeUpdate() => listBox.EndUpdate();

    /// <inheritdoc />
    protected override void AttachSelectionChanged(EventHandler handler) => listBox.SelectedIndexChanged += handler;

    /// <inheritdoc />
    protected override void DetachSelectionChanged(EventHandler handler) => listBox.SelectedIndexChanged -= handler;

    private static void OnSelectionModeChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListBox list && e.NewValue is System.Windows.Forms.SelectionMode mode)
        {
            list.listBox.SelectionMode = mode;
        }
    }
}

/// <summary>
/// A native combo box with dependency-property items, selection and display settings.
/// </summary>
/// <remarks><see cref="IsEditable"/> switches between a drop-down list and an editable drop-down.</remarks>
public class ComboBox : Selector
{
    /// <summary>Whether the text can be edited.</summary>
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable),
        typeof(bool),
        typeof(ComboBox),
        new PropertyMetadata(false, OnIsEditableChanged));

    private readonly System.Windows.Forms.ComboBox comboBox;

    /// <summary>Creates the combo box.</summary>
    public ComboBox()
    {
        comboBox = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        Controls.Add(comboBox);
        InitializeSelector();
    }

    /// <summary>Gets or sets whether the text can be edited.</summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty)!;
        set => SetValue(IsEditableProperty, value);
    }

    /// <inheritdoc />
    protected override ListControl NativeSelector => comboBox;

    /// <inheritdoc />
    protected override System.Collections.IList NativeItems => comboBox.Items;

    /// <inheritdoc />
    protected override object? NativeSelectedItem => comboBox.SelectedItem;

    /// <inheritdoc />
    protected override void BeginNativeUpdate() => comboBox.BeginUpdate();

    /// <inheritdoc />
    protected override void EndNativeUpdate() => comboBox.EndUpdate();

    /// <inheritdoc />
    protected override void AttachSelectionChanged(EventHandler handler) => comboBox.SelectedIndexChanged += handler;

    /// <inheritdoc />
    protected override void DetachSelectionChanged(EventHandler handler) => comboBox.SelectedIndexChanged -= handler;

    private static void OnIsEditableChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ComboBox combo && e.NewValue is bool editable)
        {
            combo.comboBox.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        }
    }
}
