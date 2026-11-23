using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// The layout, input and rendering base of the HWND-free visual control library.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="VisualElement"/> is a <see cref="ContainerVisual"/> with a measure/arrange pass, a background and
/// border, pointer and keyboard events, and logical focus. It never creates a window; a <see cref="VisualHost"/>
/// owns the single HWND and paints the whole tree in one pass.
/// </para>
/// <para>
/// <see cref="Visual.Bounds"/> is the arranged rectangle in the parent's coordinates; panels arrange children into
/// local coordinates, and the inherited <see cref="Visual.Render"/> pass translates before drawing content.
/// </para>
/// </remarks>
public abstract class VisualElement : ContainerVisual, IDisposable
{
    /// <summary>The font used by text-bearing elements that do not set one.</summary>
    public static readonly Font DefaultFont = new("Segoe UI", 9.5f);

    /// <summary>The outer spacing around an element.</summary>
    public static readonly DependencyProperty MarginProperty = DependencyProperty.Register(
        nameof(Margin), typeof(XamlThickness), typeof(VisualElement),
        new PropertyMetadata(XamlThickness.Empty) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The inner spacing between an element's edge and its children.</summary>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(XamlThickness), typeof(VisualElement),
        new PropertyMetadata(XamlThickness.Empty) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The requested width; <see cref="double.NaN"/> sizes to content and alignment.</summary>
    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(VisualElement),
        new PropertyMetadata(double.NaN) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The requested height; <see cref="double.NaN"/> sizes to content and alignment.</summary>
    public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(
        nameof(Height), typeof(double), typeof(VisualElement),
        new PropertyMetadata(double.NaN) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The smallest width layout may assign.</summary>
    public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register(
        nameof(MinWidth), typeof(double), typeof(VisualElement),
        new PropertyMetadata(0d) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The smallest height layout may assign.</summary>
    public static readonly DependencyProperty MinHeightProperty = DependencyProperty.Register(
        nameof(MinHeight), typeof(double), typeof(VisualElement),
        new PropertyMetadata(0d) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The largest width layout may assign.</summary>
    public static readonly DependencyProperty MaxWidthProperty = DependencyProperty.Register(
        nameof(MaxWidth), typeof(double), typeof(VisualElement),
        new PropertyMetadata(double.PositiveInfinity) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The largest height layout may assign.</summary>
    public static readonly DependencyProperty MaxHeightProperty = DependencyProperty.Register(
        nameof(MaxHeight), typeof(double), typeof(VisualElement),
        new PropertyMetadata(double.PositiveInfinity) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>How an element is placed inside its slot horizontally.</summary>
    public static readonly DependencyProperty HorizontalAlignmentProperty = DependencyProperty.Register(
        nameof(HorizontalAlignment), typeof(XamlHorizontalAlignment), typeof(VisualElement),
        new PropertyMetadata(XamlHorizontalAlignment.Stretch) { AffectsArrange = true, AffectsRender = false });

    /// <summary>How an element is placed inside its slot vertically.</summary>
    public static readonly DependencyProperty VerticalAlignmentProperty = DependencyProperty.Register(
        nameof(VerticalAlignment), typeof(XamlVerticalAlignment), typeof(VisualElement),
        new PropertyMetadata(XamlVerticalAlignment.Stretch) { AffectsArrange = true, AffectsRender = false });

    /// <summary>Whether an element is painted, laid out and hit-tested.</summary>
    public static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register(
        nameof(Visibility), typeof(XamlVisibility), typeof(VisualElement),
        new PropertyMetadata(XamlVisibility.Visible) { AffectsMeasure = true });

    /// <summary>Whether an element accepts input and draws an enabled state.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.Register(
        nameof(IsEnabled), typeof(bool), typeof(VisualElement),
        new PropertyMetadata(true));

    /// <summary>Whether an element participates in the focus chain.</summary>
    public static readonly DependencyProperty FocusableProperty = DependencyProperty.Register(
        nameof(Focusable), typeof(bool), typeof(VisualElement),
        new PropertyMetadata(false) { AffectsRender = false });

    /// <summary>Whether a pointer hit lands on an element's own rectangle.</summary>
    public static readonly DependencyProperty IsHitTestVisibleProperty = DependencyProperty.Register(
        nameof(IsHitTestVisible), typeof(bool), typeof(VisualElement),
        new PropertyMetadata(true) { AffectsRender = false });

    /// <summary>The cursor shown while the pointer is over an element.</summary>
    public static readonly DependencyProperty CursorProperty = DependencyProperty.Register(
        nameof(Cursor), typeof(Cursor), typeof(VisualElement),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The outline brush drawn around an element's rectangle.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(XamlBrush), typeof(VisualElement),
        new PropertyMetadata(null));

    /// <summary>The outline width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(XamlThickness), typeof(VisualElement),
        new PropertyMetadata(XamlThickness.Empty) { AffectsMeasure = true });

    /// <summary>The background brush painted behind an element's content.</summary>
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(XamlBrush), typeof(VisualElement),
        new PropertyMetadata(null));

    /// <summary>The background and border corner radius in pixels.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(float), typeof(VisualElement),
        new PropertyMetadata(0f));

    /// <summary>The inherited data context used by bindings and templates.</summary>
    public static readonly DependencyProperty DataContextProperty = DependencyProperty.Register(
        nameof(DataContext), typeof(object), typeof(VisualElement),
        new PropertyMetadata(null) { Inherits = true, AffectsRender = false });

    /// <summary>The control whose template created this element, or <see langword="null"/> outside a template.</summary>
    public static readonly DependencyProperty TemplatedParentProperty = DependencyProperty.Register(
        nameof(TemplatedParent), typeof(VisualElement), typeof(VisualElement),
        new PropertyMetadata(null) { AffectsRender = false });

    private bool _measureValid;
    private Size _measureAvailable;
    private Size _desiredSize;
    private VisualHost? _surface;
    private bool _focused;
    private bool _focusVisible;
    private bool _pointerOver;
    private VisualStyle? _explicitStyle;
    private VisualStyle? _implicitStyle;
    private VisualStyleApplication? _styleApplication;
    private VisualStyleCollection? _styles;
    private ResourceDictionary? _resources;
    private VisualNameScope? _templateScope;

    /// <summary>The outer spacing around this element.</summary>
    public XamlThickness Margin
    {
        get => (XamlThickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
    }

    /// <summary>The inner spacing between this element's edge and its children.</summary>
    public XamlThickness Padding
    {
        get => (XamlThickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>The requested width; <see cref="double.NaN"/> sizes to content and alignment.</summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>The requested height; <see cref="double.NaN"/> sizes to content and alignment.</summary>
    public double Height
    {
        get => (double)GetValue(HeightProperty)!;
        set => SetValue(HeightProperty, value);
    }

    /// <summary>The smallest width layout may assign.</summary>
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>The smallest height layout may assign.</summary>
    public double MinHeight
    {
        get => (double)GetValue(MinHeightProperty)!;
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>The largest width layout may assign.</summary>
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>The largest height layout may assign.</summary>
    public double MaxHeight
    {
        get => (double)GetValue(MaxHeightProperty)!;
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>How this element is placed inside its slot horizontally.</summary>
    public XamlHorizontalAlignment HorizontalAlignment
    {
        get => (XamlHorizontalAlignment)GetValue(HorizontalAlignmentProperty)!;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>How this element is placed inside its slot vertically.</summary>
    public XamlVerticalAlignment VerticalAlignment
    {
        get => (XamlVerticalAlignment)GetValue(VerticalAlignmentProperty)!;
        set => SetValue(VerticalAlignmentProperty, value);
    }

    /// <summary>Whether this element is painted, laid out and hit-tested.</summary>
    public XamlVisibility Visibility
    {
        get => (XamlVisibility)GetValue(VisibilityProperty)!;
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>Whether this element accepts input and draws an enabled state.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty)!;
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Whether this element participates in the focus chain.</summary>
    public bool Focusable
    {
        get => (bool)GetValue(FocusableProperty)!;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>Whether a pointer hit lands on this element's own rectangle.</summary>
    /// <remarks>Panels set this to <see langword="false"/> when they have no background so content clicks pass through.</remarks>
    public bool IsHitTestVisible
    {
        get => (bool)GetValue(IsHitTestVisibleProperty)!;
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    /// <summary>The cursor shown while the pointer is over this element, or <see langword="null"/> for the default.</summary>
    public Cursor? Cursor
    {
        get => (Cursor?)GetValue(CursorProperty)!;
        set => SetValue(CursorProperty, value);
    }

    /// <summary>The outline brush drawn around the element's rectangle.</summary>
    public XamlBrush? BorderBrush
    {
        get => (XamlBrush?)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>The outline width per side, in pixels.</summary>
    public XamlThickness BorderThickness
    {
        get => (XamlThickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>The background brush painted behind the element's content.</summary>
    public new XamlBrush? Background
    {
        get => (XamlBrush?)GetValue(BackgroundProperty)!;
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The background and border corner radius in pixels.</summary>
    public new float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>A caller-facing identifier, or <see langword="null"/> when none was assigned.</summary>
    /// <remarks>
    /// The visual backend has no <c>NameScope</c> for ordinary elements, so this is a plain label used by the gallery
    /// inspector and tree dumps. Template parts are tracked by <see cref="VisualNameScope"/> instead.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>The inherited data context used by bindings and templates.</summary>
    public object? DataContext
    {
        get => GetValue(DataContextProperty);
        set => SetValue(DataContextProperty, value);
    }

    /// <summary>The control whose template created this element, or <see langword="null"/> outside a template.</summary>
    public VisualElement? TemplatedParent
    {
        get => (VisualElement?)GetValue(TemplatedParentProperty);
        set => SetValue(TemplatedParentProperty, value);
    }

    /// <summary>Resources offered to this element and its descendants by name.</summary>
    /// <remarks>
    /// A lookup checks this dictionary first, then each ancestor's, then <see cref="VisualHost.Resources"/>.
    /// Style resources are ordinary entries; <see cref="VisualResourceLookup.Get{T}"/> converts a stored string to
    /// the requested type.
    /// </remarks>
    public ResourceDictionary Resources => _resources ??= new ResourceDictionary();

    /// <summary>The size this element wants inside <paramref name="availableSize"/>.</summary>
    public Size DesiredSize => _desiredSize;

    /// <summary>Whether this element currently has logical focus inside its surface.</summary>
    public bool IsFocused => _focused;

    /// <summary>Whether focus was established by the keyboard, so the focus ring should be painted.</summary>
    /// <remarks>
    /// A pointer click focuses the control it lands on but does not raise this flag; it clears on the next pointer
    /// press. This keeps the visual focus ring meaningful without drawing a blue box after every mouse click.
    /// </remarks>
    public bool IsFocusVisible => _focused && _focusVisible;

    /// <summary>Whether the pointer is over this element or one of its descendants.</summary>
    public bool IsPointerOver => _pointerOver;

    /// <summary>The element that owns this one, or <see langword="null"/> for a root.</summary>
    public VisualElement? ParentElement => Parent as VisualElement;

    /// <summary>The surface that hosts this element, resolved through the parent chain.</summary>
    public VisualHost? Surface => _surface ?? ParentElement?.Surface;

    /// <summary>The DPI used to measure text.</summary>
    /// <remarks>
    /// Always 96: the visual tree is laid out in logical units and the surface applies the monitor scale once when
    /// painting. Reporting the physical control DPI here would make a point font both measure and render at the
    /// high DPI, then get scaled a second time by the world transform.
    /// </remarks>
    protected int RenderDpi => 96;

    /// <summary>The style explicitly assigned to this element, or <see langword="null"/>.</summary>
    /// <remarks>
    /// An explicit style takes precedence over any implicit style resolved from a <see cref="Styles"/> collection or
    /// <see cref="VisualHost.Styles"/>. Assigning <see langword="null"/> re-applies the implicit style, if any.
    /// </remarks>
    public VisualStyle? Style
    {
        get => _explicitStyle;
        set
        {
            if (ReferenceEquals(_explicitStyle, value))
            {
                return;
            }

            _explicitStyle = value;
            ApplyEffectiveStyle();
        }
    }

    /// <summary>Styles offered implicitly to this element and its descendants.</summary>
    /// <remarks>
    /// The nearest collection wins: this element's own collection is consulted first, then each ancestor's, then
    /// <see cref="VisualHost.Styles"/>. Within a collection the first style whose
    /// <see cref="VisualStyle.TargetType"/> accepts the element is used. The subtree is re-resolved when this
    /// collection changes, when a child is added through <see cref="Add{T}"/>, and when the surface root is assigned
    /// or replaced.
    /// </remarks>
    public VisualStyleCollection Styles => _styles ??= CreateStyles();

    /// <summary>Raised after the pointer is pressed on this element or bubbles from a child.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerPressed;

    /// <summary>Raised from the root down to the hit element before <see cref="PointerPressed"/>.</summary>
    public event EventHandler<VisualPointerEventArgs>? PreviewPointerPressed;

    /// <summary>Raised after the pointer moves over this element or bubbles from a child.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerMoved;

    /// <summary>Raised after the pointer is released on this element or bubbles from a child.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerReleased;

    /// <summary>Raised when the pointer enters this element.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this element.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerExited;

    /// <summary>Raised when the wheel turns over this element or bubbles from a child.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerWheel;

    /// <summary>Raised when a key goes down on this element or bubbles from a child.</summary>
    public event EventHandler<VisualKeyEventArgs>? KeyDown;

    /// <summary>Raised when a key goes up on this element or bubbles from a child.</summary>
    public event EventHandler<VisualKeyEventArgs>? KeyUp;

    /// <summary>Raised when text input reaches the focused element.</summary>
    public event EventHandler<VisualTextInputEventArgs>? TextInput;

    /// <summary>Raised after this element receives logical focus.</summary>
    public event EventHandler? GotFocus;

    /// <summary>Raised after this element loses logical focus.</summary>
    public event EventHandler? LostFocus;

    /// <summary>Adds a child element, applies the implicit styles it inherits, clears a stale root surface and invalidates layout.</summary>
    /// <param name="child">The child to add.</param>
    /// <typeparam name="T">The child's concrete type.</typeparam>
    /// <returns>The added child.</returns>
    public T Add<T>(T child) where T : VisualElement
    {
        if (child is null) throw new ArgumentNullException(nameof(child));
        child._surface = null;
        base.Add(child);
        child.RefreshImplicitStyles();
        InvalidateMeasure();
        return child;
    }

    /// <summary>Adds a child element and returns this element for chaining.</summary>
    /// <param name="child">The child to add.</param>
    /// <returns>This element.</returns>
    public VisualElement AddChild(VisualElement child)
    {
        Add(child);
        return this;
    }

    /// <summary>Removes a child element.</summary>
    /// <param name="child">The child to remove.</param>
    /// <returns><see langword="true"/> when the child was present.</returns>
    public bool RemoveChild(VisualElement child)
    {
        if (Surface?.FocusedElement is { } focused && IsAncestorOf(focused))
        {
            Surface.FocusElement(null);
        }

        if (!Remove(child))
        {
            return false;
        }

        child._surface = null;
        child.RefreshImplicitStyles();
        InvalidateMeasure();
        return true;
    }

    // 被移除的子树里可能正握着逻辑焦点；先交还焦点，TextBox 的插入符计时器才会停。
    private bool IsAncestorOf(VisualElement element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Measures this element inside <paramref name="availableSize"/> and caches the result.</summary>
    /// <param name="availableSize">The room the parent can offer.</param>
    /// <returns>The desired size including the margin.</returns>
    public Size Measure(Size availableSize)
    {
        if (_measureValid && _measureAvailable == availableSize)
        {
            return _desiredSize;
        }

        if (Visibility == XamlVisibility.Collapsed)
        {
            _measureValid = true;
            _measureAvailable = availableSize;
            _desiredSize = Size.Empty;
            return _desiredSize;
        }

        var margin = Margin;
        var innerAvailable = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(margin.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(margin.Vertical)));

        // 显式尺寸是内容可用的上限：固定宽的 Border/StackPanel 必须把宽度传给子项，
        // 否则子项先按父级的宽量成一行，换行高度在 Arrange 时才需要，已经被裁掉。
        var explicitWidth = (double)GetValue(WidthProperty)!;
        var explicitHeight = (double)GetValue(HeightProperty)!;
        if (!double.IsNaN(explicitWidth) && explicitWidth >= 0d)
        {
            innerAvailable.Width = (int)Math.Ceiling(explicitWidth);
        }

        if (!double.IsNaN(explicitHeight) && explicitHeight >= 0d)
        {
            innerAvailable.Height = (int)Math.Ceiling(explicitHeight);
        }

        var desired = MeasureOverride(innerAvailable);
        var width = ResolveAxis(Width, desired.Width, MinWidth, MaxWidth);
        var height = ResolveAxis(Height, desired.Height, MinHeight, MaxHeight);
        _desiredSize = new Size(
            (int)Math.Ceiling(width + margin.Horizontal),
            (int)Math.Ceiling(height + margin.Vertical));
        _measureAvailable = availableSize;
        _measureValid = true;
        return _desiredSize;
    }

    /// <summary>Arranges this element inside <paramref name="finalRect"/>, in the parent's coordinates.</summary>
    /// <param name="finalRect">The slot the parent assigns, including this element's margin.</param>
    public void Arrange(RectangleF finalRect)
    {
        if (Visibility == XamlVisibility.Collapsed)
        {
            Bounds = RectangleF.Empty;
            return;
        }

        var margin = Margin;
        var slot = new RectangleF(
            finalRect.X + margin.Left,
            finalRect.Y + margin.Top,
            Math.Max(0f, finalRect.Width - margin.Horizontal),
            Math.Max(0f, finalRect.Height - margin.Vertical));

        var desired = new Size(
            Math.Max(0, _desiredSize.Width - (int)Math.Ceiling(margin.Horizontal)),
            Math.Max(0, _desiredSize.Height - (int)Math.Ceiling(margin.Vertical)));
        var width = ResolveArranged(Width, desired.Width, MinWidth, MaxWidth, slot.Width, HorizontalAlignment == XamlHorizontalAlignment.Stretch);
        var height = ResolveArranged(Height, desired.Height, MinHeight, MaxHeight, slot.Height, VerticalAlignment == XamlVerticalAlignment.Stretch);

        var x = HorizontalAlignment switch
        {
            XamlHorizontalAlignment.Right => slot.Right - width,
            XamlHorizontalAlignment.Center => slot.X + ((slot.Width - width) / 2f),
            XamlHorizontalAlignment.Stretch when !double.IsNaN(Width) => slot.X + ((slot.Width - width) / 2f),
            _ => slot.X,
        };
        var y = VerticalAlignment switch
        {
            XamlVerticalAlignment.Bottom => slot.Bottom - height,
            XamlVerticalAlignment.Center => slot.Y + ((slot.Height - height) / 2f),
            XamlVerticalAlignment.Stretch when !double.IsNaN(Height) => slot.Y + ((slot.Height - height) / 2f),
            _ => slot.Y,
        };

        Bounds = new RectangleF(x, y, Math.Max(0f, width), Math.Max(0f, height));
        ArrangeOverride(new Size((int)Math.Ceiling(width), (int)Math.Ceiling(height)));
    }

    /// <summary>Marks this element and its ancestors as needing a new measure pass.</summary>
    public void InvalidateMeasure()
    {
        _measureValid = false;
        ParentElement?.InvalidateMeasure();
        Surface?.InvalidateSurface();
    }

    /// <summary>Marks this element and its ancestors as needing a new arrange pass.</summary>
    public void InvalidateArrange()
    {
        _measureValid = false;
        ParentElement?.InvalidateArrange();
        Surface?.InvalidateSurface();
    }

    /// <summary>Requests a repaint of the hosting surface without a new layout pass.</summary>
    public void InvalidateVisual()
    {
        // 最近的位图缓存要重画；InvalidateCache 会把外层缓存也一并标脏。
        VisualElement? cached = null;
        for (var current = this; current is not null; current = current.ParentElement)
        {
            if (current.CacheAsBitmap)
            {
                current.InvalidateCache();
                cached = current;
                break;
            }
        }

        if (Surface is not { } surface)
        {
            return;
        }

        // 缓存节点重画时可能同时刷新外层缓存；直接整面重画，避免漏掉更大的缓存区域。
        if (cached is not null)
        {
            surface.InvalidatePaint();
            return;
        }

        var damage = AbsoluteBounds;
        if (damage.Width <= 0f || damage.Height <= 0f)
        {
            surface.InvalidatePaint();
            return;
        }

        // 变换后的像素位置与 Bounds 不一致；效果还会画到 Bounds 之外。这两种走整面重画。
        for (var current = this; current is not null; current = current.ParentElement)
        {
            if (!current.Transform.IsIdentity)
            {
                surface.InvalidatePaint();
                return;
            }
        }

        damage.Inflate(2f + (Effect?.Padding ?? 0f), 2f + (Effect?.Padding ?? 0f));
        surface.InvalidatePaint(damage);
    }

    /// <summary>Moves logical focus to this element.</summary>
    /// <returns><see langword="true"/> when the surface accepted the focus.</returns>
    public bool Focus() => Surface?.FocusElement(this) ?? false;

    /// <summary>Converts a surface point into this element's local coordinates.</summary>
    /// <param name="surfacePoint">A point in the surface's client coordinates.</param>
    public PointF PointFromSurface(PointF surfacePoint)
    {
        var point = surfacePoint;
        for (var current = this; current is not null; current = current.ParentElement)
        {
            // Render applies Transform first, then Bounds translation; invert in the opposite order.
            point.X -= current.Bounds.X;
            point.Y -= current.Bounds.Y;
            if (!current.Transform.IsIdentity)
            {
                using var inverse = current.Transform.Clone();
                inverse.Invert();
                var points = new[] { point };
                inverse.TransformPoints(points);
                point = points[0];
            }
        }

        return point;
    }

    /// <summary>Converts this element's local point into the surface's client coordinates.</summary>
    /// <param name="localPoint">A point in this element's local coordinates.</param>
    public PointF PointToSurface(PointF localPoint)
    {
        var point = localPoint;
        for (var current = this; current is not null; current = current.ParentElement)
        {
            if (!current.Transform.IsIdentity)
            {
                var points = new[] { point };
                current.Transform.TransformPoints(points);
                point = points[0];
            }

            point.X += current.Bounds.X;
            point.Y += current.Bounds.Y;
        }

        return point;
    }

    /// <summary>Returns this element's transformed rectangle in the surface's client coordinates.</summary>
    public RectangleF AbsoluteBounds
    {
        get
        {
            // A scaled/rotated element is no longer axis-aligned; use the transformed corners.
            var topLeft = PointToSurface(PointF.Empty);
            var topRight = PointToSurface(new PointF(Bounds.Width, 0f));
            var bottomLeft = PointToSurface(new PointF(0f, Bounds.Height));
            var bottomRight = PointToSurface(new PointF(Bounds.Width, Bounds.Height));
            var minX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
            var minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
            var maxX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
            var maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
            return new RectangleF(minX, minY, Math.Max(0f, maxX - minX), Math.Max(0f, maxY - minY));
        }
    }

    /// <summary>Disposes this element and its descendants.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases element resources; derived controls stop timers here.</summary>
    /// <param name="disposing">Whether managed resources are released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        // 样式应用握着本元素的 PropertyChanged 订阅；释放时摘掉，避免事件把已释放对象拉回来。
        _styleApplication?.Apply(null);
        _explicitStyle = null;
        _implicitStyle = null;
        DisposeCache();

        foreach (var child in Children.ToArray())
        {
            if (child is VisualElement element)
            {
                element.Dispose();
            }
            else
            {
                child.DisposeCache();
            }
        }

        Children.Clear();
    }

    /// <inheritdoc />
    protected override object? GetInheritedValue(DependencyProperty property)
        => ReferenceEquals(property, DataContextProperty)
            ? ParentElement?.DataContext
            : base.GetInheritedValue(property);

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, VisibilityProperty))
        {
            IsVisible = Visibility == XamlVisibility.Visible;
        }
        else if (ReferenceEquals(e.Property, DataContextProperty))
        {
            NotifyDataContextDescendants(this, e.OldValue, e.NewValue);
        }

        if (e.Property.Metadata.AffectsMeasure)
        {
            InvalidateMeasure();
        }
        else if (e.Property.Metadata.AffectsArrange)
        {
            InvalidateArrange();
        }

        if (e.Property.Metadata.AffectsRender)
        {
            InvalidateVisual();
        }
    }

    // 子元素自己写了 DataContext 就截断继承；其余后代要收到 inherited 变更，绑定在祖先切换时才能重读。
    private static void NotifyDataContextDescendants(VisualElement parent, object? oldValue, object? newValue)
    {
        foreach (var child in parent.Children)
        {
            if (child is not VisualElement element || element.IsSet(DataContextProperty))
            {
                continue;
            }

            element.RaisePropertyChanged(DataContextProperty, oldValue, newValue, BindingPriority.Inherited);
            NotifyDataContextDescendants(element, oldValue, newValue);
        }
    }

    internal void SetSurfaceRoot(VisualHost? surface) => _surface = surface;

    // 模板里的每个后代都记着施模板的控件；TemplateBinding 靠它找源，模板触发器也写回它。
    internal void SetTemplatedParentRecursive(VisualElement? parent)
    {
        TemplatedParent = parent;
        foreach (var child in Children)
        {
            if (child is VisualElement element)
            {
                element.SetTemplatedParentRecursive(parent);
            }
        }
    }

    internal void SetTemplateScope(VisualNameScope? scope) => _templateScope = scope;

    /// <summary>Returns the named template part created for this element, or <see langword="null"/>.</summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="name">The <c>x:Name</c> the part was declared with inside the template.</param>
    /// <remarks>The scope belongs to one template instance; parts of another instance never resolve here.</remarks>
    public T? GetTemplateChild<T>(string name) where T : class
        => _templateScope?.Find<T>(name);

    // 样式有 Template 时由 VisualStyleApplication 回调；只有 ContentControl 覆写它。
    internal virtual void ApplyStyleTemplate(VisualControlTemplate? template)
    {
    }

    // 显式样式压过隐式；两者都为空时把两个层清干净。
    private void ApplyEffectiveStyle()
        => (_styleApplication ??= new VisualStyleApplication(this)).Apply(_explicitStyle ?? _implicitStyle);

    private VisualStyleCollection CreateStyles()
    {
        var styles = new VisualStyleCollection();
        styles.Changed += (_, _) => RefreshImplicitStyles();
        return styles;
    }

    internal void SetImplicitStyle(VisualStyle? style)
    {
        if (ReferenceEquals(_implicitStyle, style))
        {
            return;
        }

        _implicitStyle = style;
        if (_explicitStyle is null)
        {
            ApplyEffectiveStyle();
        }
    }

    // 从本元素起向下重解隐式样式；挂在祖先集合上的样式变化只需从那个祖先调用一次。
    internal void RefreshImplicitStyles()
    {
        SetImplicitStyle(ResolveImplicitStyle());
        foreach (var child in Children)
        {
            if (child is VisualElement element)
            {
                element.RefreshImplicitStyles();
            }
        }
    }

    private VisualStyle? ResolveImplicitStyle()
    {
        for (var current = this; current is not null; current = current.ParentElement)
        {
            if (current._styles?.FindMatch(this) is { } match)
            {
                return match;
            }
        }

        return Surface?.Styles.FindMatch(this);
    }

    internal void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        if (!focused)
        {
            _focusVisible = false;
        }

        InvalidateVisual();
        if (focused)
        {
            OnGotFocus();
            GotFocus?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            OnLostFocus();
            LostFocus?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void SetFocusVisible(bool visible)
    {
        if (_focusVisible == visible)
        {
            return;
        }

        _focusVisible = visible;
        InvalidateVisual();
    }

    internal void SetPointerOver(bool over, VisualPointerEventArgs? args)
    {
        if (_pointerOver == over)
        {
            return;
        }

        _pointerOver = over;
        InvalidateVisual();
        if (over)
        {
            OnPointerEntered(args ?? new VisualPointerEventArgs(PointF.Empty, MouseButtons.None, MouseButtons.None, 1, 0, Keys.None));
            PointerEntered?.Invoke(this, args ?? new VisualPointerEventArgs(PointF.Empty, MouseButtons.None, MouseButtons.None, 1, 0, Keys.None));
        }
        else
        {
            OnPointerExited(args ?? new VisualPointerEventArgs(PointF.Empty, MouseButtons.None, MouseButtons.None, 1, 0, Keys.None));
            PointerExited?.Invoke(this, args ?? new VisualPointerEventArgs(PointF.Empty, MouseButtons.None, MouseButtons.None, 1, 0, Keys.None));
        }
    }

    internal void RaisePreviewPointerPressed(VisualPointerEventArgs args)
        => PreviewPointerPressed?.Invoke(this, args);

    internal void RaisePointerPressed(VisualPointerEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnPointerPressed(args);
            current.PointerPressed?.Invoke(current, args);
        }
    }

    internal void RaisePointerMoved(VisualPointerEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnPointerMoved(args);
            current.PointerMoved?.Invoke(current, args);
        }
    }

    internal void RaisePointerReleased(VisualPointerEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnPointerReleased(args);
            current.PointerReleased?.Invoke(current, args);
        }
    }

    internal void RaisePointerWheel(VisualPointerEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnPointerWheel(args);
            current.PointerWheel?.Invoke(current, args);
        }
    }

    internal void RaiseKeyDown(VisualKeyEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnKeyDown(args);
            current.KeyDown?.Invoke(current, args);
        }
    }

    internal void RaiseKeyUp(VisualKeyEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnKeyUp(args);
            current.KeyUp?.Invoke(current, args);
        }
    }

    internal void RaiseTextInput(VisualTextInputEventArgs args)
    {
        for (var current = this; current is not null && !args.Handled; current = current.ParentElement)
        {
            current.OnTextInput(args);
            current.TextInput?.Invoke(current, args);
        }
    }

    /// <summary>Returns the size this element wants inside the available room.</summary>
    /// <param name="availableSize">The room left after the margin and padding.</param>
    protected virtual Size MeasureOverride(Size availableSize)
        => new(
            (int)Math.Ceiling(Padding.Horizontal),
            (int)Math.Ceiling(Padding.Vertical));

    /// <summary>Arranges this element's children inside its own local size.</summary>
    /// <param name="finalSize">The arranged content size.</param>
    protected virtual void ArrangeOverride(Size finalSize)
    {
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(
            0,
            0,
            Math.Max(0, (int)Math.Ceiling(Bounds.Width)),
            Math.Max(0, (int)Math.Ceiling(Bounds.Height)));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Background is { } || (BorderBrush is { } && !BorderThickness.IsEmpty))
        {
            XamlDrawing.PaintChromeWithBrushes(graphics, bounds, Background, BorderBrush, BorderThickness, CornerRadius);
        }

        OnRender(graphics);
    }

    /// <summary>Paints this element's own content in local coordinates.</summary>
    /// <param name="graphics">The target surface.</param>
    protected virtual void OnRender(Graphics graphics)
    {
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point) => IsHitTestVisible;

    /// <summary>Called when the pointer is pressed on this element.</summary>
    /// <param name="args">The pointer data.</param>
    protected virtual void OnPointerPressed(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when the pointer moves over this element.</summary>
    /// <param name="args">The pointer data.</param>
    protected virtual void OnPointerMoved(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when the pointer is released on this element.</summary>
    /// <param name="args">The pointer data.</param>
    protected virtual void OnPointerReleased(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when the pointer enters this element.</summary>
    /// <param name="args">The pointer data.</param>
    protected virtual void OnPointerEntered(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when the pointer leaves this element.</summary>
    /// <param name="args">The pointer data.</param>
    protected virtual void OnPointerExited(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when the wheel turns over this element.</summary>
    /// <param name="args">The pointer data; <see cref="VisualPointerEventArgs.Delta"/> carries the wheel delta.</param>
    protected virtual void OnPointerWheel(VisualPointerEventArgs args)
    {
    }

    /// <summary>Called when a key goes down on this element.</summary>
    /// <param name="args">The key data.</param>
    protected virtual void OnKeyDown(VisualKeyEventArgs args)
    {
    }

    /// <summary>Called when a key goes up on this element.</summary>
    /// <param name="args">The key data.</param>
    protected virtual void OnKeyUp(VisualKeyEventArgs args)
    {
    }

    /// <summary>Called when text input reaches this element.</summary>
    /// <param name="args">The text data.</param>
    protected virtual void OnTextInput(VisualTextInputEventArgs args)
    {
    }

    /// <summary>Called after this element receives logical focus.</summary>
    protected virtual void OnGotFocus()
    {
    }

    /// <summary>Called after this element loses logical focus.</summary>
    protected virtual void OnLostFocus()
    {
    }

    private static double ResolveAxis(double explicitValue, double desired, double minimum, double maximum)
    {
        var value = double.IsNaN(explicitValue) ? desired : explicitValue;
        value = Math.Max(value, Math.Max(0, minimum));
        return Math.Min(value, Math.Max(0, maximum));
    }

    private static float ResolveArranged(
        double explicitValue,
        int desired,
        double minimum,
        double maximum,
        float slot,
        bool stretch)
    {
        var value = double.IsNaN(explicitValue)
            ? (stretch ? slot : Math.Min(slot, desired))
            : explicitValue;
        value = Math.Max(value, Math.Max(0, minimum));
        value = Math.Min(value, Math.Max(0, maximum));
        return (float)Math.Min(value, slot);
    }
}
