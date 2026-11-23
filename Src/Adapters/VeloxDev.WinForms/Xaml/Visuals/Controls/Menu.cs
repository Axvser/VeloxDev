using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>One entry in a <see cref="VisualContextMenu"/> or <see cref="VisualMenu"/>.</summary>
/// <remarks>
/// Items are data, not visuals: the presenter measures and draws every row, so a menu has one bounds and one input
/// surface. A submenu is another <see cref="VisualContextMenu"/> opened on the row.
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

/// <summary>A layered menu of <see cref="VisualMenuItem"/> rows, opened in the surface overlay.</summary>
/// <remarks>
/// The menu keeps one bounds and draws every row itself. A row with children opens a nested menu anchored to the
/// right of the row; closing this menu closes the nested one. A press outside the menu and its placement target
/// closes it through <see cref="VisualPopup"/>.
/// </remarks>
public class VisualContextMenu : VisualPopup
{
    private const float RowHeight = 28f;
    private const float SeparatorHeight = 9f;
    private const float CheckColumn = 20f;
    private const float ArrowColumn = 18f;

    private readonly List<RectangleF> _rows = [];
    private int _highlight = -1;
    private VisualContextMenu? _submenu;

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
        Closed += (_, _) => CloseSubmenu();
    }

    /// <summary>The rows shown by this menu.</summary>
    public IList<VisualMenuItem> Items { get; } = new List<VisualMenuItem>();

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
        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 180f;
        var height = Padding.Vertical;
        foreach (var item in Items)
        {
            if (item.IsSeparator)
            {
                height += SeparatorHeight;
                continue;
            }

            var measured = VisualText.Measure(item.Header, DefaultFont, 0f, wrap: false, RenderDpi);
            var gesture = string.IsNullOrEmpty(item.InputGestureText)
                ? 0f
                : VisualText.Measure(item.InputGestureText!, DefaultFont, 0f, wrap: false, RenderDpi).Width + 18f;
            width = Math.Max(width, measured.Width + CheckColumn + ArrowColumn + gesture + Padding.Horizontal + 24f);
            height += RowHeight;
        }

        return new Size((int)Math.Ceiling(Math.Min(width, Math.Max(120f, availableSize.Width))), (int)Math.Ceiling(height));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        _rows.Clear();
        var y = (float)Math.Ceiling(Padding.Top);
        foreach (var item in Items)
        {
            var height = item.IsSeparator ? SeparatorHeight : RowHeight;
            _rows.Add(new RectangleF(
                (int)Math.Ceiling(Padding.Left),
                y,
                Math.Max(0f, finalSize.Width - Padding.Horizontal),
                height));
            y += height;
        }
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
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

            var foreground = item.IsEnabled ? theme.Foreground : theme.DisabledForeground;
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
                (int)Math.Ceiling(row.Left + CheckColumn),
                (int)Math.Ceiling(row.Top),
                Math.Max(0, (int)Math.Ceiling(row.Width - CheckColumn - ArrowColumn - 8f)),
                (int)Math.Ceiling(row.Height));
            VisualText.Draw(this,
                graphics,
                item.Header,
                DefaultFont,
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
                    Math.Max(0, (int)Math.Ceiling(row.Width - ArrowColumn - 8f)),
                    (int)Math.Ceiling(row.Height));
                VisualText.Draw(this,
                    graphics,
                    gestureText,
                    DefaultFont,
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
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var index = IndexAt(args.GetPosition(this));
        if (index == _highlight)
        {
            return;
        }

        _highlight = index;
        InvalidateVisual();
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _submenu?.Dispose();
            _submenu = null;
        }

        base.Dispose(disposing);
    }

    private int IndexAt(PointF point)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
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
                _highlight = index;
                InvalidateVisual();
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
            VerticalOffset = _rows[index].Top - Padding.Top,
        };
        foreach (var child in item.Items)
        {
            submenu.Items.Add(child);
        }

        submenu.IsOpen = true;
        submenu.Focus();
        _submenu = submenu;
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
}

/// <summary>A horizontal menu bar whose top-level entries open a <see cref="VisualContextMenu"/>.</summary>
public class VisualMenu : Panel
{
    private readonly List<RectangleF> _headers = [];
    private VisualContextMenu? _open;
    private int _hover = -1;

    /// <summary>Creates an empty menu bar.</summary>
    public VisualMenu()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(4f, 2f);
    }

    /// <summary>The top-level entries.</summary>
    public IList<VisualMenuItem> Items { get; } = new List<VisualMenuItem>();

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Padding.Horizontal;
        var height = (int)Math.Ceiling(Theme.Current.ControlHeight + Padding.Vertical);
        foreach (var item in Items)
        {
            width += (int)Math.Ceiling(VisualText.Measure(item.Header, DefaultFont, 0f, wrap: false, RenderDpi).Width + 24f);
        }

        return new Size((int)Math.Ceiling(width), height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        _headers.Clear();
        var x = (float)Math.Ceiling(Padding.Left);
        foreach (var item in Items)
        {
            var width = (float)Math.Ceiling(
                VisualText.Measure(item.Header, DefaultFont, 0f, wrap: false, RenderDpi).Width + 24f);
            _headers.Add(new RectangleF(x, 0f, width, finalSize.Height));
            x += width;
        }
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        base.RenderContent(graphics);
        var theme = Theme.Current;
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
                DefaultFont,
                theme.Foreground,
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
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var index = IndexAt(args.GetPosition(this));
        if (index == _hover)
        {
            return;
        }

        _hover = index;
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
        _hover = -1;
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _open?.Dispose();
            _open = null;
        }

        base.Dispose(disposing);
    }

    private int _openIndex = -1;

    private int IndexAt(PointF point)
    {
        for (var index = 0; index < _headers.Count; index++)
        {
            if (_headers[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
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
            HorizontalOffset = _headers[index].Left,
            VerticalOffset = 0f,
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
                InvalidateVisual();
            }
        };
        menu.IsOpen = true;
        menu.Focus();
        _open = menu;
        InvalidateVisual();
    }

    private void CloseMenu()
    {
        _open?.Close();
        _open?.Dispose();
        _open = null;
        _openIndex = -1;
        InvalidateVisual();
    }
}
