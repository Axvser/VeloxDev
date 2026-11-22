using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// The single window that hosts a tree of <see cref="VisualElement"/> objects and routes all input to it.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one HWND is created for the whole tree. Elements are painted in one pass into the double buffer, so a
/// structural change cannot show half of an old and half of a new tree. Native WinForms controls stay outside the
/// tree unless an element opts into a child window through <c>NativeControlHost</c>.
/// </para>
/// <para>
/// Focus is logical: the surface keeps the native keyboard focus and <see cref="FocusedElement"/> receives key and
/// text input. Pointer events hit-test the visual tree, capture through <see cref="VisualElement"/> and bubble.
/// </para>
/// </remarks>
public class VisualSurface : System.Windows.Forms.Control
{
    private const int WsClipChildren = 0x02000000;

    private VisualElement? _root;
    private VisualElement? _focused;
    private VisualElement? _capture;
    private readonly List<VisualElement> _hoverChain = [];
    private readonly Dictionary<NativeControlHost, System.Windows.Forms.Control> _nativeChildren = [];
    private readonly HashSet<NativeControlHost> _placedNative = [];
    private readonly VisualStyleCollection _styles = new();
    private readonly ResourceDictionary _resources = new();
    private readonly OverlayLayer _overlay = new();
    private readonly List<VisualPopup> _openPopups = [];
    private Rectangle _damage;
    private bool _damageFull = true;
    private MouseButtons _buttons;
    private bool _layoutValid;
    private Size _layoutSize;
    private int _renderDpi;
    private bool _renderDpiValid;

