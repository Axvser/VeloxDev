using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using NativeTreeNode = System.Windows.Forms.TreeNode;
using NativeTreeView = System.Windows.Forms.TreeView;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A hierarchical list backed by a native <see cref="System.Windows.Forms.TreeView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The control builds nodes from <see cref="ItemsSource"/>. For every item it looks for a public <c>Items</c> or
/// <c>Children</c> property whose value is an <see cref="IEnumerable"/> (a string is ignored) and recurses into it;
/// a source that cycles back to an ancestor stops at that ancestor. The node label comes from
/// <see cref="DisplayMemberPath"/> when set, otherwise from <see cref="object.ToString"/>.
/// </para>
/// <para>
/// A native tree node renders plain text only: <see cref="ItemTemplate"/> is accepted for source compatibility and
/// kept on the control, but it is never used to paint a node. The data item is stored in <c>Tag</c>, and
/// <see cref="SelectedItem"/> returns that item.
/// </para>
/// <para>
/// Hand-authored trees use <see cref="TreeViewItem"/>: either write them directly under this control or add them to
/// the <see cref="Items"/> collection. Their <see cref="TreeViewItem.Items"/> collection is the nested-node channel.
/// </para>
/// </remarks>
public class TreeView : Control
{
    /// <summary>The sequence whose items become root nodes.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TreeView),
        new PropertyMetadata(null, OnItemsSourcePropertyChanged));

    /// <summary>The template accepted for each item; a native node still renders text.</summary>
    /// <remarks>See the type remarks: a native <see cref="System.Windows.Forms.TreeView"/> cannot host a templated node.</remarks>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(TreeView),
        new PropertyMetadata(null));

    /// <summary>The property path used as a node label; empty uses <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(TreeView),
        new PropertyMetadata(string.Empty, OnStructurePropertyChanged));

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(TreeView),
        new PropertyMetadata(null, OnSelectionPropertyChanged));

    /// <summary>The depth-first index of the selected item, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(TreeView),
        new PropertyMetadata(-1, OnSelectionPropertyChanged));

    /// <summary>The border stroke colour; empty uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(TreeView),
        new PropertyMetadata(Color.Empty, OnChromePropertyChanged));

    /// <summary>The border stroke width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(TreeView),
        new PropertyMetadata(new Thickness(1f), OnChromePropertyChanged));

    /// <summary>The corner radius, in pixels; six by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(TreeView),
        new PropertyMetadata(6f, OnChromePropertyChanged));

    private readonly NativeTreeView _tree;
    private readonly ObservableCollection<TreeViewItem> _authored = new();
    private readonly List<INotifyCollectionChanged> _observedCollections = [];
    private readonly List<TreeViewItem> _observedItems = [];
    private readonly List<NodeEntry> _entries = [];
    private readonly List<object?> _flattened = [];
    private bool _syncing;
    private bool _rebuilding;

    /// <summary>Creates the wrapper and its native tree.</summary>
    public TreeView()
    {
        TabStop = false;

        _tree = new NativeTreeView
        {
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            TabStop = true,
        };

        _tree.AfterSelect += OnAfterSelect;
        _tree.KeyDown += (_, e) => OnKeyDown(e);
        _tree.KeyUp += (_, e) => OnKeyUp(e);
        _tree.MouseDown += (_, e) => OnMouseDown(e);
        _tree.MouseUp += (_, e) => OnMouseUp(e);
        _tree.MouseMove += (_, e) => OnMouseMove(e);
        _tree.MouseEnter += (_, e) => OnMouseEnter(e);
        _tree.MouseLeave += (_, e) => OnMouseLeave(e);
        _tree.MouseWheel += (_, e) => OnMouseWheel(e);
        _tree.GotFocus += (_, e) => OnGotFocus(e);
        _tree.LostFocus += (_, e) => OnLostFocus(e);

        Controls.Add(_tree);
        _authored.CollectionChanged += OnAuthoredChanged;
        ApplyNativeAppearance();
    }

    /// <summary>Gets or sets the sequence whose items become root nodes.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Gets or sets the template accepted for each item.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Gets or sets the property path used as a node label.</summary>
    public string DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty) ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>Gets or sets the selected item.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets or sets the depth-first index of the selected item.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Gets or sets the border stroke colour.</summary>
    public Color BorderBrush
    {
        get => (Color)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>The hand-authored root nodes.</summary>
    /// <remarks>Items added here are rendered by the native tree; the <see cref="TreeViewItem"/> controls themselves are not parented.</remarks>
    public IList<TreeViewItem> Items => _authored;

    /// <summary>The data items of every currently built node, in depth-first order.</summary>
    public IReadOnlyList<object?> FlattenedItems => _flattened;

    /// <summary>The number of built nodes.</summary>
    public int NodeCount => _entries.Count;

    /// <summary>Raised after the native tree reports a selection change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Moves focus to the native tree.</summary>
    /// <returns><see langword="true"/> when the tree received focus.</returns>
    public new bool Focus() => Focusable && _tree.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = _tree.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            preferred.Height + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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
        if (_tree.Bounds != inset)
        {
            _tree.Bounds = inset;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted)
        {
            return;
        }

        XamlDrawing.PaintChrome(
            e.Graphics,
            ClientRectangle,
            EffectiveBackground,
            EffectiveBorder,
            BorderThickness,
            CornerRadius);
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        ApplyNativeAppearance();
    }

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TreeViewItem item)
        {
            // 直接写在 TreeView 下的 TreeViewItem 由原生节点呈现，控件本身不参与布局。
            item.Visible = false;
            RebuildNodes();
        }
    }

    /// <inheritdoc />
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (e.Control is TreeViewItem && !IsDisposed && !_tree.IsDisposed)
        {
            RebuildNodes();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachObservers();
            _authored.CollectionChanged -= OnAuthoredChanged;
            foreach (var item in _authored)
            {
                if (!Controls.Contains(item) && !item.IsDisposed)
                {
                    item.Dispose();
                }
            }

            if (Controls.Contains(_tree))
            {
                Controls.Remove(_tree);
            }

            _tree.Dispose();
        }

        base.Dispose(disposing);
    }

    private new Color EffectiveBackground => HasXamlValue(BackgroundProperty) && Background is { A: > 0 } brush
        ? brush.ApproximateColor
        : Theme.Current.Surface;

    private new Color EffectiveForeground => HasXamlValue(ForegroundProperty) ? base.EffectiveForeground : Theme.Current.Foreground;

    private Color EffectiveBorder => HasXamlValue(BorderBrushProperty) ? BorderBrush : Theme.Current.ControlBorder;

    private void ApplyNativeAppearance()
    {
        if (IsDisposed || _tree.IsDisposed)
        {
            return;
        }

        _tree.BackColor = XamlDrawing.ToOpaque(EffectiveBackground, SystemColors.Window);
        _tree.ForeColor = EffectiveForeground;
        _tree.Font = Font;
        _tree.Enabled = IsEnabled;
        _tree.ItemHeight = Math.Max(16, Theme.Current.ControlHeight);
        Invalidate();
    }

    private void OnAuthoredChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNodes();

    private void RebuildNodes()
    {
        if (IsDisposed || _rebuilding || _tree.IsDisposed)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            DetachObservers();
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                _entries.Clear();
                _flattened.Clear();

                var ancestors = new HashSet<object>(ReferenceComparer.Instance);
                foreach (var authored in _authored)
                {
                    AddAuthoredNode(authored, null, ancestors);
                }

                foreach (System.Windows.Forms.Control child in Controls)
                {
                    if (child is TreeViewItem item && !_authored.Contains(item))
                    {
                        AddAuthoredNode(item, null, ancestors);
                    }
                }

                if (ItemsSource is { } source)
                {
                    Observe(source);
                    foreach (var item in source)
                    {
                        AddDataNode(item, null, ancestors);
                    }
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
        }
        finally
        {
            _rebuilding = false;
        }

        ApplySelectionFromProperties();
    }

    private void AddAuthoredNode(TreeViewItem item, NativeTreeNode? parent, HashSet<object> ancestors)
    {
        if (!ancestors.Add(item))
        {
            return;
        }

        ObserveAuthorItem(item);

        var node = new NativeTreeNode(item.Header) { Tag = item };
        AddNode(node, parent, item);
        foreach (var child in item.Items)
        {
            AddAuthoredNode(child, node, ancestors);
        }

        if (item.IsExpanded)
        {
            node.Expand();
        }

        ancestors.Remove(item);
    }

    private void AddDataNode(object? item, NativeTreeNode? parent, HashSet<object> ancestors)
    {
        if (item is not null && !ancestors.Add(item))
        {
            return;
        }

        try
        {
            var node = new NativeTreeNode(BuildText(item)) { Tag = item };
            AddNode(node, parent, item);

            if (GetChildren(item) is { } children)
            {
                Observe(children);
                foreach (var child in children)
                {
                    AddDataNode(child, node, ancestors);
                }
            }
        }
        finally
        {
            if (item is not null)
            {
                ancestors.Remove(item);
            }
        }
    }

    private void AddNode(NativeTreeNode node, NativeTreeNode? parent, object? item)
    {
        if (parent is null)
        {
            _tree.Nodes.Add(node);
        }
        else
        {
            parent.Nodes.Add(node);
        }

        _entries.Add(new NodeEntry(item, node, _flattened.Count));
        _flattened.Add(item);
    }

    private string BuildText(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (DisplayMemberPath is { Length: > 0 } path)
        {
            try
            {
                var bindingPath = new BindingPath(path);
                if (bindingPath.TryResolve(item, out var value, out _, out _))
                {
                    return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                }
            }
            catch (Exception)
            {
                // 显示路径写错时退回 ToString，不要让整棵树建不出来。
                return Convert.ToString(item, CultureInfo.CurrentCulture) ?? string.Empty;
            }
        }

        return Convert.ToString(item, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static IEnumerable? GetChildren(object? item)
    {
        if (item is null)
        {
            return null;
        }

        foreach (var name in ChildPropertyNames)
        {
            var descriptor = TypeDescriptor.GetProperties(item).Find(name, ignoreCase: false);
            if (descriptor is null
                || string.Equals(descriptor.Name, "Item", StringComparison.Ordinal))
            {
                continue;
            }

            object? candidate;
            try
            {
                candidate = descriptor.GetValue(item);
            }
            catch (Exception)
            {
                continue;
            }

            if (candidate is IEnumerable children && children is not string)
            {
                return children;
            }
        }

        return null;
    }

    private static readonly string[] ChildPropertyNames = ["Items", "Children"];

    private void Observe(IEnumerable source)
    {
        if (source is not INotifyCollectionChanged observable || _observedCollections.Contains(observable))
        {
            return;
        }

        _observedCollections.Add(observable);
        observable.CollectionChanged += OnObservedCollectionChanged;
    }

    private void ObserveAuthorItem(TreeViewItem item)
    {
        Observe(item.Items);

        if (_observedItems.Contains(item))
        {
            return;
        }

        _observedItems.Add(item);
        item.DependencyPropertyChanged += OnAuthoredItemPropertyChanged;
    }

    private void OnObservedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNodes();

    private void OnAuthoredItemPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_rebuilding || IsDisposed)
        {
            return;
        }

        if (ReferenceEquals(e.Property, TreeViewItem.HeaderProperty)
            || ReferenceEquals(e.Property, TreeViewItem.IsExpandedProperty))
        {
            RebuildNodes();
        }
    }

    private void DetachObservers()
    {
        foreach (var observable in _observedCollections)
        {
            observable.CollectionChanged -= OnObservedCollectionChanged;
        }

        _observedCollections.Clear();

        foreach (var item in _observedItems)
        {
            item.DependencyPropertyChanged -= OnAuthoredItemPropertyChanged;
        }

        _observedItems.Clear();
    }

    private void OnAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_syncing || IsDisposed)
        {
            return;
        }

        _syncing = true;
        try
        {
            var entry = FindEntryByNode(e.Node);
            SetValue(SelectedIndexProperty, entry?.Index ?? -1);
            SetValue(SelectedItemProperty, entry?.Item);
        }
        finally
        {
            _syncing = false;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySelectionFromProperties()
    {
        if (IsDisposed || _syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            NodeEntry? entry = null;
            if (SelectedItem is { } item)
            {
                entry = FindEntryByItem(item);
            }
            else if (SelectedIndex >= 0 && SelectedIndex < _entries.Count)
            {
                entry = _entries[SelectedIndex];
            }

            if (entry is null)
            {
                if (_tree.SelectedNode is not null)
                {
                    _tree.SelectedNode = null;
                }

                // 源里暂时没有这一项时保留调用方写的值；源已经建好节点后才清空失效选择。
                if (_entries.Count > 0 && SelectedItem is not null)
                {
                    SetValue(SelectedItemProperty, null);
                    SetValue(SelectedIndexProperty, -1);
                }

                return;
            }

            var selected = entry.GetValueOrDefault();
            if (!ReferenceEquals(_tree.SelectedNode, selected.Node))
            {
                _tree.SelectedNode = selected.Node;
            }

            if (SelectedIndex != selected.Index)
            {
                SetValue(SelectedIndexProperty, selected.Index);
            }

            if (!Equals(SelectedItem, selected.Item))
            {
                SetValue(SelectedItemProperty, selected.Item);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private NodeEntry? FindEntryByItem(object item)
    {
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Item, item))
            {
                return entry;
            }
        }

        foreach (var entry in _entries)
        {
            if (Equals(entry.Item, item))
            {
                return entry;
            }
        }

        return null;
    }

    private NodeEntry? FindEntryByNode(NativeTreeNode? node)
    {
        if (node is null)
        {
            return null;
        }

        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Node, node))
            {
                return entry;
            }
        }

        return null;
    }

    private static void OnItemsSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TreeView tree && !tree.IsDisposed)
        {
            tree.RebuildNodes();
        }
    }

    private static void OnStructurePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TreeView tree && !tree.IsDisposed)
        {
            tree.RebuildNodes();
        }
    }

    private static void OnSelectionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TreeView tree && !tree.IsDisposed && !tree._syncing)
        {
            tree.ApplySelectionFromProperties();
        }
    }

    private static void OnChromePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TreeView tree && !tree.IsDisposed)
        {
            tree.ApplyNativeAppearance();
        }
    }

    private readonly record struct NodeEntry(object? Item, NativeTreeNode Node, int Index);

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

