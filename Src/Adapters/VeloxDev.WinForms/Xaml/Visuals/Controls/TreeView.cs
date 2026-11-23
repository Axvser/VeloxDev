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
/// <para>
/// Without a control template the row is painted by its owning <see cref="TreeView"/>: the tree draws the selection
/// and hover bands and the item draws its header and disclosure triangle. A row never creates a window.
/// </para>
/// <para>
/// With a <see cref="TemplatedControl.Template"/> the row's template root replaces the self-drawn header. The template must contain a
/// part named <c>PART_Header</c>, which receives <see cref="Header"/>; an optional <c>PART_Expander</c> part is kept in
/// sync with <see cref="IsExpanded"/> and an optional <c>PART_ItemsPresenter</c> panel hosts this row's child rows.
/// <see cref="IsSelected"/>, <see cref="IsPointerOver"/> and <see cref="IsPressed"/> are dependency properties, so a
/// template trigger can paint the row's interactive state.
/// </para>
/// </remarks>
public class TreeViewItem : TemplatedControl
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

    /// <summary>Whether the row is the owning tree's selected row.</summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(TreeViewItem),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the pointer is over the row.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver), typeof(bool), typeof(TreeViewItem),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the left pointer button is held down on the row.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(TreeViewItem),
        new PropertyMetadata(false) { AffectsRender = true });

    private VisualElement? _headerPart;
    private VisualElement? _expanderPart;
    private Panel? _childPresenter;
    private bool _syncingExpander;
    private bool _hostsChildrenDirectly;
    private bool _disposing;
    private double _childIndent;

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

    /// <summary>Whether the row is the owning tree's selected row.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Whether the pointer is over the row.</summary>
    /// <remarks>This dependency property shadows <see cref="VisualElement.IsPointerOver"/> so a template trigger can
    /// observe the row's hover state.</remarks>
    public new bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>Whether the left pointer button is held down on the row.</summary>
    public bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>The child rows.</summary>
    public ObservableCollection<TreeViewItem> Items { get; } = [];

    /// <summary>The owning row, or <see langword="null"/> for a root.</summary>
    public TreeViewItem? ParentItem { get; internal set; }

    /// <summary>The nesting depth; assigned by the owning tree.</summary>
    public int Depth { get; internal set; }

    // 树在挂行时写回；模板部件、子行宿主与缩进都靠它。
    internal TreeView? OwnerTree { get; set; }

    internal VisualElement? ExpanderPart => _expanderPart;

    internal Panel? ChildPresenter => _childPresenter;

    internal bool HostsChildrenDirectly
    {
        get => _hostsChildrenDirectly;
        set => _hostsChildrenDirectly = value;
    }

    internal double ChildIndent
    {
        get => _childIndent;
        set => _childIndent = value;
    }

    internal void SetIsPointerOver(bool value) => SetValue(IsPointerOverProperty, value);

    internal void SetIsPressed(bool value) => SetValue(IsPressedProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsTemplated)
        {
            var root = MeasureTemplate(availableSize);
            var width = root.Width;
            var height = root.Height;
            if (_hostsChildrenDirectly && IsExpanded)
            {
                var indent = (int)Math.Ceiling(Math.Max(0d, _childIndent));
                foreach (var child in Items)
                {
                    var measured = child.Measure(new Size(Math.Max(0, availableSize.Width - indent), 0));
                    width = Math.Max(width, measured.Width + indent);
                    height += measured.Height;
                }
            }

            return new Size(width, height);
        }

        var header = MeasureHeader();
        if (!_hostsChildrenDirectly || !IsExpanded)
        {
            return header;
        }

        var widthSelf = header.Width;
        var heightSelf = header.Height;
        var childIndent = (int)Math.Ceiling(Math.Max(0d, _childIndent));
        foreach (var child in Items)
        {
            var measured = child.Measure(new Size(Math.Max(0, availableSize.Width - childIndent), 0));
            widthSelf = Math.Max(widthSelf, measured.Width + childIndent);
            heightSelf += measured.Height;
        }

        return new Size(widthSelf, heightSelf);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (IsTemplated)
        {
            var top = 0f;
            if (TemplateRoot is { } root)
            {
                top = Math.Min(finalSize.Height, Math.Max(0f, root.DesiredSize.Height));
                root.Arrange(new RectangleF(0f, 0f, finalSize.Width, top));
            }

            if (_hostsChildrenDirectly && IsExpanded)
            {
                ArrangeChildren(finalSize, top);
            }

            return;
        }

        if (_hostsChildrenDirectly && IsExpanded)
        {
            ArrangeChildren(finalSize, MeasureHeader().Height);
        }
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var headerHeight = MeasureHeader().Height;
        var expanderX = Padding.Left - 12f;
        var centerY = headerHeight / 2f;
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

        var font = ResolveFont(Font ?? OwnerTree?.Font ?? (ParentElement as TreeView)?.Font ?? DefaultFont);
        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0, (int)Math.Ceiling(Bounds.Width - Padding.Horizontal)),
            Math.Max(0, (int)Math.Ceiling(headerHeight - Padding.Vertical)));
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

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, HeaderProperty))
        {
            UpdateHeaderPart();
        }
        else if (ReferenceEquals(e.Property, IsExpandedProperty))
        {
            SyncExpanderState();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        SetIsPointerOver(true);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        SetIsPointerOver(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 释放时保持子行挂在旧模板根里，让基类的 Dispose 顺着父子链回收它们。
            _disposing = true;
            if (_expanderPart is { } expander)
            {
                expander.PropertyChanged -= OnExpanderPropertyChanged;
            }

            OwnerTree = null;
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging()
    {
        base.OnTemplateChanging();

        if (_expanderPart is { } expander)
        {
            expander.PropertyChanged -= OnExpanderPropertyChanged;
        }

        if (!_disposing)
        {
            DetachHostedChildren();
        }

        _headerPart = null;
        _expanderPart = null;
        _childPresenter = null;
        _hostsChildrenDirectly = false;
        _childIndent = 0d;
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged()
    {
        base.OnTemplateChanged();

        try
        {
            if (IsTemplated)
            {
                AttachTemplateParts();
            }
        }
        catch
        {
            // 模板无效时把子行交还树，至少 Dispose 能顺着父子链回收它们。
            OwnerTree?.OnItemTemplateChanged();
            throw;
        }

        // 模板换了，子行该落在新的宿主里；交给树重排一次。
        OwnerTree?.OnItemTemplateChanged();
        InvalidateMeasure();
    }

    private Size MeasureHeader()
    {
        var font = ResolveFont(Font ?? OwnerTree?.Font ?? (ParentElement as TreeView)?.Font ?? DefaultFont);
        var measured = VisualText.Measure(Header, font, 0f, wrap: false, RenderDpi);
        return new Size(
            (int)Math.Ceiling(measured.Width + Padding.Horizontal),
            (int)Math.Ceiling(measured.Height + Padding.Vertical));
    }

    private void ArrangeChildren(Size finalSize, float top)
    {
        var indent = (float)Math.Max(0d, _childIndent);
        var y = top;
        foreach (var child in Items)
        {
            var height = child.DesiredSize.Height;
            child.Arrange(new RectangleF(indent, y, Math.Max(0f, finalSize.Width - indent), height));
            y += height;
        }
    }

    private void AttachTemplateParts()
    {
        var header = GetTemplateChild<VisualElement>("PART_Header");
        if (header is null)
        {
            throw new InvalidOperationException(
                "TreeViewItem ControlTemplate must contain a part named 'PART_Header'.");
        }

        _headerPart = header;
        _expanderPart = GetTemplateChild<VisualElement>("PART_Expander");
        _childPresenter = GetTemplateChild<Panel>("PART_ItemsPresenter")
            ?? TreeView.FindItemsHost(TemplateRoot);

        UpdateHeaderPart();
        if (_expanderPart is not null)
        {
            _expanderPart.PropertyChanged += OnExpanderPropertyChanged;
        }

        SyncExpanderState();
    }

    private void UpdateHeaderPart()
    {
        if (_headerPart is null)
        {
            return;
        }

        switch (_headerPart)
        {
            case TextBlock text:
                text.Text = Header;
                break;
            case ContentPresenter presenter:
                presenter.Content = Header;
                break;
            case ContentControl control:
                control.Content = Header;
                break;
        }
    }

    // 展开器同步只对自身带 IsChecked 的开关类型生效；普通部件由树按命中区域切换。
    private void SyncExpanderState()
    {
        if (_expanderPart is null)
        {
            return;
        }

        _syncingExpander = true;
        try
        {
            switch (_expanderPart)
            {
                case ToggleButton toggle:
                    toggle.IsChecked = IsExpanded;
                    break;
                case CheckBox check:
                    check.IsChecked = IsExpanded;
                    break;
            }
        }
        finally
        {
            _syncingExpander = false;
        }
    }

    private void OnExpanderPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_syncingExpander)
        {
            return;
        }

        if (ReferenceEquals(e.Property, ToggleButton.IsCheckedProperty)
            || ReferenceEquals(e.Property, CheckBox.IsCheckedProperty))
        {
            IsExpanded = e.NewValue is true;
        }
    }

    // 旧模板根马上要被释放：先把挂在它里面的子行摘出来，否则会被一并 Dispose。
    private void DetachHostedChildren()
    {
        foreach (var child in Items)
        {
            if (child.ParentElement is not { } parent)
            {
                continue;
            }

            for (var current = parent; current is not null; current = current.ParentElement)
            {
                if (ReferenceEquals(current, this))
                {
                    parent.RemoveChild(child);
                    break;
                }
            }
        }
    }
}

