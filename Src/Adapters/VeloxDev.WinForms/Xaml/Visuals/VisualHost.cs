using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// The single window that hosts a tree of <see cref="VisualElement"/> objects (or a plain <see cref="ContainerVisual"/>
/// tree) and routes all input to the visual elements it contains.
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
public class VisualHost : System.Windows.Forms.Control
{
    private const int WsClipChildren = 0x02000000;

    private ContainerVisual? _root;
    private VisualElement? _rootElement;
    private VisualElement? _focused;
    private VisualElement? _capture;
    private bool _focusVisible = true;
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
    private float _dpiScale;
    private bool _dpiScaleValid;
    private float _dpiScaleOverride;
    private Bitmap? _motionBitmap;
    private float _motionBitmapScale;
    private bool _renderingMotion;
    private bool _gestureDrewMotionFrame;

    /// <summary>Creates the surface with the double-buffered paint style it needs.</summary>
    public VisualHost()
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
        Root = new ContainerVisual();
        _styles.Changed += (_, _) => RefreshStyles();
        _overlay.SetSurfaceRoot(this);
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Raised after the root tree has been measured and arranged.</summary>
    public event EventHandler? LayoutUpdated;

    /// <summary>The <see cref="System.Diagnostics.Stopwatch"/> timestamp of the last completed paint.</summary>
    public long LastPaintTimestamp { get; private set; }

    /// <summary>The number of completed paints; animation drivers use it to tell whether a drag already advanced them.</summary>
    public long PaintCount { get; private set; }

    /// <summary>Whether the last completed paint was requested by an animation tick rather than input.</summary>
    public bool LastPaintWasClockDriven { get; private set; }

    private bool nextPaintClockDriven;

    /// <summary>Marks the next paint as an animation tick, so a drag paint and a clock paint can be told apart.</summary>
    public void MarkNextPaintAsClockDriven() => nextPaintClockDriven = true;

    /// <summary>
    /// The render scale used for frames painted while a pointer gesture is in progress; <c>1</c> disables it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While <see cref="IsPointerGestureActive"/> is <see langword="true"/> and this value is below <c>1</c>, the
    /// client area is painted into a reusable offscreen bitmap at this fraction of its logical resolution and then
    /// stretched back to full size. Motion frames keep the flow animation ticking; the next frame after the gesture
    /// ends is painted at full resolution.
    /// </para>
    /// <para>
    /// Only the window paint path is affected: <see cref="RenderTo"/> always renders at full resolution. Values below
    /// <c>0.1</c> are treated as <c>0.1</c>. The default is <c>0.65</c>.
    /// </para>
    /// </remarks>
    public float MotionScale { get; set; } = 1f;

    /// <summary>Whether a pointer press is still waiting for its release.</summary>
    /// <remarks>
    /// Set by <see cref="ProcessPointerDown"/>, cleared by <see cref="ProcessPointerUp"/> and when the native mouse
    /// capture is lost. A synthesized press without a window handle stays active until an explicit release.
    /// </remarks>
    public bool IsPointerGestureActive { get; private set; }

    /// <summary>Styles applied implicitly to matching elements in this surface's visual tree.</summary>
    /// <remarks>
    /// An element's own <see cref="VisualElement.Styles"/> collection and its ancestors' collections take precedence
    /// over this one; an explicit <see cref="VisualElement.Style"/> takes precedence over all of them. The tree is
    /// re-resolved whenever this collection changes or the root is assigned.
    /// </remarks>
    public VisualStyleCollection Styles => _styles;

    /// <summary>Resources offered to every element in this surface's tree after its own and its ancestors' dictionaries.</summary>
    public ResourceDictionary Resources => _resources;