#pragma warning disable CS0108, CS8767 // 接口签名是 object；object.Equals 的静态重载只是同名。
        public bool Equals(object x, object y) => ReferenceEquals(x, y);
#pragma warning restore CS0108, CS8767

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>
/// A hand-authored node of a <see cref="TreeView"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Header"/> is the text the native tree renders. Nested nodes go into <see cref="Items"/>; a
/// <see cref="TreeViewItem"/> is a <see cref="ContentControl"/>, so it accepts only one ordinary content child and
/// therefore cannot mirror a sibling list through direct children.
/// </para>
/// <para>Standalone, the inherited <see cref="ContentControl.Content"/> is what is drawn; the header is not copied there.</para>
/// </remarks>
public class TreeViewItem : ContentControl
{
    /// <summary>The text shown by the owning tree.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(TreeViewItem),
        new PropertyMetadata(string.Empty));

    /// <summary>Whether the owning tree expands this node when it is built.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(TreeViewItem),
        new PropertyMetadata(false));

    private readonly ObservableCollection<TreeViewItem> _items = new();

    /// <summary>Creates an empty tree item.</summary>
    public TreeViewItem()
    {
    }

    /// <summary>Gets or sets the text shown by the owning tree.</summary>
    public string Header
    {
        get => (string?)GetValue(HeaderProperty) ?? string.Empty;
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Gets or sets whether the owning tree expands this node when it is built.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The nested nodes.</summary>
    public IList<TreeViewItem> Items => _items;
}
