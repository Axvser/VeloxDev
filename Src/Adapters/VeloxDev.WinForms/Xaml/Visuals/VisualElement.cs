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
/// border, pointer and keyboard events, and logical focus. It never creates a window; a <see cref="VisualSurface"/>
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

    private bool _measureValid;
    private Size _measureAvailable;
    private Size _desiredSize;
    private VisualSurface? _surface;
    private bool _focused;
    private bool _pointerOver;

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

    /// <summary>The inherited data context used by bindings and templates.</summary>
    public object? DataContext
    {
        get => GetValue(DataContextProperty);
        set => SetValue(DataContextProperty, value);
    }

    /// <summary>The size this element wants inside <paramref name="availableSize"/>.</summary>
    public Size DesiredSize => _desiredSize;

    /// <summary>Whether this element currently has logical focus inside its surface.</summary>
    public bool IsFocused => _focused;

    /// <summary>Whether the pointer is over this element or one of its descendants.</summary>
    public bool IsPointerOver => _pointerOver;

    /// <summary>The element that owns this one, or <see langword="null"/> for a root.</summary>
    public VisualElement? ParentElement => Parent as VisualElement;

    /// <summary>The surface that hosts this element, resolved through the parent chain.</summary>
    public VisualSurface? Surface => _surface ?? ParentElement?.Surface;

    /// <summary>The rendering DPI of the hosting surface; 96 when detached.</summary>
    protected int RenderDpi => Surface?.RenderDpi ?? 96;

    /// <summary>Raised after the pointer is pressed on this element or bubbles from a child.</summary>
    public event EventHandler<VisualPointerEventArgs>? PointerPressed;

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

    /// <summary>Adds a child element, clears a stale root surface and invalidates layout.</summary>
    /// <param name="child">The child to add.</param>
    /// <typeparam name="T">The child's concrete type.</typeparam>
    /// <returns>The added child.</returns>
    public T Add<T>(T child) where T : VisualElement
    {
        if (child is null) throw new ArgumentNullException(nameof(child));
        child._surface = null;
        base.Add(child);
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
    public void InvalidateVisual() => Surface?.InvalidatePaint();

    /// <summary>Moves logical focus to this element.</summary>
    /// <returns><see langword="true"/> when the surface accepted the focus.</returns>
    public bool Focus() => Surface?.FocusElement(this) ?? false;

    /// <summary>Converts a surface point into this element's local coordinates.</summary>
    /// <param name="surfacePoint">A point in the surface's client coordinates.</param>
    public PointF PointFromSurface(PointF surfacePoint)
    {
        var x = surfacePoint.X;
        var y = surfacePoint.Y;
        for (var current = this; current is not null; current = current.ParentElement)
        {
            x -= current.Bounds.X;
            y -= current.Bounds.Y;
        }

        return new PointF(x, y);
    }

    /// <summary>Converts this element's local point into the surface's client coordinates.</summary>
    /// <param name="localPoint">A point in this element's local coordinates.</param>
    public PointF PointToSurface(PointF localPoint)
    {
        var x = localPoint.X;
        var y = localPoint.Y;
        for (var current = this; current is not null; current = current.ParentElement)
        {
            x += current.Bounds.X;
            y += current.Bounds.Y;
        }

        return new PointF(x, y);
    }

    /// <summary>Returns this element's rectangle in the surface's client coordinates.</summary>
    public RectangleF AbsoluteBounds
    {
        get
        {
            var origin = PointToSurface(PointF.Empty);
            return new RectangleF(origin.X, origin.Y, Bounds.Width, Bounds.Height);
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

        foreach (var child in Children.ToArray())
        {
            if (child is VisualElement element)
            {
                element.Dispose();
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

    internal void SetSurfaceRoot(VisualSurface? surface) => _surface = surface;

    internal void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
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