    /// <summary>The root visual painted by this host; a <see cref="VisualElement"/> also receives layout and input.</summary>
    public ContainerVisual? Root
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
            _rootElement?.SetSurfaceRoot(null);
            _rootElement?.RefreshImplicitStyles();
            _root = value;
            _rootElement = value as VisualElement;
            _rootElement?.SetSurfaceRoot(this);
            RefreshStyles();
            InvalidateSurface();
        }
    }

    /// <summary>The element that currently receives keyboard and text input.</summary>
    public VisualElement? FocusedElement => _focused;

    /// <summary>Whether the surface currently has the native keyboard focus.</summary>
    public bool HasKeyboardFocus => Focused;

    /// <summary>The physical pixels per logical unit; 1 while the control has no handle.</summary>
    /// <remarks>
    /// Layout, input and painting all use logical device-independent pixels (DIPs). The control scales the logical
    /// tree by <see cref="DpiScale"/> when it paints, so element sizes and authored fonts stay identical at every
    /// monitor scale. Tests can pin a scale with <see cref="DpiScaleOverride"/>.
    /// </remarks>
    public float DpiScale
    {
        get
        {
            if (_dpiScaleOverride > 0f)
            {
                return _dpiScaleOverride;
            }

            if (!IsHandleCreated)
            {
                return 1f;
            }

            if (_dpiScaleValid)
            {
                return _dpiScale;
            }

            _dpiScale = DeviceDpiValue / 96f;
            _dpiScaleValid = true;
            return _dpiScale;
        }
    }

    /// <summary>The client area in logical units: <see cref="Control.ClientSize"/> divided by <see cref="DpiScale"/>.</summary>
    public Size LogicalClientSize
    {
        get
        {
            var scale = DpiScale;
            if (scale <= 0f)
            {
                scale = 1f;
            }

            return new Size(
                Math.Max(0, (int)Math.Round(ClientSize.Width / scale)),
                Math.Max(0, (int)Math.Round(ClientSize.Height / scale)));
        }
    }

    /// <summary>Pins <see cref="DpiScale"/> instead of deriving it from the device; 0 restores automatic scaling.</summary>
    /// <remarks>
    /// Intended for tests and for hosts that preview a different monitor scale. It does not change the control's
    /// actual DPI awareness; it only tells layout, painting and input which scale to assume.
    /// </remarks>
    public float DpiScaleOverride
    {
        get => _dpiScaleOverride;
        set
        {
            var normalized = Math.Max(0f, value);
            if (Math.Abs(_dpiScaleOverride - normalized) < 0.0001f)
            {
                return;
            }

            _dpiScaleOverride = normalized;
            _dpiScaleValid = false;
            InvalidateSurface();
        }
    }

    private int DeviceDpiValue
    {
        get
        {
#if NETFRAMEWORK
            using var graphics = Graphics.FromHwnd(Handle);
            return (int)Math.Round(graphics.DpiX);
#else
            return DeviceDpi;
#endif
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

        // 脏区以逻辑单位传入，Invalidate 却按物理客户区像素工作。
        var rectangle = ToPhysical(Rectangle.Round(damage));
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
    /// <remarks>
    /// This is the deterministic test and export path: <paramref name="size"/> and every coordinate are logical,
    /// and no DPI transform is applied even when the control itself runs at a non-96 DPI. Use
    /// <see cref="RenderPhysical"/> to paint a real client area at the current <see cref="DpiScale"/>.
    /// </remarks>
    public void RenderTo(Graphics graphics, Size size)
        => RenderCore(graphics, size, new Rectangle(0, 0, size.Width, size.Height));

    // 把逻辑树画到物理客户区：先按 DPI 缩放世界坐标，再由 RenderCore 按逻辑尺寸排版。
    internal void RenderPhysical(Graphics graphics, Rectangle deviceClip)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));

        var scale = DpiScale;
        if (scale <= 0f)
        {
            scale = 1f;
        }

        var logicalClip = ToLogical(deviceClip, scale);
        if (logicalClip.Width <= 0 || logicalClip.Height <= 0)
        {
            return;
        }

        var logicalSize = LogicalClientSize;
        var state = graphics.Save();
        try
        {
            if (Math.Abs(scale - 1f) > 0.0001f)
            {
                graphics.ScaleTransform(scale, scale);
            }

            // 裁剪区必须在缩放之后设置，才会和其余坐标一样按逻辑单位解释。
            graphics.SetClip(logicalClip);

            // 手势期间按 MotionScale 降分辨率重画到离屏位图，再整体贴回逻辑矩形；目标已带 DPI 变换与裁剪。
            if (IsPointerGestureActive && MotionScale < 0.999f && logicalSize.Width > 0 && logicalSize.Height > 0)
            {
                RenderMotion(graphics, logicalSize, logicalClip);
            }
            else
            {
                RenderCore(graphics, logicalSize, logicalClip);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    // 整面画进按 MotionScale 缩小的复用位图；DPI 缩放仍由目标 Graphics 承担，这里只处理逻辑坐标。
    private void RenderMotion(Graphics target, Size logicalSize, Rectangle damage)
    {
        if (_renderingMotion)
        {
            RenderCore(target, logicalSize, damage);
            return;
        }

        var motionScale = MotionScale;
        if (motionScale < 0.1f)
        {
            motionScale = 0.1f;
        }

        var bitmapWidth = Math.Max(1, (int)Math.Ceiling(logicalSize.Width * motionScale));
        var bitmapHeight = Math.Max(1, (int)Math.Ceiling(logicalSize.Height * motionScale));
        var bitmap = _motionBitmap;
        if (bitmap is null
            || bitmap.Width != bitmapWidth
            || bitmap.Height != bitmapHeight
            || Math.Abs(_motionBitmapScale - motionScale) > 0.0001f)
        {
            _motionBitmap?.Dispose();
            bitmap = new Bitmap(bitmapWidth, bitmapHeight, PixelFormat.Format32bppPArgb);
            _motionBitmap = bitmap;
            _motionBitmapScale = motionScale;
        }

        _renderingMotion = true;
        try
        {
            using var offscreen = Graphics.FromImage(bitmap);
            offscreen.ScaleTransform(motionScale, motionScale);
            // 离屏装的是整张位图，damage 只用于剔除远端子树；位图外的旧像素由目标裁剪区遮住。
            RenderCore(offscreen, logicalSize, damage);
        }
        finally
        {
            _renderingMotion = false;
        }

        _gestureDrewMotionFrame = true;
        target.DrawImage(
            bitmap,
            new RectangleF(0f, 0f, logicalSize.Width, logicalSize.Height));
    }

    private void RenderCore(Graphics graphics, Size size, Rectangle clearArea)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));

        if (_root is null)
        {
            return;
        }

        // 脏区小于整面时才参与子树剔除；整面重画（RenderTo / 手势结束帧）照旧画完整棵树。
        var damage = new RectangleF(clearArea.X, clearArea.Y, clearArea.Width, clearArea.Height);
        var cull = clearArea.Left > 0
                   || clearArea.Top > 0
                   || clearArea.Right < size.Width
                   || clearArea.Bottom < size.Height;
        var previousDamage = Visual.RenderDamage;
        var previousCull = Visual.CullToDamage;
        Visual.PushRenderDamage(damage, cull);

        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            EnsureLayout(size);
            _root.Render(graphics);
            _overlay.Render(graphics);
            LastPaintTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            PaintCount++;
            LastPaintWasClockDriven = nextPaintClockDriven;
            nextPaintClockDriven = false;
        }
        finally
        {
            graphics.Restore(state);
            Visual.PushRenderDamage(previousDamage, previousCull);
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
            && (element.Surface != this || !element.Focusable || !element.IsEffectivelyEnabled || !element.IsVisible))
        {
            return false;
        }

        if (ReferenceEquals(_focused, element))
        {
            if (element is not null)
            {
                element.SetFocusVisible(_focusVisible);
                Focus();
            }

            return true;
        }

        _focused?.SetFocused(false);
        _focused = element;
        _focused?.SetFocused(true);
        _focused?.SetFocusVisible(_focusVisible);
        if (element is not null)
        {
            Focus();
        }

        return true;
    }

    /// <summary>Injects a pointer press at a client point.</summary>
    /// <param name="point">The point in physical client pixels; it is converted to logical surface coordinates.</param>
    /// <param name="button">The pressed button.</param>
    /// <param name="clickCount">The click count; at least one.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerDown(PointF point, MouseButtons button, int clickCount = 1)
    {
        // 鼠标点击只转移焦点，不显示键盘焦点环；否则 Gallery 里每个 Toggle/Button 点一下都会多一圈蓝框。
        _focusVisible = false;
        _focused?.SetFocusVisible(false);
        point = ToLogical(point);
        var target = FindElementAt(point);
        DismissPopupsFor(target);
        // 禁用的祖先连同其子树都不接收这一笔按下；本地把 IsEnabled 写回 true 的子项也一样。
        var inputTarget = ResolveInputTarget(target);
        if (inputTarget is not null)
        {
            var focusable = FindFocusableAncestor(inputTarget);
            if (focusable is not null)
            {
                FocusElement(focusable);
            }
        }

        _capture = inputTarget;
        if (inputTarget is not null && IsHandleCreated)
        {
            Capture = true;
        }

        _buttons |= button;
        if (!IsPointerGestureActive)
        {
            _gestureDrewMotionFrame = false;
        }

        IsPointerGestureActive = true;

        if (inputTarget is not null)
        {
            var chain = new List<VisualElement>();
            for (var current = inputTarget; current is not null; current = current.ParentElement)
            {
                chain.Add(current);
            }

            for (var index = chain.Count - 1; index >= 0; index--)
            {
                var preview = new VisualPointerEventArgs(point, _buttons, button, clickCount, 0, ModifierKeys);
                chain[index].RaisePreviewPointerPressed(preview);
                if (preview.Handled)
                {
                    return true;
                }
            }
        }

        var args = new VisualPointerEventArgs(point, _buttons, button, clickCount, 0, ModifierKeys);
        inputTarget?.RaisePointerPressed(args);
        return args.Handled;
    }

    /// <summary>Injects a pointer move at a client point.</summary>
    /// <param name="point">The point in physical client pixels; it is converted to logical surface coordinates.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerMove(PointF point)
    {
        point = ToLogical(point);
        var hoverTarget = FindElementAt(point);
        // 禁用子树不进入悬停链，悬停态与按下态保持同一套启用判定。
        UpdateHover(point, ResolveInputTarget(hoverTarget));
        var target = ResolveInputTarget(_capture ?? hoverTarget);
        var args = new VisualPointerEventArgs(point, _buttons, MouseButtons.None, 1, 0, ModifierKeys);
        target?.RaisePointerMoved(args);
        Cursor = hoverTarget?.Cursor ?? Cursors.Default;
        return args.Handled;
    }

    /// <summary>Injects a pointer release at a client point.</summary>
    /// <param name="point">The point in physical client pixels; it is converted to logical surface coordinates.</param>
    /// <param name="button">The released button.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerUp(PointF point, MouseButtons button)
    {
        point = ToLogical(point);
        var target = ResolveInputTarget(_capture ?? FindElementAt(point) ?? _root as VisualElement);
        _buttons &= ~button;
        var args = new VisualPointerEventArgs(point, _buttons, button, 1, 0, ModifierKeys);
        target?.RaisePointerReleased(args);
        _capture = null;
        if (IsHandleCreated)
        {
            Capture = false;
        }

        EndPointerGesture();
        return args.Handled;
    }

    // 手势结束：若期间画过低分辨率帧，排一次整面重画换回清晰帧。
    private void EndPointerGesture()
    {
        if (!IsPointerGestureActive)
        {
            return;
        }

        IsPointerGestureActive = false;
        if (_gestureDrewMotionFrame)
        {
            _gestureDrewMotionFrame = false;
            InvalidatePaint();
        }
    }

    /// <summary>Injects a wheel turn at a client point.</summary>
    /// <param name="point">The point in physical client pixels; it is converted to logical surface coordinates.</param>
    /// <param name="delta">The wheel delta; positive scrolls up.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerWheel(PointF point, int delta)
    {
        point = ToLogical(point);
        var target = ResolveInputTarget(FindElementAt(point));
        var args = new VisualPointerEventArgs(point, _buttons, MouseButtons.None, 1, delta, ModifierKeys);
        target?.RaisePointerWheel(args);
        return args.Handled;
    }

    /// <summary>Injects a horizontal wheel turn at a client point.</summary>
    /// <param name="point">The point in physical client pixels; it is converted to logical surface coordinates.</param>
    /// <param name="delta">The horizontal wheel delta; positive scrolls right.</param>
    /// <returns><see langword="true"/> when an element handled the event.</returns>
    public bool ProcessPointerHWheel(PointF point, int delta)
    {
        point = ToLogical(point);
        var target = FindElementAt(point);
        var args = new VisualPointerEventArgs(
            point, _buttons, MouseButtons.None, 1, delta, ModifierKeys, isHorizontal: true);
        target?.RaisePointerWheel(args);
        return args.Handled;
    }

    private const int WmMouseHWheel = 0x020E;

    /// <inheritdoc />
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseHWheel)
        {
            var raw = m.LParam.ToInt64();
            var screen = new Point(unchecked((short)(raw & 0xFFFFL)), unchecked((short)((raw >> 16) & 0xFFFFL)));
            var point = PointToClient(screen);
            var delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFFL));
            ProcessPointerHWheel(point, delta);
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
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
        _focusVisible = true;
        _focused?.SetFocusVisible(true);
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

    /// <summary>Returns the deepest visual under a logical client point, or <see langword="null"/>.</summary>
    /// <param name="point">The point in logical client coordinates.</param>
    public Visual? HitTestVisual(Point point)
    {
        EnsureLayout(LogicalClientSize);
        return _root?.HitTestVisual(point);
    }

    /// <summary>Finds the deepest element under a logical surface point.</summary>
    /// <param name="point">The point in logical surface coordinates (DIPs), never physical client pixels.</param>
    public VisualElement? FindElementAt(PointF point)
    {
        if (_root is null)
        {
            return null;
        }

        EnsureLayout(LogicalClientSize);
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

    // bounds 是逻辑单位；原生 HWND 必须按物理客户区像素摆放。
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

        var physical = ToPhysical(bounds);
        if (child.Bounds != physical)
        {
            child.Bounds = physical;
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
        // 这里追踪的脏区是物理像素；RenderPhysical 再把它换算成逻辑单位。
        var damage = _damageFull
            ? new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)
            : e.ClipRectangle.Width > 0 && e.ClipRectangle.Height > 0
                ? e.ClipRectangle
                : _damage;
        if (damage.Width <= 0 || damage.Height <= 0)
        {
            return;
        }

        using (var background = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(background, damage);
        }

        RenderPhysical(e.Graphics, damage);

        _damage = Rectangle.Empty;
        _damageFull = false;
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _dpiScaleValid = false;
        InvalidateSurface();
    }

    /// <inheritdoc />
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        // 捕获被系统或别的控件拿走时，按起事件的释放不会再到达；手势必须在这里收尾。
        if (!Capture)
        {
            EndPointerGesture();
        }
    }

