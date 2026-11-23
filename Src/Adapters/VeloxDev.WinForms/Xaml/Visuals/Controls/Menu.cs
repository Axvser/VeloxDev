using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>One entry in a <see cref="VisualContextMenu"/> or <see cref="VisualMenu"/>.</summary>
/// <remarks>
/// Items are data, not visuals: without a template the presenter measures and draws every row, so a menu has one
/// bounds and one input surface. A submenu is another <see cref="VisualContextMenu"/> opened on the row.
/// </remarks>
public sealed class VisualMenuItem
{
    /// <summary>The text shown in the row.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>The shortcut text drawn at the right edge, or <see langword="null"/>.</summary>
    public string? InputGestureText { get; set; }

    /// <summary>Whether the row can be invoked.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether a check mark is drawn before the header.</summary>
    public bool IsChecked { get; set; }

    /// <summary>Whether the row is a horizontal separator; its other properties are ignored.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>An optional command run after <see cref="Click"/>.</summary>
    public Action? Command { get; set; }

    /// <summary>A property path resolved against the menu's data context; an <c>ICommand</c>-shaped value runs.</summary>
    public string? CommandPath { get; set; }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal VisualContextMenu? Owner { get; set; }

    /// <summary>The child rows; a non-empty list makes this row open a submenu.</summary>
    public IList<VisualMenuItem> Items { get; } = new List<VisualMenuItem>();

    /// <summary>Raised when the row is invoked.</summary>
    public event EventHandler? Click;

