using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A surface that paints <see cref="Adorner"/> objects over the control tree it covers.
/// </summary>
/// <remarks>
/// <para>
/// Adorners are not child windows of this layer. The layer arranges each adorner against its
/// <see cref="Adorner.AdornedElement"/> in the layer's own coordinates and calls
/// <see cref="Adorner.Render(Graphics, Rectangle)"/> from its paint pass. That keeps the adorner on top of the
/// decorated element without adding another control to the tree.
/// </para>
/// <para>
/// A WinForms child window cannot composite over a sibling window, so the layer makes its background a layered
/// window colour key: pixels left at <see cref="ColorKey"/> are transparent to what is underneath, while painted
/// adorners remain visible.
/// </para>
/// </remarks>
public class AdornerLayer : Control
{
    /// <summary>The colour treated as transparent; fuchsia by default.</summary>
    public static readonly DependencyProperty ColorKeyProperty = DependencyProperty.Register(
        nameof(ColorKey),
        typeof(Color),
        typeof(AdornerLayer),
        new PropertyMetadata(Color.Fuchsia, OnColorKeyPropertyChanged));

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaColorKey = 0x00000001;

    private readonly AdornerCollection _adorners;

    /// <summary>Creates the layer with the paint style and colour-key transparency it needs.</summary>
    public AdornerLayer()
    {
        _adorners = new AdornerCollection(this);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;

        // 覆盖层不参与焦点与 Tab 顺序，否则键盘输入会被装饰层截走。
        Focusable = false;
        TabStop = false;
        Theme.Changed += OnThemeChanged;

        // 没有装饰元素时层不该盖在子控件上：色键窗口即使透明也会参与命中与重绘。
        Visible = false;
    }

    /// <summary>The adorners painted by this layer, in paint order.</summary>
    public IList<Adorner> Adorners => _adorners;

    /// <summary>Gets or sets the colour treated as transparent by the layer.</summary>
    public Color ColorKey
    {
        get => (Color)GetValue(ColorKeyProperty)!;
        set => SetValue(ColorKeyProperty, value);
    }

    /// <summary>Adds an adorner to the layer.</summary>
    /// <param name="adorner">The adorner to paint.</param>
    public void Add(Adorner adorner) => _adorners.Add(adorner);

    /// <summary>Removes an adorner from the layer.</summary>
    /// <param name="adorner">The adorner to remove.</param>
    /// <returns><see langword="true"/> when the adorner was found.</returns>
    public bool Remove(Adorner adorner)
    {
        var index = _adorners.IndexOf(adorner);
        if (index < 0)
        {
            return false;
        }

        _adorners.RemoveAt(index);
        return true;
    }

