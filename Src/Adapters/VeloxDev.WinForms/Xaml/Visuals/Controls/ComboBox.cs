using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using VisualButton = VeloxDev.WinForms.Xaml.Visuals.Controls.Button;
using VisualTextBox = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A drop-down selector whose chrome can be replaced by a <see cref="VisualControlTemplate"/>.</summary>
/// <remarks>
/// <para>
/// Without a template the closed control draws the selected item itself. Opening moves a <see cref="VisualPopup"/>
/// holding a <see cref="ScrollViewer"/> and a <see cref="ListBox"/> into the surface overlay, so the list is neither
/// clipped by an ancestor <see cref="ScrollViewer"/> nor painted under later siblings.
/// </para>
/// <para>
/// Logical focus stays on the combo box while the drop-down is open: the list and its viewer are not focusable, and
/// Up/Down/Home/End/PageUp/PageDown are forwarded to them. Enter commits and closes, Escape restores the selection
/// captured at open and closes.
/// </para>
/// <para>
/// With a template the self-drawn chrome is suppressed. The template must declare <c>PART_Popup</c> (a
/// <see cref="VisualPopup"/>) and an items host named <c>PART_ItemsPresenter</c> or marked with
/// <see cref="ItemsControl.SetIsItemsHost"/>; the realized list is parented into that host. An editable template may
/// declare <c>PART_EditableTextBox</c> (a <see cref="VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox"/>) and a toggle
/// named <c>PART_ToggleButton</c> or
/// <c>PART_Arrow</c>. A missing required part throws <see cref="InvalidOperationException"/> instead of leaving the
/// chrome unchanged.
/// </para>
/// </remarks>
public class ComboBox : TemplatedControl
{
    /// <summary>Whether the text may be edited in the template's <c>PART_EditableTextBox</c>.</summary>
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(ComboBox),
        new PropertyMetadata(false) { AffectsMeasure = false, AffectsRender = false });

    /// <summary>The editable text, kept in sync with the selected item.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ComboBox),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether the pointer is currently over the control; template triggers can observe it.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver), typeof(bool), typeof(ComboBox),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The sequence whose items fill the drop-down.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text colour as a brush value; an empty colour uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(XamlBrush), typeof(ComboBox),
        new PropertyMetadata(null, null, static (_, value) => ColorBrushCoercion.Coerce(value))
        {
            AffectsRender = true,
        });

    /// <summary>Creates an item visual; <see langword="null"/> projects each item to a <see cref="TextBlock"/>.</summary>
    public static readonly DependencyProperty ItemFactoryProperty = DependencyProperty.Register(
        nameof(ItemFactory), typeof(Func<object, VisualElement>), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The zero-based index of the selected item, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(ComboBox),
        new PropertyMetadata(-1) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The selected source item, or <see langword="null"/> when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>Whether the drop-down is open.</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(ComboBox),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The property read from each item for its display text; empty uses <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(ComboBox),
        new PropertyMetadata(string.Empty) { AffectsRender = true });

    /// <summary>The largest drop-down height, in pixels.</summary>
    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight), typeof(double), typeof(ComboBox),
        new PropertyMetadata(200d) { AffectsRender = false });

    /// <summary>The text drawn while nothing is selected.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ComboBox),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    private readonly ObservableCollection<object?> _items = [];
    private readonly ListBox _list = new();
    private readonly ScrollViewer _scroll = new();
    private readonly VisualPopup _popup = new();
    private VisualPopup? _templatePopup;
    private ScrollViewer? _templateScroll;
    private VisualTextBox? _editablePart;
    private VisualButton? _toggleButton;
    private ToggleButton? _toggleSwitch;
    private bool _syncingSelection;
    private bool _syncingOpen;
    private bool _syncingText;
    private int _selectionAtOpen = -1;

    /// <summary>Creates an empty, focusable selector with theme metrics.</summary>
    public ComboBox()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        Padding = new XamlThickness(10f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
        _items.CollectionChanged += OnItemsCollectionChanged;

        _list.Focusable = false;
        _list.ItemFactory = CreateItemVisual;
        _list.ItemsSource = _items;
        _list.SelectionChanged += OnListSelectionChanged;
        _list.ItemsRebuilt += OnListItemsRebuilt;
        _list.PointerPressed += OnListPointerPressed;

        _scroll.Focusable = false;
        _scroll.MaxHeight = 200d;
        _scroll.Content = _list;

        _popup.Child = _scroll;
        _popup.PlacementTarget = this;
        _popup.Placement = VisualPlacement.Bottom;
        _popup.StaysOpen = false;
        _popup.Closed += OnPopupClosed;
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> or <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The sequence whose items fill the drop-down; it wins over <see cref="Items"/>.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Creates an item visual; <see langword="null"/> projects each item to a <see cref="TextBlock"/>.</summary>
    public Func<object, VisualElement>? ItemFactory
    {
        get => (Func<object, VisualElement>?)GetValue(ItemFactoryProperty);
        set => SetValue(ItemFactoryProperty, value);
    }

    /// <summary>Items declared inline in XAML or added from code; used when <see cref="ItemsSource"/> is empty.</summary>
    public IList Items => _items;

    /// <summary>The zero-based index of the selected item, or -1 when nothing is selected.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The selected source item, or <see langword="null"/> when nothing is selected.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Whether the drop-down is open.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The property read from each item for its display text; empty uses <see cref="object.ToString"/>.</summary>
    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty)!;
        set => SetValue(DisplayMemberPathProperty, value ?? string.Empty);
    }

    /// <summary>The largest drop-down height, in pixels.</summary>
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty)!;
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>The text drawn while nothing is selected.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty)!;
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>Whether the text may be edited in the template's <c>PART_EditableTextBox</c>.</summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty)!;
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>The editable text; a selected item's display text wins when the selection changes.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty)!;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>Whether the pointer is currently over the control; template triggers can observe it.</summary>
    /// <remarks>Shadows <see cref="VisualElement.IsPointerOver"/> so a template trigger can observe hover.</remarks>
    public new bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>The realized drop-down list; exposed for tests and hosts.</summary>
    public ListBox DropDownList => _list;

    /// <summary>The template's editable text field, or <see langword="null"/> when the template declares none.</summary>
    public VisualTextBox? EditableTextBox => _editablePart;

    /// <summary>The template's drop-down popup, or <see langword="null"/> while the control draws itself.</summary>
    public VisualPopup? TemplatePopup => _templatePopup;

    /// <summary>The text and chevron colour; an empty colour uses the active theme.</summary>
    /// <remarks>
    /// The dependency property stores a brush, so a hex string, a <see cref="Color"/> or a brush is accepted. A
    /// TwoWay binding writes the brush back, so a <see cref="Color"/> source needs a converter on the write-back path.
    /// </remarks>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty) is XamlBrush brush ? brush.ApproximateColor : Color.Empty;
        set
        {
            if (value.IsEmpty)
            {
                ClearValue(ForegroundProperty);
            }
            else
            {
                SetValue(ForegroundProperty, new SolidColorBrush(value));
            }
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsTemplated)
        {
            return MeasureTemplate(availableSize);
        }

        var text = string.IsNullOrEmpty(DisplayText) ? "M" : DisplayText;
        var measured = VisualText.Measure(text, ResolveFont(DefaultFont), 0f, wrap: false, RenderDpi);
        var width = (int)Math.Ceiling(measured.Width + Padding.Horizontal + 26);
        var height = Math.Max(
            Theme.Current.ControlHeight,
            (int)Math.Ceiling(measured.Height + Padding.Vertical + 2));
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (IsTemplated)
        {
            // 模板根负责整块外观。
            return;
        }

        var theme = Theme.Current;
        var background = Background?.ApproximateColor
            ?? (!IsEnabled
                ? theme.DisabledBackground
                : IsPointerOver || IsDropDownOpen
                    ? theme.ControlHover
                    : theme.ControlBackground);
        var border = BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var thickness = BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness;
        var radius = CornerRadius > 0 ? CornerRadius : theme.CornerRadius;
        XamlDrawing.PaintChrome(graphics, bounds, background, border, thickness, radius);

        if (IsFocusVisible)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, radius - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        var chevronRight = bounds.Width - 12f;
        var chevronCenter = bounds.Height / 2f;
        var foreground = !IsEnabled
            ? theme.DisabledForeground
            : Foreground.IsEmpty ? theme.Foreground : Foreground;
        using (var chevron = new SolidBrush(foreground))
        {
            graphics.FillPolygon(chevron, new[]
            {
                new PointF(chevronRight - 4f, chevronCenter - 2f),
                new PointF(chevronRight + 4f, chevronCenter - 2f),
                new PointF(chevronRight, chevronCenter + 3f),
            });
        }

        var text = DisplayText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 18),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical)));
        VisualText.Draw(this,
            graphics,
            text,
            ResolveFont(DefaultFont),
            foreground,
            content,
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        SetValue(IsPointerOverProperty, true);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        SetValue(IsPointerOverProperty, false);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        IsDropDownOpen = !IsDropDownOpen;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (IsEditable && _editablePart is not null
            && args.KeyCode == Keys.Enter && TryCommitEditableText())
        {
            args.Handled = true;
            return;
        }

        switch (args.KeyCode)
        {
            case Keys.Down when (args.Modifiers & Keys.Alt) != 0:
            case Keys.F4:
                IsDropDownOpen = true;
                args.Handled = true;
                break;

            case Keys.Up when (args.Modifiers & Keys.Alt) != 0:
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Escape when IsDropDownOpen:
                RestoreSelectionAtOpen();
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Enter when IsDropDownOpen:
            case Keys.Space when IsDropDownOpen:
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Down or Keys.Up or Keys.Home or Keys.End when IsDropDownOpen:
                _list.RaiseKeyDown(args);
                ScrollSelectionIntoView();
                break;

            case Keys.PageUp or Keys.PageDown when IsDropDownOpen:
                ActiveScroll?.RaiseKeyDown(args);
                break;

            case Keys.Down:
                MoveSelection(1);
                args.Handled = true;
                break;

            case Keys.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, IsEditableProperty))
        {
            if (IsEditable && IsTemplated && _editablePart is null)
            {
                throw MissingPart("PART_EditableTextBox", "TextBox");
            }

            SyncEditableText();
            return;
        }

        if (ReferenceEquals(e.Property, TextProperty))
        {
            if (!_syncingText && _editablePart is not null && _editablePart.Text != Text)
            {
                _syncingText = true;
                try
                {
                    _editablePart.Text = Text;
                }
                finally
                {
                    _syncingText = false;
                }
            }

            return;
        }

        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            _list.ItemsSource = ItemsSource ?? _items;
            return;
        }

        if (ReferenceEquals(e.Property, ItemFactoryProperty))
        {
            _list.ItemFactory = ItemFactory ?? CreateItemVisual;
            _list.RefreshItems();
            return;
        }

        if (ReferenceEquals(e.Property, DisplayMemberPathProperty))
        {
            _list.RefreshItems();
            InvalidateMeasure();
            return;
        }

        if (ReferenceEquals(e.Property, MaxDropDownHeightProperty))
        {
            var maxHeight = Math.Max(Theme.Current.ControlHeight, MaxDropDownHeight);
            _scroll.MaxHeight = maxHeight;
            if (_templateScroll is not null)
            {
                _templateScroll.MaxHeight = maxHeight;
            }

            if (IsDropDownOpen)
            {
                ActivePopup.Reposition();
            }

            return;
        }

        if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            SyncSelectedFromIndex();
            SyncListSelection();
            SyncEditableText();
            InvalidateMeasure();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ReferenceEquals(e.Property, SelectedItemProperty))
        {
            SyncSelectedIndexFromItem();
            SyncListSelection();
            SyncEditableText();
            InvalidateMeasure();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ReferenceEquals(e.Property, IsDropDownOpenProperty))
        {
            if (_syncingOpen)
            {
                return;
            }

            if (IsDropDownOpen)
            {
                _selectionAtOpen = SelectedIndex;
                SyncListSelection();
                ActivePopup.IsOpen = true;
                ScrollSelectionIntoView();
            }
            else
            {
                ActivePopup.IsOpen = false;
            }

            SyncToggleChecked();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _items.CollectionChanged -= OnItemsCollectionChanged;
            _list.SelectionChanged -= OnListSelectionChanged;
            _list.ItemsRebuilt -= OnListItemsRebuilt;
            _list.PointerPressed -= OnListPointerPressed;
            _popup.Closed -= OnPopupClosed;
        }

        // 先让基类拆模板（会把列表送回 _scroll），再释放控件自己持有的弹层与列表。
        base.Dispose(disposing);
        if (disposing)
        {
            DetachTemplateParts();
            _popup.Dispose();
        }
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging()
    {
        // 模板根销毁前先收掉开放的弹层与挂在模板树里的列表。
        var active = ActivePopup;
        if (active.IsOpen)
        {
            active.IsOpen = false;
        }

        if (IsDropDownOpen)
        {
            _syncingOpen = true;
            try
            {
                IsDropDownOpen = false;
            }
            finally
            {
                _syncingOpen = false;
            }
        }

        DetachTemplateParts();
        base.OnTemplateChanging();
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged()
    {
        base.OnTemplateChanged();

        if (IsTemplated)
        {
            ResolveTemplateParts();
            return;
        }

        RestoreUntemplatedList();
    }

    private VisualPopup ActivePopup => _templatePopup ?? _popup;

    private ScrollViewer ActiveScroll => _templateScroll ?? _scroll;

    private string DisplayText
    {
        get
        {
            var item = SelectedItem;
            if (item is not null)
            {
                return FormatItem(item);
            }

            return Placeholder;
        }
    }

    // ItemsSource 非空时压过 Items；ListBox 直接观察这个集合，集合变化会重建行。
    private IEnumerable EffectiveItems => ItemsSource ?? _items;

    private void ResolveTemplateParts()
    {
        var popupElement = GetTemplateChild<VisualElement>("PART_Popup");
        if (popupElement is null)
        {
            throw MissingPart("PART_Popup", "VisualPopup");
        }

        if (popupElement is not VisualPopup popup)
        {
            throw WrongPart("PART_Popup", popupElement, "VisualPopup");
        }

        _templatePopup = popup;

        // 弹层一旦打开会把自己挂到表面的 overlay；模板树里不能再留着同一个父级。
        popup.ParentElement?.RemoveChild(popup);
        popup.PlacementTarget = this;
        popup.Placement = VisualPlacement.Bottom;
        popup.StaysOpen = false;
        popup.MatchTargetWidth = true;
        popup.Closed -= OnPopupClosed;
        popup.Closed += OnPopupClosed;

        var host = ResolveItemsHost(popup);
        if (host is null)
        {
            throw MissingPart("PART_ItemsPresenter", "ItemsPresenter or a panel marked IsItemsHost");
        }

        if (popup.Child is null)
        {
            host.ParentElement?.RemoveChild(host);
            popup.Child = host;
        }
        else if (popup.Child is { } content
                 && !ReferenceEquals(host, content)
                 && !IsDescendant(host, content))
        {
            if (host.ParentElement is not null)
            {
                throw new InvalidOperationException(
                    "ComboBox requires 'PART_ItemsPresenter' to live inside the 'PART_Popup' content; "
                    + "move the presenter into the popup's Child.");
            }

            if (content is Panel panel)
            {
                panel.Add(host);
            }
            else
            {
                throw new InvalidOperationException(
                    "ComboBox requires the 'PART_Popup' Child to be a Panel when 'PART_ItemsPresenter' is declared "
                    + "outside it; set the popup's Child to the panel that contains the presenter.");
            }
        }

        AttachList(host);
        _templateScroll = FindAncestor<ScrollViewer>(host);
        if (_templateScroll is not null)
        {
            _templateScroll.MaxHeight = Math.Max(Theme.Current.ControlHeight, MaxDropDownHeight);
        }

        AttachEditable();
        AttachToggle();
    }

    private Panel? ResolveItemsHost(VisualPopup popup)
    {
        var named = GetTemplateChild<VisualElement>("PART_ItemsPresenter");
        if (named is not null)
        {
            return named as Panel
                ?? throw WrongPart("PART_ItemsPresenter", named, "Panel");
        }

        return FindItemsHost(TemplateRoot) ?? FindItemsHost(popup.Child);
    }

    private void AttachList(Panel host)
    {
        if (ReferenceEquals(_list.ParentElement, host))
        {
            return;
        }

        // 滚动视图用默认模板的 ContentPresenter 承载内容；先清空 Content 让 presenter 放掉列表。
        if (ReferenceEquals(_scroll.Content, _list))
        {
            _scroll.Content = null;
        }

        _list.ParentElement?.RemoveChild(_list);
        host.Add(_list);
    }

    private void AttachEditable()
    {
        var part = GetTemplateChild<VisualElement>("PART_EditableTextBox");
        if (part is null)
        {
            if (IsEditable)
            {
                throw MissingPart("PART_EditableTextBox", "TextBox");
            }

            return;
        }

        if (part is not VisualTextBox editable)
        {
            throw WrongPart("PART_EditableTextBox", part, "TextBox");
        }

        _editablePart = editable;
        _editablePart.PropertyChanged -= OnEditablePropertyChanged;
        _editablePart.PropertyChanged += OnEditablePropertyChanged;
        SyncEditableText();
    }

    private void AttachToggle()
    {
        var part = GetTemplateChild<VisualElement>("PART_ToggleButton")
            ?? GetTemplateChild<VisualElement>("PART_Arrow");
        switch (part)
        {
            case VisualButton button:
                _toggleButton = button;
                button.Click -= OnToggleClick;
                button.Click += OnToggleClick;
                break;
            case ToggleButton toggle:
                _toggleSwitch = toggle;
                toggle.PropertyChanged -= OnTogglePropertyChanged;
                toggle.PropertyChanged += OnTogglePropertyChanged;
                SyncToggleChecked();
                break;
            case ButtonBase other:
                throw new InvalidOperationException(
                    $"ComboBox toggle part '{other.GetType().Name}' must be a Button or ToggleButton so the "
                    + "drop-down state can be driven.");
        }
    }

    private void DetachTemplateParts()
    {
        DetachEditable();
        DetachToggle();
        RestoreUntemplatedList();

        if (_templatePopup is { } popup)
        {
            popup.Closed -= OnPopupClosed;
            if (popup.IsOpen)
            {
                popup.IsOpen = false;
            }

            popup.ParentElement?.RemoveChild(popup);
            popup.Dispose();
            _templatePopup = null;
        }

        _templateScroll = null;
    }

    private void DetachEditable()
    {
        if (_editablePart is { } editable)
        {
            editable.PropertyChanged -= OnEditablePropertyChanged;
            _editablePart = null;
        }
    }

    private void DetachToggle()
    {
        if (_toggleButton is { } button)
        {
            button.Click -= OnToggleClick;
            _toggleButton = null;
        }

        if (_toggleSwitch is { } toggle)
        {
            toggle.PropertyChanged -= OnTogglePropertyChanged;
            _toggleSwitch = null;
        }
    }

    private void RestoreUntemplatedList()
    {
        for (var current = _list.ParentElement; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, _scroll))
            {
                return;
            }
        }

        _list.ParentElement?.RemoveChild(_list);
        // Content 值可能本来就是列表；先清空再写回，才会重走 ContentControl 的呈现路径。
        _scroll.Content = null;
        _scroll.Content = _list;
    }

    private void SyncEditableText()
    {
        if (_editablePart is null)
        {
            return;
        }

        _syncingText = true;
        try
        {
            _editablePart.Text = DisplayText;
        }
        finally
        {
            _syncingText = false;
        }
    }

    private void SyncToggleChecked()
    {
        if (_toggleSwitch is null)
        {
            return;
        }

        _syncingOpen = true;
        try
        {
            _toggleSwitch.IsChecked = IsDropDownOpen;
        }
        finally
        {
            _syncingOpen = false;
        }
    }

    private void OnEditablePropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_syncingText || !ReferenceEquals(e.Property, VisualTextBox.TextProperty))
        {
            return;
        }

        var text = _editablePart?.Text ?? string.Empty;
        if (!string.Equals(Text, text, StringComparison.Ordinal))
        {
            _syncingText = true;
            try
            {
                Text = text;
            }
            finally
            {
                _syncingText = false;
            }
        }
    }

    private bool TryCommitEditableText()
    {
        var text = _editablePart?.Text;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var items = EffectiveItems;
        var count = Count(items);
        for (var index = 0; index < count; index++)
        {
            if (ItemAt(items, index) is { } item
                && string.Equals(FormatItem(item), text, StringComparison.CurrentCultureIgnoreCase))
            {
                SelectedIndex = index;
                IsDropDownOpen = false;
                return true;
            }
        }

        return false;
    }

    private void OnToggleClick(object? sender, EventArgs e)
    {
        Focus();
        IsDropDownOpen = !IsDropDownOpen;
    }

    private void OnTogglePropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_syncingOpen || !ReferenceEquals(e.Property, ToggleButton.IsCheckedProperty))
        {
            return;
        }

        IsDropDownOpen = _toggleSwitch?.IsChecked == true;
    }

    private static Panel? FindItemsHost(VisualElement? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is Panel panel && (root is ItemsPresenter || ItemsControl.GetIsItemsHost(panel)))
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

    private static bool IsDescendant(VisualElement element, VisualElement ancestor)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(VisualElement element) where T : class
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static InvalidOperationException MissingPart(string name, string expected)
        => new($"The ComboBox ControlTemplate must declare a part named '{name}' ({expected}).");

    private static InvalidOperationException WrongPart(string name, object part, string expected)
        => new(
            $"The ComboBox ControlTemplate part '{name}' must be a {expected}, but was '{part.GetType().Name}'.");

    private VisualElement CreateItemVisual(object item)
    {
        if (ItemFactory is { } factory)
        {
            return factory(item);
        }

        return item as VisualElement
            ?? new TextBlock
            {
                Text = FormatItem(item),
                Padding = new XamlThickness(8f, 4f),
            };
    }

    private string FormatItem(object item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var property = item.GetType().GetProperty(
                DisplayMemberPath,
                BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(item) is { } value)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ItemsSource is not null)
        {
            return;
        }

        _list.ItemsSource = _items;

        // 选择可能早于条目添加（XAML/初始化器先写 SelectedIndex）；条目够到索引后再解析。
        if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
        {
            SyncSelectedFromIndex();
        }
        else if (SelectedIndex >= 0 && SelectedItem is not null)
        {
            // 集合缩短后原来的选中项已经不存在，别留下一个指向空位的高亮。
            SelectedIndex = -1;
        }
    }

    private void OnListItemsRebuilt(object? sender, EventArgs e)
    {
        if (SelectedIndex >= 0 && SelectedIndex < Count(EffectiveItems))
        {
            SyncListSelection();
        }
    }

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        // 重建期间 ListBox 先把选择清成 -1；待定索引尚未解析出对象时保留它，条目到位后再推回去。
        if (_list.SelectedIndex < 0 && SelectedIndex >= 0 && SelectedItem is null)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            SelectedIndex = _list.SelectedIndex;
            SelectedItem = _list.SelectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // 指针选中一行即提交：ListBox 已经写好 SelectedIndex，事件在这里关闭弹层。
    private void OnListPointerPressed(object? sender, VisualPointerEventArgs args)
    {
        if (IsDropDownOpen && args.Button == MouseButtons.Left)
        {
            IsDropDownOpen = false;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
        {
            _syncingOpen = true;
            IsDropDownOpen = false;
            _syncingOpen = false;
        }

        Focus();
    }

    private void SyncSelectedFromIndex()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var index = SelectedIndex;
            var items = EffectiveItems;
            SelectedItem = index >= 0 && index < Count(items) ? ItemAt(items, index) : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncSelectedIndexFromItem()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var index = -1;
            if (SelectedItem is { } selected)
            {
                var items = EffectiveItems;
                var count = Count(items);
                for (var candidate = 0; candidate < count; candidate++)
                {
                    if (Equals(ItemAt(items, candidate), selected))
                    {
                        index = candidate;
                        break;
                    }
                }
            }

            SelectedIndex = index;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncListSelection()
    {
        if (_list.SelectedIndex == SelectedIndex && Equals(_list.SelectedItem, SelectedItem))
        {
            return;
        }

        // 行还没物化时写一个越界索引会被 ListBox 立刻清成 -1；等 ItemsRebuilt 再推。
        if (SelectedIndex >= _list.Items.Count)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            _list.SelectedIndex = SelectedIndex;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = Count(EffectiveItems);
        if (count == 0)
        {
            return;
        }

        var current = SelectedIndex;
        var next = current < 0
            ? (delta > 0 ? 0 : count - 1)
            : XamlMath.Clamp(current + delta, 0, count - 1);
        SelectedIndex = next;
    }

    private void RestoreSelectionAtOpen()
    {
        if (_selectionAtOpen != SelectedIndex)
        {
            SelectedIndex = _selectionAtOpen;
        }
    }

    private void ScrollSelectionIntoView()
    {
        var index = SelectedIndex;
        if (ActivePopup.IsOpen && index >= 0 && index < _list.Items.Count)
        {
            ActiveScroll.ScrollIntoView(_list.Items[index]);
        }
    }

    private static int Count(IEnumerable source)
    {
        if (source is ICollection collection)
        {
            return collection.Count;
        }

        var count = 0;
        foreach (var _ in source)
        {
            count++;
        }

        return count;
    }

    private static object? ItemAt(IEnumerable source, int index)
    {
        if (source is IList list)
        {
            return list[index];
        }

        var current = 0;
        foreach (var item in source)
        {
            if (current++ == index)
            {
                return item;
            }
        }

        return null;
    }
}

/// <summary>An inline item for <see cref="ComboBox.Items"/>; its text is its display text.</summary>
public class ComboBoxItem : TextBlock
{
    /// <summary>Creates an empty item with the drop-down row padding.</summary>
    public ComboBoxItem()
    {
        Padding = new XamlThickness(8f, 4f);
    }
}