    internal void Invoke()
    {
        if (!IsEnabled)
        {
            return;
        }

        Click?.Invoke(this, EventArgs.Empty);
        Command?.Invoke();
        if (!string.IsNullOrWhiteSpace(CommandPath) && Owner?.DataContext is { } context)
        {
            var value = context;
            foreach (var segment in CommandPath.Split('.'))
            {
                if (value is null) break;
                value = value.GetType().GetProperty(segment)?.GetValue(value);
            }

            if (value is VeloxDev.MVVM.IVeloxCommand command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }
}

/// <summary>One realized row of a templated menu, hosted by the template's items presenter.</summary>
/// <remarks>
/// The container draws the row itself while it has no template of its own. A menu template can still style a
/// container through an implicit style; <see cref="IsHighlighted"/>, <see cref="IsPressed"/> and
/// <see cref="IsEnabled"/> are dependency properties so triggers can observe the interactive state.
/// </remarks>
public class VisualMenuItemContainer : TemplatedControl
{
    internal const float RowHeight = 28f;
    internal const float SeparatorHeight = 9f;
    internal const float CheckColumn = 20f;
    internal const float ArrowColumn = 18f;
    internal const float MinimumWidth = 180f;

    /// <summary>The item data this container presents.</summary>
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(VisualMenuItem), typeof(VisualMenuItemContainer),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether this row is the current pointer or keyboard selection.</summary>
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(VisualMenuItemContainer),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the left pointer button is held on this row.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(VisualMenuItemContainer),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether this container is a menu-bar header rather than a drop-down row.</summary>
    public static readonly DependencyProperty IsTopLevelProperty = DependencyProperty.Register(
        nameof(IsTopLevel), typeof(bool), typeof(VisualMenuItemContainer),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Creates an unhighlighted container that passes uncovered pointer hits to its menu.</summary>
    public VisualMenuItemContainer()
    {
        IsHitTestVisible = false;
        Padding = default;
    }

    /// <summary>The item data this container presents.</summary>
    public VisualMenuItem? Item
    {
        get => (VisualMenuItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Whether this row is the current pointer or keyboard selection.</summary>
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty)!;
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>Whether the left pointer button is held on this row.</summary>
    public bool IsPressed
    {
        get => (bool)GetValue(IsPressedProperty)!;
        set => SetValue(IsPressedProperty, value);
    }

    /// <summary>Whether this container is a menu-bar header rather than a drop-down row.</summary>
    public bool IsTopLevel
    {
        get => (bool)GetValue(IsTopLevelProperty)!;
        set => SetValue(IsTopLevelProperty, value);
    }

    /// <summary>The row-text colour; an empty colour uses the active theme.</summary>
    public Color Foreground { get; set; } = Color.Empty;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsTemplated)
        {
            return MeasureTemplate(availableSize);
        }

        if (Item is not { } item)
        {
            return Size.Empty;
        }

        var font = ResolveFont(DefaultFont);
        var header = VisualText.Measure(item.Header, font, 0f, wrap: false, RenderDpi);
        if (IsTopLevel)
        {
            // Math.Ceiling 没有 int 重载；net461 上 int→double 与 int→decimal 都会命中，必须显式转 double。
            return new Size(
                (int)Math.Ceiling((double)header.Width + 24d),
                Math.Max(
                    Theme.Current.ControlHeight,
                    (int)Math.Ceiling((double)header.Height + 8d)));
        }

        if (item.IsSeparator)
        {
            return new Size((int)Math.Ceiling((double)MinimumWidth), (int)Math.Ceiling((double)SeparatorHeight));
        }

        var gesture = string.IsNullOrEmpty(item.InputGestureText)
            ? 0f
            : VisualText.Measure(item.InputGestureText!, font, 0f, wrap: false, RenderDpi).Width + 18f;
        return new Size(
            (int)Math.Ceiling(Math.Max(
                (double)MinimumWidth,
                (double)header.Width + CheckColumn + ArrowColumn + gesture + 24d)),
            (int)Math.Ceiling((double)RowHeight));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (IsTemplated || Item is not { } item)
        {
            return;
        }

        var theme = Theme.Current;
        var font = ResolveFont(DefaultFont);
        var bounds = new RectangleF(
            0f,
            0f,
            Math.Max(0f, (float)Math.Ceiling(Bounds.Width)),
            Math.Max(0f, (float)Math.Ceiling(Bounds.Height)));
        var foreground = item.IsEnabled
            ? (Foreground.IsEmpty ? theme.Foreground : Foreground)
            : theme.DisabledForeground;

        if (IsTopLevel)
        {
            if (IsHighlighted)
            {
                using var background = new SolidBrush(theme.Selection);
                using var path = XamlDrawing.RoundedRectangle(
                    new RectangleF(bounds.X, bounds.Y + 2f, bounds.Width, Math.Max(0f, bounds.Height - 4f)),
                    3f);
                graphics.FillPath(background, path);
            }

            VisualText.Draw(this,
                graphics,
                item.Header,
                font,
                foreground,
                new Rectangle(0, 0, (int)bounds.Width, (int)bounds.Height),
                wrap: false,
                StringAlignment.Center,
                StringAlignment.Center);
            return;
        }

        if (item.IsSeparator)
        {
            using var separator = new Pen(theme.ControlBorder, 1f);
            var middle = bounds.Top + (bounds.Height / 2f);
            graphics.DrawLine(separator, bounds.Left + 6f, middle, Math.Max(bounds.Left + 6f, bounds.Right - 6f), middle);
            return;
        }

        if (IsHighlighted && item.IsEnabled)
        {
            using var background = new SolidBrush(theme.Selection);
            using var path = XamlDrawing.RoundedRectangle(
                new RectangleF(bounds.X, bounds.Y + 1f, bounds.Width, Math.Max(0f, bounds.Height - 2f)),
                3f);
            graphics.FillPath(background, path);
        }

        if (item.IsChecked)
        {
            using var check = new Pen(theme.Accent, 1.6f);
            graphics.DrawLines(check, new[]
            {
                new PointF(bounds.Left + 5f, bounds.Top + 14f),
                new PointF(bounds.Left + 9f, bounds.Top + 18f),
                new PointF(bounds.Left + 16f, bounds.Top + 9f),
            });
        }

        var text = new Rectangle(
            (int)Math.Ceiling(bounds.Left + CheckColumn),
            (int)Math.Ceiling(bounds.Top),
            Math.Max(0, (int)Math.Ceiling(bounds.Width - CheckColumn - ArrowColumn - 8f)),
            (int)Math.Ceiling(bounds.Height));
        VisualText.Draw(this,
            graphics,
            item.Header,
            font,
            foreground,
            text,
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);

        if (item.InputGestureText is { Length: > 0 } gestureText)
        {
            var gesture = new Rectangle(
                (int)Math.Ceiling(bounds.Left),
                (int)Math.Ceiling(bounds.Top),
                Math.Max(0, (int)Math.Ceiling(bounds.Width - ArrowColumn - 8f)),
                (int)Math.Ceiling(bounds.Height));
            VisualText.Draw(this,
                graphics,
                gestureText,
                font,
                theme.MutedForeground,
                gesture,
                wrap: false,
                StringAlignment.Far,
                StringAlignment.Center);
        }

        if (item.Items.Count > 0)
        {
            using var arrow = new SolidBrush(foreground);
            var x = bounds.Right - 9f;
            var y = bounds.Top + (bounds.Height / 2f);
            graphics.FillPolygon(arrow, new[]
            {
                new PointF(x - 3f, y - 4f),
                new PointF(x + 2f, y),
                new PointF(x - 3f, y + 4f),
            });
        }
    }
}

/// <summary>A layered menu of <see cref="VisualMenuItem"/> rows, opened in the surface overlay.</summary>
/// <remarks>
/// <para>
/// Without a template the menu keeps one bounds and draws every row itself. A row with children opens a nested menu
/// anchored to the right of the row; closing this menu closes the nested one. A press outside the menu and its
/// placement target closes it through <see cref="VisualPopup"/>.
/// </para>
/// <para>
/// A template replaces the self-drawn rows. The menu realizes one <see cref="VisualMenuItemContainer"/> per row into
/// the part named <c>PART_ItemsPresenter</c> (or a panel marked with <see cref="ItemsControl.SetIsItemsHost"/>);
/// submenu popups reuse the same template. A missing items host throws <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public class VisualContextMenu : VisualPopup
{
    /// <summary>A control template applied directly to this menu, or <see langword="null"/> to use the style's.</summary>
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template), typeof(VisualControlTemplate), typeof(VisualContextMenu),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Whether the pointer is over the menu; template triggers can observe it.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver), typeof(bool), typeof(VisualContextMenu),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the left pointer button is held on the menu.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(VisualContextMenu),
        new PropertyMetadata(false) { AffectsRender = true });

    private readonly List<RectangleF> _rows = [];
    private readonly List<VisualMenuItemContainer> _containers = [];
    private int _highlight = -1;
    private VisualContextMenu? _submenu;
    private VisualControlTemplate? _styleTemplate;
    private VisualControlTemplate? _appliedTemplate;
    private VisualElement? _templateRoot;
    private VisualTemplateTriggerApplication? _templateTriggers;
    private bool _disposed;

    /// <summary>The item-text colour as a brush value; an empty colour uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(XamlBrush), typeof(VisualContextMenu),
        new PropertyMetadata(null, null, static (_, value) => ColorBrushCoercion.Coerce(value))
        {
            AffectsRender = true,
        });

    /// <summary>Creates an empty menu with theme metrics.</summary>
    public VisualContextMenu()
    {
        MatchTargetWidth = false;
        Focusable = true;
        Padding = new XamlThickness(4f);
        Background = Theme.Current.SurfaceAlt;
        BorderBrush = Theme.Current.ControlBorder;
        BorderThickness = new XamlThickness(1f);
        CornerRadius = Theme.Current.CornerRadius;
        Closed += (_, _) =>
        {
            CloseSubmenu();
            SetValue(IsPressedProperty, false);
            SetValue(IsPointerOverProperty, false);
        };
    }

    /// <summary>The rows shown by this menu.</summary>
    public IList<VisualMenuItem> Items { get; } = new List<VisualMenuItem>();

    /// <summary>A control template applied directly to this menu, or <see langword="null"/> to use the style's.</summary>
    public VisualControlTemplate? Template
    {
        get => (VisualControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>The template root currently applied, or <see langword="null"/> while the menu draws its rows.</summary>
    public VisualElement? TemplateRoot => _templateRoot;

    /// <summary>Whether the pointer is over the menu; template triggers can observe it.</summary>
    /// <remarks>Shadows <see cref="VisualElement.IsPointerOver"/> so a template trigger can observe hover.</remarks>
    public new bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>Whether the left pointer button is held on the menu.</summary>
    public bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>The item-text colour; an empty colour uses the active theme.</summary>
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

    /// <summary>The realized row containers when a template is applied; empty while the menu draws itself.</summary>
    public IReadOnlyList<VisualMenuItemContainer> Containers => _containers;

    /// <summary>The open submenu, or <see langword="null"/> while no row with children is open.</summary>
    public VisualContextMenu? Submenu => _submenu;

    /// <summary>Opens the menu at a surface point relative to <paramref name="target"/>.</summary>
    /// <param name="target">The element the point is measured against.</param>
    /// <param name="surfacePoint">The point in surface coordinates.</param>
    public void OpenAt(VisualElement target, PointF surfacePoint)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));

        AttachOwners(Items);
        var local = target.PointFromSurface(surfacePoint);
        PlacementTarget = target;
        Placement = VisualPlacement.Bottom;
        HorizontalOffset = local.X;
        VerticalOffset = local.Y;
        IsOpen = true;
        Focus();
    }

    private void AttachOwners(IEnumerable<VisualMenuItem> items)
    {
        foreach (var item in items)
        {
            item.Owner = this;
            AttachOwners(item.Items);
        }
    }

    /// <summary>Invalidates the measured rows after the item list changed.</summary>
    public void Refresh()
    {
        _highlight = -1;
        _rows.Clear();
        if (_templateRoot is not null)
        {
            RebuildContainers();
        }

        InvalidateMeasure();
    }

    // 样式模板与直接赋值走同一入口；模板根与触发器归本菜单自己持有。
    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        _styleTemplate = template;
        UpdateTemplate();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_templateRoot is not null)
        {
            return _templateRoot.Measure(availableSize);
        }

        var width = 180f;
        var height = Padding.Vertical;
        var font = ResolveFont(DefaultFont);
        foreach (var item in Items)
        {
            if (item.IsSeparator)
            {
                height += VisualMenuItemContainer.SeparatorHeight;
                continue;
            }

            var measured = VisualText.Measure(item.Header, font, 0f, wrap: false, RenderDpi);
            var gesture = string.IsNullOrEmpty(item.InputGestureText)
                ? 0f
                : VisualText.Measure(item.InputGestureText!, font, 0f, wrap: false, RenderDpi).Width + 18f;
            width = Math.Max(
                width,
                measured.Width + VisualMenuItemContainer.CheckColumn + VisualMenuItemContainer.ArrowColumn
                + gesture + Padding.Horizontal + 24f);
            height += VisualMenuItemContainer.RowHeight;
        }

        // width/availableSize 的 int/float 混合在 net461 的 Math.Min/Max 上也要显式统一成 double。
        return new Size(
            (int)Math.Ceiling(Math.Min((double)width, Math.Max(120d, (double)availableSize.Width))),
            (int)Math.Ceiling(height));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (_templateRoot is { } root)
        {
            root.Arrange(new RectangleF(0f, 0f, finalSize.Width, finalSize.Height));
            return;
        }

        _rows.Clear();
        var y = (float)Math.Ceiling(Padding.Top);
        foreach (var item in Items)
        {
            var height = item.IsSeparator ? VisualMenuItemContainer.SeparatorHeight : VisualMenuItemContainer.RowHeight;
            _rows.Add(new RectangleF(
                (int)Math.Ceiling(Padding.Left),
                y,
                Math.Max(0f, finalSize.Width - Padding.Horizontal),
                height));
            y += height;
        }
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        // 模板根负责整块外观；自绘的行与弹层底色都退出。
        if (_templateRoot is not null)
        {
            return;
        }

        base.RenderContent(graphics);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_templateRoot is not null)
        {
            return;
        }

        var theme = Theme.Current;
        var font = ResolveFont(DefaultFont);
        var normalForeground = Foreground.IsEmpty ? theme.Foreground : Foreground;
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            var row = _rows[index];
            if (item.IsSeparator)
            {
                using var separator = new Pen(theme.ControlBorder, 1f);
                graphics.DrawLine(
                    separator,
                    row.Left + 6f,
                    row.Top + (row.Height / 2f),
                    row.Right - 6f,
                    row.Top + (row.Height / 2f));
                continue;
            }

            if (index == _highlight && item.IsEnabled)
            {
                using var background = new SolidBrush(theme.Selection);
                using var path = XamlDrawing.RoundedRectangle(new RectangleF(row.X, row.Y + 1f, row.Width, row.Height - 2f), 3f);
                graphics.FillPath(background, path);
            }

            var foreground = item.IsEnabled ? normalForeground : theme.DisabledForeground;
            if (item.IsChecked)
            {
                using var check = new Pen(theme.Accent, 1.6f);
                graphics.DrawLines(check, new[]
                {
                    new PointF(row.Left + 5f, row.Top + 14f),
                    new PointF(row.Left + 9f, row.Top + 18f),
                    new PointF(row.Left + 16f, row.Top + 9f),
                });
            }

            var text = new Rectangle(
                (int)Math.Ceiling(row.Left + VisualMenuItemContainer.CheckColumn),
                (int)Math.Ceiling(row.Top),
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        row.Width - VisualMenuItemContainer.CheckColumn - VisualMenuItemContainer.ArrowColumn - 8f)),
                (int)Math.Ceiling(row.Height));
            VisualText.Draw(this,
                graphics,
                item.Header,
                font,
                foreground,
                text,
                wrap: false,
                StringAlignment.Near,
                StringAlignment.Center);

            if (item.InputGestureText is { Length: > 0 } gestureText)
            {
                var gesture = new Rectangle(
                    (int)Math.Ceiling(row.Left),
                    (int)Math.Ceiling(row.Top),
                    Math.Max(0, (int)Math.Ceiling(row.Width - VisualMenuItemContainer.ArrowColumn - 8f)),
                    (int)Math.Ceiling(row.Height));
                VisualText.Draw(this,
                    graphics,
                    gestureText,
                    font,
                    theme.MutedForeground,
                    gesture,
                    wrap: false,
                    StringAlignment.Far,
                    StringAlignment.Center);
            }

            if (item.Items.Count > 0)
            {
                using var arrow = new SolidBrush(foreground);
                var x = row.Right - 9f;
                var y = row.Top + (row.Height / 2f);
                graphics.FillPolygon(arrow, new[]
                {
                    new PointF(x - 3f, y - 4f),
                    new PointF(x + 2f, y),
                    new PointF(x - 3f, y + 4f),
                });
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        SetValue(IsPointerOverProperty, true);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        SetValue(IsPointerOverProperty, false);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var index = IndexAt(args.GetPosition(this));
        if (index == _highlight)
        {
            return;
        }

        SetHighlight(index);
        if (index >= 0 && Items[index].IsEnabled)
        {
            if (Items[index].Items.Count > 0)
            {
                OpenSubmenu(index);
            }
            else
            {
                CloseSubmenu();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        var index = IndexAt(args.GetPosition(this));
        if (index < 0 || !Items[index].IsEnabled)
        {
            return;
        }

        SetValue(IsPressedProperty, true);
        SetHighlight(index);
        SetContainerPressed(index, true);

        var item = Items[index];
        if (item.Items.Count > 0)
        {
            OpenSubmenu(index);
        }
        else
        {
            CloseAll();
            item.Invoke();
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        SetValue(IsPressedProperty, false);
        SetContainerPressed(-1, false);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        switch (args.KeyCode)
        {
            case Keys.Down:
                MoveHighlight(1);
                args.Handled = true;
                break;
            case Keys.Up:
                MoveHighlight(-1);
                args.Handled = true;
                break;
            case Keys.Right when _highlight >= 0 && Items[_highlight].Items.Count > 0:
                OpenSubmenu(_highlight);
                _submenu?.Focus();
                args.Handled = true;
                break;
            case Keys.Enter when _highlight >= 0:
            case Keys.Space when _highlight >= 0:
                if (Items[_highlight].Items.Count > 0)
                {
                    OpenSubmenu(_highlight);
                }
                else if (Items[_highlight].IsEnabled)
                {
                    var item = Items[_highlight];
                    CloseAll();
                    item.Invoke();
                }

                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, TemplateProperty))
        {
            UpdateTemplate();
        }
        else if (ReferenceEquals(e.Property, ForegroundProperty))
        {
            foreach (var container in _containers)
            {
                container.Foreground = Foreground;
                container.InvalidateVisual();
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            DetachContainers();
            _templateTriggers?.Dispose();
            _templateTriggers = null;
            SetTemplateScope(null);
            _templateRoot = null;
            _appliedTemplate = null;
            _submenu?.Dispose();
            _submenu = null;
        }

        base.Dispose(disposing);
    }

    private int IndexAt(PointF point)
    {
        if (_templateRoot is not null)
        {
            // 模板行挂在 ItemsPresenter 下，Bounds 不是菜单坐标；统一换到表面坐标再比。
            var surfacePoint = PointToSurface(point);
            for (var index = 0; index < _containers.Count; index++)
            {
                if (_containers[index].AbsoluteBounds.Contains(surfacePoint))
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private void SetHighlight(int index)
    {
        _highlight = index;
        for (var candidate = 0; candidate < _containers.Count; candidate++)
        {
            _containers[candidate].IsHighlighted = candidate == index;
        }

        InvalidateVisual();
    }

    private void SetContainerPressed(int index, bool pressed)
    {
        if (index >= 0 && index < _containers.Count)
        {
            _containers[index].IsPressed = pressed;
        }
    }

    private void MoveHighlight(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = _highlight;
        for (var step = 0; step < Items.Count; step++)
        {
            index = (index + delta + Items.Count) % Items.Count;
            if (!Items[index].IsSeparator)
            {
                SetHighlight(index);
                return;
            }
        }
    }

    private void OpenSubmenu(int index)
    {
        var item = Items[index];
        if (item.Items.Count == 0)
        {
            return;
        }

        CloseSubmenu();
        var submenu = new VisualContextMenu
        {
            PlacementTarget = this,
            Placement = VisualPlacement.Right,
            VerticalOffset = SubmenuOffset(index),
            Foreground = Foreground,
        };
        if (_appliedTemplate is { } template)
        {
            submenu.Template = template;
        }

        foreach (var child in item.Items)
        {
            submenu.Items.Add(child);
        }

        if (submenu.TemplateRoot is not null)
        {
            submenu.Refresh();
        }

        submenu.IsOpen = true;
        submenu.Focus();
        _submenu = submenu;
    }

    private float SubmenuOffset(int index)
    {
        if (_templateRoot is not null && index < _containers.Count)
        {
            return _containers[index].AbsoluteBounds.Y - AbsoluteBounds.Y;
        }

        return index < _rows.Count ? _rows[index].Top - Padding.Top : 0f;
    }

    private void CloseSubmenu()
    {
        if (_submenu is null)
        {
            return;
        }

        var submenu = _submenu;
        _submenu = null;
        submenu.Close();
        submenu.Dispose();
        Focus();
    }

    private void CloseAll()
    {
        CloseSubmenu();
        Close();
    }

    private void UpdateTemplate()
    {
        if (_disposed)
        {
            return;
        }

        var effective = Template ?? _styleTemplate;
        if (ReferenceEquals(effective, _appliedTemplate))
        {
            return;
        }

        DetachContainers();
        RemoveTemplate();
        _appliedTemplate = effective;
        if (effective is not null)
        {
            var instance = effective.CreateContent(this);
            _templateRoot = instance.Root;
            SetTemplateScope(instance.Scope);
            Add(instance.Root);
            _templateTriggers = new VisualTemplateTriggerApplication(this, instance.Scope, effective.Triggers);
        }

        if (_templateRoot is not null)
        {
            RebuildContainers();
        }

        InvalidateMeasure();
        InvalidateVisual();
        if (IsOpen)
        {
            Reposition();
        }
    }

    private void RemoveTemplate()
    {
        _templateTriggers?.Dispose();
        _templateTriggers = null;
        _appliedTemplate = null;
        SetTemplateScope(null);

        if (_templateRoot is null)
        {
            return;
        }

        if (Children.Contains(_templateRoot))
        {
            RemoveChild(_templateRoot);
        }

        _templateRoot.Dispose();
        _templateRoot = null;
    }

    private void RebuildContainers()
    {
        if (_templateRoot is null)
        {
            return;
        }

        DetachContainers();
        var host = ResolveItemsHost();
        if (host is null)
        {
            throw new InvalidOperationException(
                "The VisualContextMenu ControlTemplate must declare a part named 'PART_ItemsPresenter' "
                + "(an ItemsPresenter or a panel marked IsItemsHost).");
        }

        foreach (var item in Items)
        {
            var container = new VisualMenuItemContainer
            {
                Item = item,
                Foreground = Foreground,
            };
            host.Add(container);
            _containers.Add(container);
        }
    }

    private void DetachContainers()
    {
        foreach (var container in _containers)
        {
            container.ParentElement?.RemoveChild(container);
            container.Dispose();
        }

        _containers.Clear();
        _highlight = -1;
    }

    private Panel? ResolveItemsHost()
    {
        var named = GetTemplateChild<VisualElement>("PART_ItemsPresenter");
        if (named is not null)
        {
            return named as Panel
                ?? throw new InvalidOperationException(
                    $"The VisualContextMenu ControlTemplate part 'PART_ItemsPresenter' must be a Panel, but was "
                    + $"'{named.GetType().Name}'.");
        }

        return FindItemsHost(_templateRoot);
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
}

/// <summary>A horizontal menu bar whose top-level entries open a <see cref="VisualContextMenu"/>.</summary>
/// <remarks>
/// A template replaces the self-drawn headers: one <see cref="VisualMenuItemContainer"/> is realized per top-level
/// entry into <c>PART_ItemsPresenter</c> (or a panel marked with <see cref="ItemsControl.SetIsItemsHost"/>). The
/// drop-down menus opened from the bar remain self-drawn. A missing items host throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public class VisualMenu : TemplatedControl
{
    /// <summary>Whether the pointer is over the bar; template triggers can observe it.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver), typeof(bool), typeof(VisualMenu),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the left pointer button is held on the bar.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(VisualMenu),
        new PropertyMetadata(false) { AffectsRender = true });

    private readonly List<RectangleF> _headers = [];
    private readonly List<VisualMenuItemContainer> _containers = [];
    private VisualContextMenu? _open;
    private int _hover = -1;
    private int _openIndex = -1;

    /// <summary>The header-text colour as a brush value; an empty colour uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(XamlBrush), typeof(VisualMenu),
        new PropertyMetadata(null, null, static (_, value) => ColorBrushCoercion.Coerce(value))
        {
            AffectsRender = true,
        });

    /// <summary>Creates an empty menu bar.</summary>
    public VisualMenu()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(4f, 2f);
    }

    /// <summary>The top-level entries.</summary>
    public IList<VisualMenuItem> Items { get; } = new List<VisualMenuItem>();

    /// <summary>Whether the pointer is over the bar; template triggers can observe it.</summary>
    /// <remarks>Shadows <see cref="VisualElement.IsPointerOver"/> so a template trigger can observe hover.</remarks>
    public new bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>Whether the left pointer button is held on the bar.</summary>
    public bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>The header-text colour; an empty colour uses the active theme.</summary>
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

    /// <summary>The realized header containers when a template is applied; empty while the bar draws itself.</summary>
    public IReadOnlyList<VisualMenuItemContainer> Containers => _containers;

    /// <summary>The drop-down menu opened from the bar, or <see langword="null"/>.</summary>
    public VisualContextMenu? OpenedMenu => _open;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsTemplated)
        {
            return MeasureTemplate(availableSize);
        }

        var width = Padding.Horizontal;
        var height = (int)Math.Ceiling((double)Theme.Current.ControlHeight + Padding.Vertical);
        var font = ResolveFont(DefaultFont);
        foreach (var item in Items)
        {
            width += (int)Math.Ceiling((double)VisualText.Measure(item.Header, font, 0f, wrap: false, RenderDpi).Width + 24d);
        }

        return new Size((int)Math.Ceiling((double)width), height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (IsTemplated)
        {
            ArrangeTemplate(finalSize);
            return;
        }

        _headers.Clear();
        var x = (float)Math.Ceiling(Padding.Left);
        var font = ResolveFont(DefaultFont);
        foreach (var item in Items)
        {
            var width = (float)Math.Ceiling(
                VisualText.Measure(item.Header, font, 0f, wrap: false, RenderDpi).Width + 24f);
            _headers.Add(new RectangleF(x, 0f, width, finalSize.Height));
            x += width;
        }
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        base.RenderContent(graphics);
        if (IsTemplated)
        {
            return;
        }

        var theme = Theme.Current;
        var font = ResolveFont(DefaultFont);
        var foreground = Foreground.IsEmpty ? theme.Foreground : Foreground;
        for (var index = 0; index < Items.Count; index++)
        {
            var header = _headers[index];
            var active = index == _hover || (_open is not null && index == _openIndex);
            if (active)
            {
                using var background = new SolidBrush(theme.Selection);
                using var path = XamlDrawing.RoundedRectangle(
                    new RectangleF(header.X, header.Y + 2f, header.Width, header.Height - 4f), 3f);
                graphics.FillPath(background, path);
            }

            VisualText.Draw(this,
                graphics,
                Items[index].Header,
                font,
                foreground,
                new Rectangle(
                    (int)header.X,
                    (int)header.Y,
                    (int)header.Width,
                    (int)header.Height),
                wrap: false,
                StringAlignment.Center,
                StringAlignment.Center);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        SetValue(IsPointerOverProperty, true);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var index = IndexAt(args.GetPosition(this));
        if (index == _hover)
        {
            return;
        }

        _hover = index;
        UpdateContainerStates();
        InvalidateVisual();
        if (_open is not null && index >= 0 && index != _openIndex)
        {
            OpenMenu(index);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        SetValue(IsPointerOverProperty, false);
        _hover = -1;
        UpdateContainerStates();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        var index = IndexAt(args.GetPosition(this));
        if (index < 0)
        {
            return;
        }

        SetValue(IsPressedProperty, true);
        if (_open is not null && index == _openIndex)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu(index);
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        SetValue(IsPressedProperty, false);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        switch (args.KeyCode)
        {
            case Keys.Left:
                OpenMenu(Math.Max(0, (_openIndex < 0 ? 0 : _openIndex) - 1));
                args.Handled = true;
                break;
            case Keys.Right:
                OpenMenu(Math.Min(Items.Count - 1, _openIndex + 1));
                args.Handled = true;
                break;
            case Keys.Down when _open is null:
                OpenMenu(Math.Max(0, _hover));
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ForegroundProperty))
        {
            foreach (var container in _containers)
            {
                container.Foreground = Foreground;
                container.InvalidateVisual();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging()
    {
        DetachContainers();
        base.OnTemplateChanging();
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged()
    {
        base.OnTemplateChanged();
        if (IsTemplated)
        {
            RebuildContainers();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        DetachContainers();
        if (disposing)
        {
            _open?.Dispose();
            _open = null;
        }

        base.Dispose(disposing);
    }

    private int IndexAt(PointF point)
    {
        if (IsTemplated)
        {
            var surfacePoint = PointToSurface(point);
            for (var index = 0; index < _containers.Count; index++)
            {
                if (_containers[index].AbsoluteBounds.Contains(surfacePoint))
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = 0; index < _headers.Count; index++)
        {
            if (_headers[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateContainerStates()
    {
        for (var index = 0; index < _containers.Count; index++)
        {
            _containers[index].IsHighlighted = index == _hover || (_open is not null && index == _openIndex);
        }
    }

    private void RebuildContainers()
    {
        DetachContainers();
        var host = ResolveItemsHost();
        if (host is null)
        {
            throw new InvalidOperationException(
                "The VisualMenu ControlTemplate must declare a part named 'PART_ItemsPresenter' "
                + "(an ItemsPresenter or a panel marked IsItemsHost).");
        }

        foreach (var item in Items)
        {
            var container = new VisualMenuItemContainer
            {
                Item = item,
                IsTopLevel = true,
                Foreground = Foreground,
            };
            host.Add(container);
            _containers.Add(container);
        }

        UpdateContainerStates();
    }

    private void DetachContainers()
    {
        foreach (var container in _containers)
        {
            container.ParentElement?.RemoveChild(container);
            container.Dispose();
        }

        _containers.Clear();
    }

    private Panel? ResolveItemsHost()
    {
        var named = GetTemplateChild<VisualElement>("PART_ItemsPresenter");
        if (named is not null)
        {
            return named as Panel
                ?? throw new InvalidOperationException(
                    $"The VisualMenu ControlTemplate part 'PART_ItemsPresenter' must be a Panel, but was "
                    + $"'{named.GetType().Name}'.");
        }

        return FindItemsHost(TemplateRoot);
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

    private void OpenMenu(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        CloseMenu();
        _openIndex = index;
        var item = Items[index];
        var menu = new VisualContextMenu
        {
            PlacementTarget = this,
            Placement = VisualPlacement.Bottom,
            HorizontalOffset = HeaderOffset(index),
            VerticalOffset = 0f,
            Foreground = Foreground,
        };
        foreach (var child in item.Items)
        {
            menu.Items.Add(child);
        }

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, menu))
            {
                _open = null;
                _openIndex = -1;
                Focus();
                UpdateContainerStates();
                InvalidateVisual();
            }
        };
        menu.IsOpen = true;
        menu.Focus();
        _open = menu;
        UpdateContainerStates();
        InvalidateVisual();
    }

    private float HeaderOffset(int index)
    {
        if (IsTemplated && index < _containers.Count)
        {
            return _containers[index].AbsoluteBounds.X - AbsoluteBounds.X;
        }

        return index < _headers.Count ? _headers[index].Left : 0f;
    }

    private void CloseMenu()
    {
        _open?.Close();
        _open?.Dispose();
        _open = null;
        _openIndex = -1;
        UpdateContainerStates();
        InvalidateVisual();
    }
}