    /// <summary>Creates the surface with the double-buffered paint style it needs.</summary>
    public VisualSurface()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        DoubleBuffered = true;
        TabStop = true;
        _styles.Changed += (_, _) => RefreshStyles();
        _overlay.SetSurfaceRoot(this);
    }

    /// <summary>Styles applied implicitly to matching elements in this surface's visual tree.</summary>
    /// <remarks>
    /// An element's own <see cref="VisualElement.Styles"/> collection and its ancestors' collections take precedence
    /// over this one; an explicit <see cref="VisualElement.Style"/> takes precedence over all of them. The tree is
    /// re-resolved whenever this collection changes or the root is assigned.
    /// </remarks>
    public VisualStyleCollection Styles => _styles;

    /// <summary>Resources offered to every element in this surface's tree after its own and its ancestors' dictionaries.</summary>
    public ResourceDictionary Resources => _resources;

    /// <summary>The root element painted by this surface.</summary>
    public VisualElement? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value))
            {
                return;
            }

            _focused?.SetFocused(false);
            _focused = null;
            _capture = null;
            _hoverChain.Clear();
            CloseAllPopups();
            ClearNativeChildren();
            _root?.SetSurfaceRoot(null);
            _root?.RefreshImplicitStyles();
            _root = value;
            _root?.SetSurfaceRoot(this);
            RefreshStyles();
            InvalidateSurface();
        }
    }

    /// <summary>The element that currently receives keyboard and text input.</summary>
    public VisualElement? FocusedElement => _focused;

    /// <summary>Whether the surface currently has the native keyboard focus.</summary>
    public bool HasKeyboardFocus => Focused;

    internal int RenderDpi
    {
        get
        {
            if (_renderDpiValid)
            {
                return _renderDpi;
            }

            if (!IsHandleCreated)
            {
                return 96;
            }

            using var graphics = Graphics.FromHwnd(Handle);
            _renderDpi = (int)Math.Round(graphics.DpiX);
            _renderDpiValid = true;
            return _renderDpi;
        }
    }

    /// <inheritdoc />
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= WsClipChildren;
            return parameters;
        }
    }

    /// <summary>Requests a layout pass and a repaint of the whole surface.</summary>
    public void InvalidateSurface()
    {
        _layoutValid = false;
        _damageFull = true;
        Invalidate();
    }

    internal void InvalidatePaint()
    {
        _damageFull = true;
        Invalidate();
    }

    internal void InvalidatePaint(RectangleF damage)
    {
        if (_damageFull)
        {
            return;
        }

        var rectangle = Rectangle.Round(damage);
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return;
        }

        _damage = _damage.Width == 0 ? rectangle : Rectangle.Union(_damage, rectangle);
        Invalidate(rectangle);
    }

    /// <inheritdoc />
    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        _damageFull = true;
        Invalidate();
    }

    /// <summary>Renders the tree into <paramref name="graphics"/> at <paramref name="size"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="size">The logical size of the surface.</param>
    public void RenderTo(Graphics graphics, Size size)
        => RenderCore(graphics, size, new Rectangle(0, 0, size.Width, size.Height));

    private void RenderCore(Graphics graphics, Size size, Rectangle clearArea)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));

        using (var background = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(background, clearArea);
        }

        if (_root is null)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            EnsureLayout(size);
            _root.Render(graphics);
            _overlay.Render(graphics);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <summary>Closes every popup currently hosted by the overlay layer.</summary>
    public void ClosePopups() => CloseAllPopups();

    /// <summary>Moves logical focus to <paramref name="element"/>.</summary>
    /// <param name="element">The element to focus, or <see langword="null"/> to clear focus.</param>
    /// <returns><see langword="true"/> when the element belongs to this surface and is focusable.</returns>
    public bool FocusElement(VisualElement? element)
    {
        if (element is not null
            && (element.Surface != this || !element.Focusable || !element.IsEnabled || !element.IsVisible))
        {
            return false;
        }

        if (ReferenceEquals(_focused, element))
        {
            if (element is not null)
            {
                Focus();
            }

            return true;
        }

        _focused?.SetFocused(false);
        _focused = element;
        _focused?.SetFocused(true);
        if (element is not null)
        {
            Focus();
        }

        return true;
    }

    /// <summary>Injects a pointer press at a surface point.</summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="button">The pressed button.</param>
    /// <param name="clickCount">The click count; at least one.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerDown(PointF point, MouseButtons button, int clickCount = 1)
    {
        var target = FindElementAt(point);
        DismissPopupsFor(target);
        if (target is not null)
        {
            var focusable = FindFocusableAncestor(target);
            if (focusable is not null)
            {
                FocusElement(focusable);
            }
        }

        _capture = target;
        if (target is not null && IsHandleCreated)
        {
            Capture = true;
        }

        _buttons |= button;
        var args = new VisualPointerEventArgs(point, _buttons, button, clickCount, 0, ModifierKeys);
        target?.RaisePointerPressed(args);
        return args.Handled;
    }

    /// <summary>Injects a pointer move at a surface point.</summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerMove(PointF point)
    {
        var hoverTarget = FindElementAt(point);
        UpdateHover(point, hoverTarget);
        var target = _capture ?? hoverTarget;
        var args = new VisualPointerEventArgs(point, _buttons, MouseButtons.None, 1, 0, ModifierKeys);
        target?.RaisePointerMoved(args);
        Cursor = hoverTarget?.Cursor ?? Cursors.Default;
        return args.Handled;
    }

    /// <summary>Injects a pointer release at a surface point.</summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="button">The released button.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerUp(PointF point, MouseButtons button)
    {
        var target = _capture ?? FindElementAt(point);
        _buttons &= ~button;
        var args = new VisualPointerEventArgs(point, _buttons, button, 1, 0, ModifierKeys);
        target?.RaisePointerReleased(args);
        _capture = null;
        if (IsHandleCreated)
        {
            Capture = false;
        }

        return args.Handled;
    }

    /// <summary>Injects a wheel turn at a surface point.</summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="delta">The wheel delta; positive scrolls up.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerWheel(PointF point, int delta)
    {
        var target = FindElementAt(point);
        var args = new VisualPointerEventArgs(point, _buttons, MouseButtons.None, 1, delta, ModifierKeys);
        target?.RaisePointerWheel(args);
        return args.Handled;
    }

    /// <summary>Injects a key press into the focused element with the current modifiers.</summary>
    /// <param name="keyCode">The key code.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessKeyDown(Keys keyCode) => ProcessKeyDown(keyCode, ModifierKeys);

    /// <summary>Injects a key press with explicit modifiers; used by hosts and tests.</summary>
    /// <param name="keyCode">The key code.</param>
    /// <param name="modifiers">The modifier keys to report.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessKeyDown(Keys keyCode, Keys modifiers)
    {
        var args = new VisualKeyEventArgs(keyCode, modifiers, isRepeat: false);
        _focused?.RaiseKeyDown(args);
        if (!args.Handled && keyCode == Keys.Tab)
        {
            MoveFocus((modifiers & Keys.Shift) == 0);
            return true;
        }

        return args.Handled;
    }

    /// <summary>Injects a key release into the focused element with the current modifiers.</summary>
    /// <param name="keyCode">The key code.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessKeyUp(Keys keyCode) => ProcessKeyUp(keyCode, ModifierKeys);

    /// <summary>Injects a key release with explicit modifiers; used by hosts and tests.</summary>
    /// <param name="keyCode">The key code.</param>
    /// <param name="modifiers">The modifier keys to report.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessKeyUp(Keys keyCode, Keys modifiers)
    {
        var args = new VisualKeyEventArgs(keyCode, modifiers, isRepeat: false);
        _focused?.RaiseKeyUp(args);
        return args.Handled;
    }

    /// <summary>Injects text input into the focused element.</summary>
    /// <param name="text">The characters to insert.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessTextInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var args = new VisualTextInputEventArgs(text);
        _focused?.RaiseTextInput(args);
        return args.Handled;
    }

    /// <summary>Finds the deepest element under a surface point.</summary>
    /// <param name="point">The point in surface coordinates.</param>
    public VisualElement? FindElementAt(PointF point)
    {
        if (_root is null)
        {
            return null;
        }

        EnsureLayout(ClientSize);
        var hit = _overlay.HitTestVisual(point) ?? _root.HitTestVisual(point);
        for (var current = hit; current is not null; current = current.Parent)
        {
            if (current is VisualElement element)
            {
                return element;
            }
        }

        return null;
    }

    internal void PlaceNative(NativeControlHost owner, System.Windows.Forms.Control child, Rectangle bounds)
    {
        if (!_nativeChildren.TryGetValue(owner, out var existing))
        {
            _nativeChildren[owner] = child;
            Controls.Add(child);
        }
        else if (!ReferenceEquals(existing, child))
        {
            Controls.Remove(existing);
            _nativeChildren[owner] = child;
            Controls.Add(child);
        }

        _placedNative.Add(owner);

        if (child.Bounds != bounds)
        {
            child.Bounds = bounds;
        }

        child.Visible = owner.IsVisible && owner.Visibility == XamlVisibility.Visible;
    }

    internal void RemoveNative(NativeControlHost owner)
    {
        if (!_nativeChildren.TryGetValue(owner, out var child))
        {
            return;
        }

        _nativeChildren.Remove(owner);
        _placedNative.Remove(owner);
        if (!child.IsDisposed && Controls.Contains(child))
        {
            Controls.Remove(child);
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var damage = _damageFull
            ? new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)
            : e.ClipRectangle.Width > 0 && e.ClipRectangle.Height > 0
                ? e.ClipRectangle
                : _damage;
        if (damage.Width <= 0 || damage.Height <= 0)
        {
            return;
        }

        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(damage);
            RenderCore(e.Graphics, ClientSize, damage);
        }
        finally
        {
            e.Graphics.Restore(state);
        }

        _damage = Rectangle.Empty;
        _damageFull = false;
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _renderDpiValid = false;
        InvalidateSurface();
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ProcessPointerDown(e.Location, e.Button, e.Clicks);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ProcessPointerMove(e.Location);
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        ProcessPointerUp(e.Location, e.Button);
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ProcessPointerWheel(e.Location, e.Delta))
        {
            // handled; WinForms does not expose a cancel flag here
        }
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHover(PointToClient(Cursor.Position), FindElementAt(PointToClient(Cursor.Position)));
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(null, null);
        Cursor = Cursors.Default;
    }

    /// <inheritdoc />
    protected override bool IsInputKey(Keys keyData)
    {
        // Tab/Enter/Escape/arrows are dialog keys by default; the logical focus chain owns them here.
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Tab or Keys.Enter or Keys.Escape or Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            return true;
        }

        return base.IsInputKey(keyData);
    }

    /// <inheritdoc />
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab)
        {
            return ProcessKeyDown(Keys.Tab, keyData & Keys.Modifiers);
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ProcessKeyDown(e.KeyCode, e.Modifiers))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (ProcessKeyUp(e.KeyCode, e.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (char.IsControl(e.KeyChar))
        {
            return;
        }

        if (ProcessTextInput(e.KeyChar.ToString()))
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _focused?.SetFocused(true);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _focused?.SetFocused(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseAllPopups();
            ClearNativeChildren();
            _focused = null;
            _capture = null;
            _hoverChain.Clear();
            _root?.Dispose();
            _root = null;
            _overlay.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshStyles()
    {
        _root?.RefreshImplicitStyles();
        _overlay.RefreshImplicitStyles();
    }

    internal VisualElement OverlayHost => _overlay;

    internal void RegisterPopup(VisualPopup popup)
    {
        if (!_openPopups.Contains(popup))
        {
            _openPopups.Add(popup);
        }
    }

    internal void UnregisterPopup(VisualPopup popup) => _openPopups.Remove(popup);

    private void CloseAllPopups()
    {
        foreach (var popup in _openPopups.ToArray())
        {
            popup.Close();
        }

        _openPopups.Clear();
    }

    // 点击落在弹层或它的 PlacementTarget 里时保持打开；其余位置按 StaysOpen 关闭。
    private void DismissPopupsFor(VisualElement? target)
    {
        foreach (var popup in _openPopups.ToArray())
        {
            if (!popup.StaysOpen && !popup.ShouldStayOpen(target))
            {
                popup.Close();
            }
        }
    }

    private void ClearNativeChildren()
    {
        foreach (var child in _nativeChildren.Values)
        {
            if (!child.IsDisposed && Controls.Contains(child))
            {
                Controls.Remove(child);
            }
        }

        _nativeChildren.Clear();
        _placedNative.Clear();
    }

    private void UpdateHover(PointF? point, VisualElement? target)
    {
        var next = new List<VisualElement>();
        for (var current = target; current is not null; current = current.ParentElement)
        {
            next.Add(current);
        }

        var args = point is { } position
            ? new VisualPointerEventArgs(position, MouseButtons.None, MouseButtons.None, 1, 0, ModifierKeys)
            : null;
        foreach (var element in _hoverChain)
        {
            if (!next.Contains(element))
            {
                element.SetPointerOver(false, args);
            }
        }

        foreach (var element in next)
        {
            if (!_hoverChain.Contains(element))
            {
                element.SetPointerOver(true, args);
            }
        }

        _hoverChain.Clear();
        _hoverChain.AddRange(next);
    }

    private void EnsureLayout(Size size)
    {
        if (_root is null || (_layoutValid && _layoutSize == size))
        {
            return;
        }

        _placedNative.Clear();
        _root.Measure(size);
        _root.Arrange(new RectangleF(0f, 0f, size.Width, size.Height));

        // 覆盖层不属于根树：它铺满客户区，子项（Popup）自己决定位置，不参与面板排版。
        _overlay.Measure(size);
        _overlay.Arrange(new RectangleF(0f, 0f, size.Width, size.Height));
        foreach (var popup in _openPopups.ToArray())
        {
            popup.Reposition();
        }

        // A host that was collapsed or removed from the tree never reaches ArrangeOverride, so its HWND would stay
        // floating above the surface. Anything not visited by this pass is detached.
        foreach (var host in _nativeChildren.Keys.ToArray())
        {
            if (!_placedNative.Contains(host))
            {
                RemoveNative(host);
            }
        }

        _layoutSize = size;
        _layoutValid = true;
    }

    private static VisualElement? FindFocusableAncestor(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Focusable && current.IsEnabled && current.IsVisible)
            {
                return current;
            }
        }

        return null;
    }

    private void MoveFocus(bool forward)
    {
        var order = new List<VisualElement>();
        if (_root is not null)
        {
            CollectFocusable(_root, order);
        }

        if (order.Count == 0)
        {
            return;
        }

        var index = _focused is null ? -1 : order.IndexOf(_focused);
        var next = forward
            ? (index + 1) % order.Count
            : (index <= 0 ? order.Count - 1 : index - 1);
        FocusElement(order[next]);
    }

    private static void CollectFocusable(VisualElement element, List<VisualElement> order)
    {
        if (!element.IsVisible)
        {
            return;
        }

        if (element.Focusable && element.IsEnabled)
        {
            order.Add(element);
        }

        foreach (var child in element.Children)
        {
            if (child is VisualElement elementChild)
            {
                CollectFocusable(elementChild, order);
            }
        }
    }
}

// 覆盖层只承载弹层：它铺满客户区、自己不取命中，子项位置由 VisualPopup 直接写 Bounds。
internal sealed class OverlayLayer : VisualElement
{
    internal OverlayLayer()
    {
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    protected override void ArrangeOverride(Size finalSize)
    {
    }
}