/// <summary>A hierarchical list of <see cref="TreeViewItem"/> rows with selection, expansion and keyboard navigation.</summary>
/// <remarks>
/// <para>
/// Without a control template only expanded rows are realized as direct children, and the tree arranges them in a flat
/// row order. Put it in a <see cref="ScrollViewer"/> for long directories. Expansion, selection and collection changes
/// rebuild that flat order and invalidate the measure.
/// </para>
/// <para>
/// With a <see cref="TemplatedControl.Template"/> the template root must contain a <c>PART_ItemsPresenter</c> part or a
/// panel marked with <see cref="ItemsControl.SetIsItemsHost"/>; root rows are hosted there. Each row hosts its own
/// children in the row template's <c>PART_ItemsPresenter</c> when it has one, otherwise in the row element itself.
/// The tree does not virtualize rows: a very large expanded tree realizes every visible row.
/// </para>
/// <para>
/// The tree owns the row visuals it shows: an item can be collapsed and shown again, but it should not be shared by
/// two trees at the same time.
/// </para>
/// </remarks>
public class TreeView : TemplatedControl
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
    private readonly HashSet<TreeViewItem> _attached = [];
    private TreeViewItem? _hover;
    private TreeViewItem? _pressedRow;
    private Panel? _itemsHost;
    private bool _rebuilding;
    private bool _disposing;

    /// <summary>Creates a focusable tree with the current theme font.</summary>
    public TreeView()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(0f);
        Items.CollectionChanged += OnRootItemsChanged;
        // 预览阶段先截住展开器：模板里用 ToggleButton 当展开器时也要由树统一切换。
        PreviewPointerPressed += OnPreviewPointerPressed;
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
        get => ResolveFont((Font)GetValue(FontProperty)!);
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
        if (IsTemplated)
        {
            EnsureItemsHost();
            return MeasureTemplate(availableSize);
        }

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
        if (IsTemplated)
        {
            EnsureItemsHost();
            ArrangeTemplate(finalSize);
            return;
        }

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

        // 模板负责整行外观（含选中/悬停），树的合奏带必须退出。
        if (IsTemplated)
        {
            return;
        }

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

        if (IsFocusVisible && SelectedItem is { } selectedRow)
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

        var row = RowAtSurface(args.Position);
        if (row is null)
        {
            return;
        }

        Focus();
        SetPressedRow(row);
        if (row.Items.Count > 0 && IsExpanderHit(row, args.Position))
        {
            row.IsExpanded = !row.IsExpanded;
        }
        else
        {
            SelectedItem = row;
        }

        args.Handled = true;
    }

    // 展开器在按钮自己处理按下之前先切换；否则 ToggleButton 只会改自己的 IsChecked。
    private void OnPreviewPointerPressed(object? sender, VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var row = RowAtSurface(args.Position);
        if (row is null || row.Items.Count == 0 || !IsExpanderHit(row, args.Position))
        {
            return;
        }

        Focus();
        SetPressedRow(row);
        row.IsExpanded = !row.IsExpanded;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        if (_pressedRow is { } row)
        {
            row.SetIsPressed(false);
            _pressedRow = null;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var row = RowAtSurface(args.Position);
        if (ReferenceEquals(row, _hover))
        {
            return;
        }

        _hover?.SetIsPointerOver(false);
        _hover = row;
        _hover?.SetIsPointerOver(true);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        if (_hover is not null)
        {
            _hover.SetIsPointerOver(false);
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
            if (e.OldValue is TreeViewItem old)
            {
                old.IsSelected = false;
            }

            if (e.NewValue is TreeViewItem next)
            {
                next.IsSelected = true;
                ScrollIntoView(next);
            }

            SyncSelectionState();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (ReferenceEquals(e.Property, IndentProperty))
        {
            foreach (var item in _observing)
            {
                item.ChildIndent = Indent;
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 释放时不要摘行：让基类顺着旧的模板根/直接子项回收它们。
            _disposing = true;
            Items.CollectionChanged -= OnRootItemsChanged;
            foreach (var item in _observing)
            {
                Observe(item, subscribe: false);
            }

            _observing.Clear();
            _attached.Clear();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging()
    {
        base.OnTemplateChanging();

        // 模板根马上要被释放：行先摘出来，OnTemplateChanged 再挂到新宿主。
        if (!_disposing)
        {
            DetachAttachedRows();
        }

        _itemsHost = null;
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged()
    {
        base.OnTemplateChanged();

        if (IsTemplated)
        {
            try
            {
                EnsureItemsHost();
            }
            catch
            {
                // 模板无效时仍把行挂回树上，Dispose 才能顺着父子链回收。
                foreach (var row in _rows)
                {
                    DetachFromCurrentParent(row);
                    Add(row);
                    _attached.Add(row);
                }

                throw;
            }
        }

        Rebuild();
    }

    // 行的模板换了：子行要按新的宿主重挂，整棵树重算一次。
    internal void OnItemTemplateChanged()
    {
        if (!_rebuilding)
        {
            Rebuild();
        }
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
            DetachAttachedRows();
            if (IsTemplated)
            {
                var host = EnsureItemsHost();
                foreach (var item in Items)
                {
                    AttachSubtree(item, host);
                }
            }
            else
            {
                // 旧路径会把树的子项里所有行清掉；保持这个行为，外部挂进来的行也一并回收。
                foreach (var child in Children.ToArray())
                {
                    if (child is TreeViewItem stray)
                    {
                        DetachFromCurrentParent(stray);
                    }
                }

                foreach (var row in _rows)
                {
                    // 旧路径由树统一摆放所有行；行自己不再托管子行。
                    row.HostsChildrenDirectly = false;
                    row.ChildIndent = 0d;
                    DetachFromCurrentParent(row);
                    Add(row);
                    _attached.Add(row);
                }
            }
        }
        finally
        {
            _rebuilding = false;
        }

        if (_hover is { } hover && !_rows.Contains(hover))
        {
            hover.SetIsPointerOver(false);
            _hover = null;
        }

        if (_pressedRow is { } pressed && !_rows.Contains(pressed))
        {
            pressed.SetIsPressed(false);
            _pressedRow = null;
        }

        InvalidateMeasure();
        SyncSelectionState();
        if (SelectedItem is { } selected && !_rows.Contains(selected))
        {
            SelectedItem = null;
        }
    }

    // 模板模式按行递归挂载：根行进模板宿主，子行进父行自己的 PART_ItemsPresenter。
    private void AttachSubtree(TreeViewItem item, Panel host)
    {
        DetachFromCurrentParent(item);
        host.Add(item);
        _attached.Add(item);
        if (!item.IsExpanded || item.Items.Count == 0)
        {
            return;
        }

        var childHost = ResolveChildHost(item);
        foreach (var child in item.Items)
        {
            AttachSubtree(child, childHost);
        }
    }

    private Panel ResolveChildHost(TreeViewItem item)
    {
        if (item.ChildPresenter is { } presenter)
        {
            item.HostsChildrenDirectly = false;
            item.ChildIndent = 0d;
            return presenter;
        }

        item.HostsChildrenDirectly = true;
        item.ChildIndent = Indent;
        return item;
    }

    private void DetachAttachedRows()
    {
        foreach (var row in _attached)
        {
            DetachFromCurrentParent(row);
        }

        _attached.Clear();
    }

    private static void DetachFromCurrentParent(TreeViewItem row)
    {
        if (row.ParentElement is { } parent && parent.Children.Contains(row))
        {
            parent.RemoveChild(row);
        }
    }

    private Panel EnsureItemsHost()
    {
        if (_itemsHost is not null)
        {
            return _itemsHost;
        }

        _itemsHost = GetTemplateChild<Panel>("PART_ItemsPresenter")
            ?? FindItemsHost(TemplateRoot);
        if (_itemsHost is null)
        {
            throw new InvalidOperationException(
                "TreeView ControlTemplate must contain a part named 'PART_ItemsPresenter' or a panel marked "
                + "with ItemsControl.IsItemsHost.");
        }

        return _itemsHost;
    }

    // 行模板里的子行宿主：PART_ItemsPresenter 优先，其次任一 ItemsPresenter/IsItemsHost 面板。
    internal static Panel? FindItemsHost(VisualElement? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ItemsPresenter presenter)
        {
            return presenter;
        }

        if (root is Panel panel && ItemsControl.GetIsItemsHost(panel))
        {
            return panel;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindItemsHost(element) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void SyncSelectionState()
    {
        foreach (var item in _observing)
        {
            item.IsSelected = ReferenceEquals(item, SelectedItem);
        }
    }

    private void SetPressedRow(TreeViewItem row)
    {
        if (ReferenceEquals(_pressedRow, row))
        {
            return;
        }

        _pressedRow?.SetIsPressed(false);
        _pressedRow = row;
        row.SetIsPressed(true);
    }

    private static bool IsExpanderHit(TreeViewItem row, PointF surfacePoint)
    {
        if (row.ExpanderPart is { } expander)
        {
            var bounds = expander.AbsoluteBounds;
            if (bounds.Width > 0f && bounds.Height > 0f)
            {
                return bounds.Contains(surfacePoint);
            }
        }

        // 模板行没有展开器部件时不猜区域；没有模板的行仍退回旧的自绘三角命中区。
        if (row.TemplateRoot is not null)
        {
            return false;
        }

        var left = row.AbsoluteBounds.X;
        return surfacePoint.X >= left - 4f && surfacePoint.X <= left + 16f;
    }

    private void Flatten(TreeViewItem item, int depth)
    {
        item.Depth = depth;
        item.OwnerTree = this;
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
            item.OwnerTree = this;
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

    private TreeViewItem? RowAtSurface(PointF surfacePoint)
    {
        // 模板模式下父行的 Bounds 包住子行；倒序先命中更深的行。
        for (var index = _rows.Count - 1; index >= 0; index--)
        {
            var row = _rows[index];
            if (row.AbsoluteBounds.Contains(surfacePoint))
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