#if !NETFRAMEWORK
    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dpiScaleValid = false;
        InvalidateSurface();
    }
#endif

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _dpiScaleValid = false;
        _motionBitmap?.Dispose();
        _motionBitmap = null;
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
        // PointToClient 返回物理坐标，悬停链用的是逻辑坐标。
        var point = ToLogical(PointToClient(Cursor.Position));
        UpdateHover(point, ResolveInputTarget(FindElementAt(point)));
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
            // 主题是静态状态；不摘订阅，静态事件会把宿主钉在内存里。
            Theme.Changed -= OnThemeChanged;
            CloseAllPopups();
            ClearNativeChildren();
            _focused = null;
            _capture = null;
            _hoverChain.Clear();
            IsPointerGestureActive = false;
            _motionBitmap?.Dispose();
            _motionBitmap = null;
            _rootElement?.Dispose();
            _rootElement = null;
            _root = null;
            _overlay.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshStyles()
    {
        _rootElement?.RefreshImplicitStyles();
        _overlay.RefreshImplicitStyles();
    }

    // 主题色由控件在绘制时读取，切换 Theme.Mode 只失效表面即可重绘。
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        InvalidateSurface();
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

    private PointF ToLogical(PointF point)
    {
        var scale = DpiScale;
        return scale <= 0f || Math.Abs(scale - 1f) < 0.0001f
            ? point
            : new PointF(point.X / scale, point.Y / scale);
    }

    private Rectangle ToPhysical(Rectangle rectangle)
    {
        var scale = DpiScale;
        if (scale <= 0f || Math.Abs(scale - 1f) < 0.0001f)
        {
            return rectangle;
        }

        return new Rectangle(
            (int)Math.Floor(rectangle.X * scale),
            (int)Math.Floor(rectangle.Y * scale),
            Math.Max(1, (int)Math.Ceiling(rectangle.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(rectangle.Height * scale)));
    }

    private static Rectangle ToLogical(Rectangle rectangle, float scale)
        => scale <= 0f || Math.Abs(scale - 1f) < 0.0001f
            ? rectangle
            : new Rectangle(
                (int)Math.Floor(rectangle.X / scale),
                (int)Math.Floor(rectangle.Y / scale),
                Math.Max(1, (int)Math.Ceiling(rectangle.Width / scale)),
                Math.Max(1, (int)Math.Ceiling(rectangle.Height / scale)));

    private void EnsureLayout(Size size)
    {
        if (_root is null || (_layoutValid && _layoutSize == size))
        {
            return;
        }

        // 先认领这一趟：measure/arrange 里再有 InvalidateMeasure（滚动条折叠/展开就是这种）
        // 会把 _layoutValid 打回 false，下一帧继续排版，而不是被本次结尾的 true 吞掉。
        _layoutValid = true;
        _placedNative.Clear();
        if (_rootElement is { } elementRoot)
        {
            elementRoot.Measure(size);
            elementRoot.Arrange(new RectangleF(0f, 0f, size.Width, size.Height));
        }
        else
        {
            _root.Bounds = new RectangleF(0f, 0f, size.Width, size.Height);
        }

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
        LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }

    // 输入目标必须自身与所有祖先都启用；否则这一笔输入整段作废。
    private static VisualElement? ResolveInputTarget(VisualElement? element)
        => element is not null && element.IsEffectivelyEnabled ? element : null;

    private static VisualElement? FindFocusableAncestor(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Focusable && current.IsEffectivelyEnabled && current.IsVisible)
            {
                return current;
            }
        }

        return null;
    }

    private void MoveFocus(bool forward)
    {
        var order = new List<VisualElement>();
        if (_rootElement is not null)
        {
            CollectFocusable(_rootElement, order);
        }

        if (order.Count == 0)
        {
            return;
        }

        // TabIndex 升序；同值时保持遍历顺序（稳定 DFS），不会因排序实现而抖动。
        var ranked = new List<(VisualElement Element, int Index)>(order.Count);
        for (var index = 0; index < order.Count; index++)
        {
            ranked.Add((order[index], index));
        }

        ranked.Sort(static (left, right) =>
        {
            var byTabIndex = left.Element.TabIndex.CompareTo(right.Element.TabIndex);
            return byTabIndex != 0 ? byTabIndex : left.Index.CompareTo(right.Index);
        });

        var currentIndex = -1;
        for (var position = 0; position < ranked.Count; position++)
        {
            if (ReferenceEquals(ranked[position].Element, _focused))
            {
                currentIndex = position;
                break;
            }
        }

        var next = forward
            ? (currentIndex + 1) % ranked.Count
            : (currentIndex <= 0 ? ranked.Count - 1 : currentIndex - 1);
        FocusElement(ranked[next].Element);
    }

    private static void CollectFocusable(VisualElement element, List<VisualElement> order)
    {
        // 禁用的容器连子树一起跳过：里面的控件即使本地是启用态也不能被 Tab 到。
        if (!element.IsVisible || !element.IsEffectivelyEnabled)
        {
            return;
        }

        if (element.Focusable && element.IsTabStop)
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