    /// <summary>Paints every adorner into <paramref name="graphics"/> using the layer's current geometry.</summary>
    /// <param name="graphics">The target graphics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graphics"/> is <see langword="null"/>.</exception>
    public void Render(Graphics graphics)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));

        if (_adorners.Count == 0)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
            graphics.SetClip(ClientRectangle);
            foreach (var adorner in _adorners)
            {
                if (adorner is null)
                {
                    continue;
                }

                var bounds = ResolveAdornerBounds(adorner);
                if (adorner.Bounds != bounds)
                {
                    adorner.Bounds = bounds;
                }

                adorner.Render(graphics, bounds);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        foreach (var adorner in _adorners)
        {
            if (adorner is null)
            {
                continue;
            }

            var bounds = ResolveAdornerBounds(adorner);
            if (adorner.Bounds != bounds)
            {
                adorner.Bounds = bounds;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 底色必须是色键本身：WinForms 父子窗口不能真正合成，色键是让下层像素透出来的唯一途径。
        using var brush = new SolidBrush(ColorKey);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (IsPainted)
        {
            Render(e.Graphics);
        }
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyColorKey();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    // 装饰元素与层常常是兄弟窗口，不能只沿 Parent 找层：先找共同祖先，再把两边相对祖先的偏移相减。
    private Rectangle ResolveAdornerBounds(Adorner adorner)
    {
        if (adorner.AdornedElement is not { } element || ReferenceEquals(element, this))
        {
            return ClientRectangle;
        }

        var ancestors = new HashSet<System.Windows.Forms.Control>();
        for (System.Windows.Forms.Control? current = this; current is not null; current = current.Parent)
        {
            ancestors.Add(current);
        }

        var elementX = 0;
        var elementY = 0;
        System.Windows.Forms.Control? common = null;
        for (var current = element; current is not null; current = current.Parent)
        {
            if (ancestors.Contains(current))
            {
                common = current;
                break;
            }

            elementX += current.Left;
            elementY += current.Top;
        }

        if (common is null)
        {
            return ClientRectangle;
        }

        var layerX = 0;
        var layerY = 0;
        for (System.Windows.Forms.Control? current = this; current is not null && !ReferenceEquals(current, common); current = current.Parent)
        {
            layerX += current.Left;
            layerY += current.Top;
        }

        return new Rectangle(
            elementX - layerX,
            elementY - layerY,
            Math.Max(0, element.Width),
            Math.Max(0, element.Height));
    }

    private static void OnColorKeyPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is AdornerLayer layer && !layer.IsDisposed)
        {
            layer.ApplyColorKey();
            layer.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    private void ApplyColorKey()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var handle = Handle;
        var extended = IntPtr.Size == 8 ? GetWindowLongPtr64(handle, GwlExStyle) : GetWindowLong32(handle, GwlExStyle);
        var layered = new IntPtr(extended.ToInt64() | WsExLayered);
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(handle, GwlExStyle, layered);
        }
        else
        {
            SetWindowLong32(handle, GwlExStyle, layered);
        }

        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpFrameChanged);
        SetLayeredWindowAttributes(handle, (uint)ColorTranslator.ToWin32(ColorKey), 0, LwaColorKey);
    }

    // 运行期设置 WS_EX_LAYERED 后必须让窗口重算边框，否则 SetLayeredWindowAttributes 不生效。
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    private void SyncVisibility() => Visible = _adorners.Count > 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    // 集合只负责让层在增删后重排重绘。
    private sealed class AdornerCollection(AdornerLayer owner) : Collection<Adorner>
    {
        protected override void InsertItem(int index, Adorner item)
        {
            base.InsertItem(index, item);
            owner.SyncVisibility();
            owner.PerformLayout();
            owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            owner.SyncVisibility();
            owner.PerformLayout();
            owner.Invalidate();
        }

        protected override void SetItem(int index, Adorner item)
        {
            base.SetItem(index, item);
            owner.SyncVisibility();
            owner.PerformLayout();
            owner.Invalidate();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            owner.SyncVisibility();
            owner.PerformLayout();
            owner.Invalidate();
        }
    }
}

/// <summary>
/// Draws decorative content over another control, positioned by an <see cref="AdornerLayer"/>.
/// </summary>
/// <remarks>
/// The default paint pass draws a thin accent border around the arranged bounds. Derive and override
/// <see cref="OnRenderAdorner"/> to draw something else; the layer calls it with bounds already expressed in the
/// layer's coordinates.
/// </remarks>
public class Adorner : Control
{
    /// <summary>The element this adorner is positioned against.</summary>
    public static readonly DependencyProperty AdornedElementProperty = DependencyProperty.Register(
        nameof(AdornedElement),
        typeof(System.Windows.Forms.Control),
        typeof(Adorner),
        new PropertyMetadata(null));

    /// <summary>Creates the adorner with the paint style it needs.</summary>
    public Adorner()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Focusable = false;
        TabStop = false;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets the element this adorner is positioned against.</summary>
    /// <remarks>When the layer cannot find this element in its own tree it arranges the adorner over the whole layer.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? AdornedElement
    {
        get => (System.Windows.Forms.Control?)GetValue(AdornedElementProperty);
        set => SetValue(AdornedElementProperty, value);
    }

    /// <summary>Paints this adorner into <paramref name="bounds"/>.</summary>
    /// <param name="graphics">The target graphics.</param>
    /// <param name="bounds">The bounds to draw in, in the caller's coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graphics"/> is <see langword="null"/>.</exception>
    public void Render(Graphics graphics, Rectangle bounds)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));
        OnRenderAdorner(graphics, bounds);
    }

    /// <summary>Draws the adorner's content.</summary>
    /// <param name="graphics">The target graphics.</param>
    /// <param name="bounds">The arranged bounds.</param>
    /// <remarks>The default implementation draws a one-pixel accent border from <see cref="Theme.Current"/>.</remarks>
    protected virtual void OnRenderAdorner(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new RectangleF(
            bounds.X + 0.5f,
            bounds.Y + 0.5f,
            Math.Max(0f, bounds.Width - 1f),
            Math.Max(0f, bounds.Height - 1f));
        if (body.Width <= 0f || body.Height <= 0f)
        {
            return;
        }

        using var pen = new Pen(Theme.Current.Accent, 1f);
        graphics.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (IsPainted)
        {
            OnRenderAdorner(e.Graphics, ClientRectangle);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
