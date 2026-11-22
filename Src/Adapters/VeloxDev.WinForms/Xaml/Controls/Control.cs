using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// The dependency-property host of this XAML layer: a <see cref="System.Windows.Forms.ScrollableControl"/> that also
/// carries dependency properties, bindings, triggers, resources and an inherited <see cref="DataContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the base control of the XAML layer: <see cref="Panel"/>, <see cref="ScrollableControl"/>,
/// <see cref="Canvas"/> and <see cref="UserControl"/> derive from it. Because it is itself a scrollable WinForms
/// control, it supports children, <see cref="System.Windows.Forms.ScrollableControl.AutoScroll"/> and focus without
/// extra machinery.
/// </para>
/// <para>
/// Native WinForms controls stay native. They can participate in a XAML tree, be named, have properties and events
/// set, and be bound through <see cref="System.Windows.Forms.Control.DataBindings"/>; they do not need to derive from
/// this class. Attached dependency properties work on any <see cref="System.Windows.Forms.Control"/> as well.
/// </para>
/// <para>
/// <see cref="DataContext"/> is inherited down the control tree. A child with no local value reads the nearest
/// ancestor <see cref="Control"/>'s context, and bindings re-resolve when an ancestor's context changes.
/// </para>
/// </remarks>
public class Control : System.Windows.Forms.ScrollableControl, IInputElement
{
    /// <summary>The inherited data context property.</summary>
    public static readonly DependencyProperty DataContextProperty = DependencyProperty.Register(
        nameof(DataContext),
        typeof(object),
        typeof(Control));

    /// <summary>Whether this control can receive keyboard focus; maps to the WinForms selectable control style.</summary>
    public static readonly DependencyProperty FocusableProperty = DependencyProperty.Register(
        nameof(Focusable),
        typeof(bool),
        typeof(Control),
        new PropertyMetadata(true, OnFocusableChanged));

    /// <summary>Whether this control currently has keyboard focus; updated from the native focus callbacks.</summary>
    public static readonly DependencyProperty IsKeyboardFocusedProperty = DependencyProperty.Register(
        nameof(IsKeyboardFocused),
        typeof(bool),
        typeof(Control));

    /// <summary>Whether the pointer is currently over this control or one of its descendants.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver),
        typeof(bool),
        typeof(Control));

    /// <summary>The outer spacing around this control, animatable like a WPF margin.</summary>
    /// <remarks>Every change is written to the native <see cref="System.Windows.Forms.Control.Margin"/>, so the WinForms layout engine keeps seeing it.</remarks>
    public static readonly DependencyProperty MarginProperty = DependencyProperty.Register(
        nameof(Margin),
        typeof(Thickness),
        typeof(Control),
        new PropertyMetadata(Thickness.DefaultMargin, OnMarginPropertyChanged) { AffectsMeasure = true });

    /// <summary>The inner spacing between this control's edge and its children, animatable like a WPF padding.</summary>
    /// <remarks>Every change is written to the native <see cref="System.Windows.Forms.ScrollableControl.Padding"/>, so native layout and scrolling keep seeing it.</remarks>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding),
        typeof(Thickness),
        typeof(Control),
        new PropertyMetadata(Thickness.Empty, OnPaddingPropertyChanged) { AffectsMeasure = true });

    /// <summary>The background brush. The default is <see langword="null"/>: a control with no authored background stays transparent instead of painting the WinForms system colour.</summary>
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background),
        typeof(Brush),
        typeof(Control),
        new PropertyMetadata(null, OnBackgroundPropertyChanged));

    /// <summary>The text brush; <see langword="null"/> falls back to the native <see cref="System.Windows.Forms.Control.ForeColor"/>.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(Control),
        new PropertyMetadata(null, OnForegroundPropertyChanged));

    /// <summary>The font used by this control; null means the native <see cref="System.Windows.Forms.Control.Font"/>.</summary>
    public static readonly DependencyProperty FontProperty = DependencyProperty.Register(
        nameof(Font),
        typeof(Font),
        typeof(Control),
        new PropertyMetadata(null, OnFontPropertyChanged) { AffectsMeasure = true });

    /// <summary>The requested width in pixels; a negative value leaves the native width in charge.</summary>
    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(-1, OnWidthPropertyChanged) { AffectsMeasure = true });

    /// <summary>The requested height in pixels; a negative value leaves the native height in charge.</summary>
    public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(
        nameof(Height),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(-1, OnHeightPropertyChanged) { AffectsMeasure = true });

    /// <summary>The smallest width layout may assign, in pixels.</summary>
    public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register(
        nameof(MinWidth),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(0, OnSizeConstraintChanged) { AffectsMeasure = true });

    /// <summary>The smallest height layout may assign, in pixels.</summary>
    public static readonly DependencyProperty MinHeightProperty = DependencyProperty.Register(
        nameof(MinHeight),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(0, OnSizeConstraintChanged) { AffectsMeasure = true });

    /// <summary>The largest width layout may assign, in pixels; <see cref="int.MaxValue"/> means unbounded.</summary>
    public static readonly DependencyProperty MaxWidthProperty = DependencyProperty.Register(
        nameof(MaxWidth),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(int.MaxValue, OnSizeConstraintChanged) { AffectsMeasure = true });

    /// <summary>The largest height layout may assign, in pixels; <see cref="int.MaxValue"/> means unbounded.</summary>
    public static readonly DependencyProperty MaxHeightProperty = DependencyProperty.Register(
        nameof(MaxHeight),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(int.MaxValue, OnSizeConstraintChanged) { AffectsMeasure = true });

    /// <summary>Whether the control and its children are painted and laid out.</summary>
    public static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register(
        nameof(Visibility),
        typeof(Visibility),
        typeof(Control),
        new PropertyMetadata(Visibility.Visible, OnVisibilityPropertyChanged) { AffectsMeasure = true });

    /// <summary>Whether the control accepts input; mirrors the native <see cref="System.Windows.Forms.Control.Enabled"/>.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.Register(
        nameof(IsEnabled),
        typeof(bool),
        typeof(Control),
        new PropertyMetadata(true, OnIsEnabledPropertyChanged));

    /// <summary>The position of this control in the tab order; <c>-1</c> means no explicit position.</summary>
    public static readonly DependencyProperty TabIndexProperty = DependencyProperty.Register(
        nameof(TabIndex),
        typeof(int),
        typeof(Control),
        new PropertyMetadata(-1, OnTabIndexPropertyChanged) { AffectsRender = false });

    /// <summary>Whether the Tab key can stop on this control; mirrors the native <see cref="System.Windows.Forms.Control.TabStop"/>.</summary>
    public static readonly DependencyProperty IsTabStopProperty = DependencyProperty.Register(
        nameof(IsTabStop),
        typeof(bool),
        typeof(Control),
        new PropertyMetadata(true, OnIsTabStopPropertyChanged) { AffectsRender = false });

    /// <summary>How a layout panel places this control horizontally inside its slot.</summary>
    public static readonly DependencyProperty HorizontalAlignmentProperty = DependencyProperty.Register(
        nameof(HorizontalAlignment),
        typeof(HorizontalAlignment),
        typeof(Control),
        new PropertyMetadata(HorizontalAlignment.Stretch) { AffectsArrange = true });

    /// <summary>How a layout panel places this control vertically inside its slot.</summary>
    public static readonly DependencyProperty VerticalAlignmentProperty = DependencyProperty.Register(
        nameof(VerticalAlignment),
        typeof(VerticalAlignment),
        typeof(Control),
        new PropertyMetadata(VerticalAlignment.Stretch) { AffectsArrange = true });

    private readonly Dictionary<DependencyProperty, object?> _values = [];
    private readonly Dictionary<DependencyProperty, BindingExpression> _bindings = [];
    private readonly List<BindingExpression> _attachedBindings = [];
    private readonly List<NativeBindingEntry> _nativeBindings = [];
    private NameScope? _nativeBindingScope;
    private readonly Dictionary<Trigger, TriggerRuntimeState> _triggerStates = [];
    private readonly Dictionary<Trigger, TriggerRuntimeState> _templateTriggerStates = [];
    private readonly ResourceDictionary _resources = new();
    private readonly InputBindingCollection _inputBindings = new();
    private readonly Dictionary<DependencyProperty, object?> _styleValues = [];
    private readonly Dictionary<DependencyProperty, BindingExpression> _styleSetterBindings = [];
    private readonly Dictionary<DependencyProperty, List<StyleTriggerValue>> _styleTriggerValues = [];
    private readonly List<Trigger> _styleTriggers = [];
    private readonly Dictionary<Trigger, int> _styleTriggerOrders = [];
    private readonly HashSet<Trigger> _observedDirectTriggers = [];
    private readonly List<Style> _appliedStyleChain = [];

    private TriggerCollection? _triggers;
    private Style? _explicitStyle;
    private Style? _appliedStyle;
    private Control? _styleResourceScope;
    private object? _styleResourceKey;
    private System.Windows.Forms.Control? _templateContent;
    private ControlTemplate? _templateTemplate;
    private readonly HashSet<Keys> _pressedKeys = [];
    private bool _evaluatingTriggers;
    private bool _applyingNativeBindings;
    private bool _pointerCaptured;
    private bool _disposed;
    private readonly Dictionary<System.Windows.Forms.Control, EventHandler> _nativeHoverHandlers = [];

    // 本控件最近一次测量的结果。只有 availableSize 相同才命中；失效时沿祖先链清到根。
    private bool _measureCacheValid;
    private Size _measureCacheAvailable;
    private Size _measureCacheResult;

    // 根控件持有的布局 pass 状态：子树里的失效请求在这里合并成一次 PerformLayout。
    private bool _layoutPassRunning;
    private bool _layoutPassRequested;

    // DP 清除（样式/触发器退出）时要还原到接管前的原生值，否则原生属性会停在最后一帧。
    private Font? _nativeFontFallback;
    private int? _nativeWidthFallback;
    private int? _nativeHeightFallback;
    private Padding? _nativeMarginFallback;
    private Padding? _nativePaddingFallback;

    /// <summary>Creates the control and connects its resource change signal.</summary>
    public Control()
    {
        // 所有 XAML 基础控件统一双缓冲：局部重绘先落到该控件自己的后缓冲，避免父/子窗口各画一半。
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        // 原生 Margin 默认 3；XAML 的读回路径在没人写 Margin 时会落到它，所以这里同步成 0。
        NativeMargin = System.Windows.Forms.Padding.Empty;
        _resources.Changed += OnResourcesChanged;
        ApplyFocusableStyle();
    }

    /// <inheritdoc />
    public bool Focusable
    {
        get => (bool)GetValue(FocusableProperty)!;
        set => SetValue(FocusableProperty, value);
    }

    /// <inheritdoc />
    public bool IsKeyboardFocused => (bool)GetValue(IsKeyboardFocusedProperty)!;

    /// <inheritdoc />
    public bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>
    /// Gets or sets the outer spacing around this control.
    /// </summary>
    /// <remarks>
    /// The native <see cref="System.Windows.Forms.Control.Margin"/> mirrors this value; reading an unset property
    /// returns the native one so a value written through a base-typed reference is still visible here.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Thickness Margin
    {
        get => HasXamlValue(MarginProperty) ? (Thickness)GetValue(MarginProperty)! : Thickness.FromPadding(base.Margin);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the inner spacing between this control's edge and its children.
    /// </summary>
    /// <remarks>
    /// The native <see cref="System.Windows.Forms.ScrollableControl.Padding"/> mirrors this value; reading an unset
    /// property returns the native one so a value written through a base-typed reference is still visible here.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Thickness Padding
    {
        get => HasXamlValue(PaddingProperty) ? (Thickness)GetValue(PaddingProperty)! : Thickness.FromPadding(base.Padding);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>Gets or sets the background brush.</summary>
    /// <remarks>
    /// When no brush is set the control stays transparent; a native <see cref="System.Windows.Forms.Control.BackColor"/>
    /// written directly still produces a brush so code that paints a card itself keeps working.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Brush? Background
    {
        get
        {
            if (HasXamlValue(BackgroundProperty))
            {
                return GetValue(BackgroundProperty) as Brush;
            }

            // 原生 BackColor 是环境继承值：没作者写过时它等于父控件的 BackColor（或系统默认），
            // 不能当成「作者设了背景」，否则子控件会用一块不透明底色盖住父控件的渐变/圆角。
            if (base.BackColor == DefaultBackColor)
            {
                return null;
            }

            return Parent is { } parent && base.BackColor == parent.BackColor
                ? null
                : new SolidColorBrush(base.BackColor);
        }
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Gets or sets the text brush; <see langword="null"/> falls back to the native <see cref="System.Windows.Forms.Control.ForeColor"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Brush? Foreground
    {
        get => HasXamlValue(ForegroundProperty)
            ? GetValue(ForegroundProperty) as Brush
            : new SolidColorBrush(base.ForeColor);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The colour <see cref="Background"/> paints, or the native fallback when no brush is authored.</summary>
    internal Color EffectiveBackground
        => HasXamlValue(BackgroundProperty) && GetValue(BackgroundProperty) is Brush background
            ? background.ApproximateColor
            : base.BackColor;

    /// <summary>The colour <see cref="Foreground"/> paints, or the native fallback when no brush is authored.</summary>
    internal Color EffectiveForeground
        => HasXamlValue(ForegroundProperty) && GetValue(ForegroundProperty) is Brush foreground
            ? foreground.ApproximateColor
            : base.ForeColor;

    /// <summary>Gets or sets the font; null falls back to the native <see cref="System.Windows.Forms.Control.Font"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Font Font
    {
        get => (Font?)GetValue(FontProperty) ?? base.Font;
        set => SetValue(FontProperty, value);
    }

    /// <summary>Gets or sets the requested width in pixels; a negative value leaves the native width in charge.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int Width
    {
        get
        {
            var value = (int)GetValue(WidthProperty)!;
            return value >= 0 ? value : base.Width;
        }
        set => SetValue(WidthProperty, value);
    }

    /// <summary>Gets or sets the requested height in pixels; a negative value leaves the native height in charge.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int Height
    {
        get
        {
            var value = (int)GetValue(HeightProperty)!;
            return value >= 0 ? value : base.Height;
        }
        set => SetValue(HeightProperty, value);
    }

    /// <summary>Gets or sets the smallest width layout may assign, in pixels.</summary>
    public int MinWidth
    {
        get => (int)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>Gets or sets the smallest height layout may assign, in pixels.</summary>
    public int MinHeight
    {
        get => (int)GetValue(MinHeightProperty)!;
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>Gets or sets the largest width layout may assign, in pixels.</summary>
    public int MaxWidth
    {
        get => (int)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>Gets or sets the largest height layout may assign, in pixels.</summary>
    public int MaxHeight
    {
        get => (int)GetValue(MaxHeightProperty)!;
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>Gets or sets whether this control is painted, laid out and able to receive input.</summary>
    public Visibility Visibility
    {
        get => (Visibility)GetValue(VisibilityProperty)!;
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>Gets or sets whether the control accepts input; mirrors the native <see cref="System.Windows.Forms.Control.Enabled"/>.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty)!;
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets this control's position in the tab order; <c>-1</c> means no explicit position.</summary>
    /// <remarks>Setting a value writes the native <see cref="System.Windows.Forms.Control.TabIndex"/>.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int TabIndex
    {
        get => (int)GetValue(TabIndexProperty)!;
        set => SetValue(TabIndexProperty, value);
    }

    /// <summary>Gets or sets whether the Tab key can stop on this control.</summary>
    /// <remarks>Setting a value writes the native <see cref="System.Windows.Forms.Control.TabStop"/>.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsTabStop
    {
        get => (bool)GetValue(IsTabStopProperty)!;
        set => SetValue(IsTabStopProperty, value);
    }

    /// <summary>Gets or sets how a layout panel places this control horizontally inside its slot.</summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalAlignmentProperty)!;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>Gets or sets how a layout panel places this control vertically inside its slot.</summary>
    public VerticalAlignment VerticalAlignment
    {
        get => (VerticalAlignment)GetValue(VerticalAlignmentProperty)!;
        set => SetValue(VerticalAlignmentProperty, value);
    }

    // 布局是否要为它留槽位：Collapsed 连槽位都不要，Hidden 只是不画。
    internal bool OccupiesLayoutSpace => Visibility != Visibility.Collapsed;

    /// <summary>Returns the size this control wants inside <paramref name="availableSize"/>.</summary>
    /// <param name="availableSize">The room the parent can offer; zero means unconstrained.</param>
    /// <remarks>The explicit width and height win over <see cref="GetPreferredSize"/>; repeated calls with the same
    /// available size reuse the cached result until <see cref="InvalidateMeasure"/>.</remarks>
    public Size Measure(Size availableSize) => MeasureCached(availableSize);

    /// <summary>Marks the measure of this control and every ancestor as stale.</summary>
    /// <remarks>
    /// A layout pass runs on the topmost XAML control and coalesces further requests made during that pass. This is
    /// the single invalidation entry for properties declared with <see cref="PropertyMetadata.AffectsMeasure"/>.
    /// </remarks>
    public void InvalidateMeasure()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        ClearMeasureCacheToRoot();
        RequestLayoutPass();
    }

    /// <summary>Marks the arrange of this control's subtree as stale without invalidating its measure.</summary>
    /// <remarks>Use it for alignment or position changes that do not alter the desired size.</remarks>
    public void InvalidateArrange()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        RequestLayoutPass();
    }

    internal Size MeasureCached(Size availableSize)
    {
        if (_measureCacheValid && _measureCacheAvailable == availableSize)
        {
            return _measureCacheResult;
        }

        var result = MeasureCore(availableSize);
        _measureCacheAvailable = availableSize;
        _measureCacheResult = result;
        _measureCacheValid = true;
        return result;
    }

    internal void ClearMeasureCache()
    {
        _measureCacheValid = false;
    }

    private void ClearMeasureCacheToRoot()
    {
        for (Control? current = this; current is not null; current = current.Parent as Control)
        {
            current._measureCacheValid = false;
        }
    }

    // 根控件的 PerformLayout 会递归走到整棵子树；期间再来的失效只标一次，结束后再跑一轮。
    private void RequestLayoutPass()
    {
        Control root = this;
        while (root.Parent is Control parent)
        {
            root = parent;
        }

        if (root._layoutPassRunning)
        {
            root._layoutPassRequested = true;
            return;
        }

        root._layoutPassRunning = true;
        try
        {
            for (var pass = 0; pass < 8 && !root.IsDisposed; pass++)
            {
                root._layoutPassRequested = false;
                root.PerformLayout();
                if (!root._layoutPassRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            root._layoutPassRunning = false;
        }
    }

    /// <summary>Returns the size this control wants inside <paramref name="availableSize"/>.</summary>
    /// <param name="availableSize">The room the parent can offer; zero means unconstrained.</param>
    /// <remarks>Derive and override to give a custom control or panel its own measure pass.</remarks>
    protected internal virtual Size MeasureCore(Size availableSize)
    {
        // 必须把可用宽高交给 GetPreferredSize：TextBlock/ContentControl 靠它决定换行后的高度。
        var preferred = GetPreferredSize(availableSize);
        return ConstrainSize(preferred, availableSize);
    }

    // 面板自己重写 MeasureCore 时也要走这里：显式 Width/Height 与 Min/Max 不能被内容尺寸绕过。
    internal Size ConstrainSize(Size preferred, Size availableSize)
    {
        var width = MeasureAxis(WidthProperty, preferred.Width, MinWidth, MaxWidth, availableSize.Width);
        var height = MeasureAxis(HeightProperty, preferred.Height, MinHeight, MaxHeight, availableSize.Height);
        return new Size(width, height);
    }

    internal int MeasureAxis(
        DependencyProperty sizeProperty,
        int preferred,
        int minimum,
        int maximum,
        int available)
    {
        var explicitValue = (int)GetValue(sizeProperty)!;
        var value = explicitValue >= 0 ? explicitValue : preferred;
        if (explicitValue < 0 && available > 0 && value <= 0)
        {
            // 没有显式尺寸、也没有首选尺寸时占满可用空间；否则容器会在 0 尺寸上反复测量。
            value = available;
        }

        value = Math.Max(value, Math.Max(0, minimum));
        return Math.Min(value, Math.Max(0, maximum));
    }

    // 自身内容是否要画：Hidden/Collapsed 都不画。
    internal bool IsPainted => Visibility == Visibility.Visible;

    /// <summary>Moves keyboard focus to this control when it is focusable.</summary>
    /// <returns><see langword="true"/> when the control received focus.</returns>
    public new bool Focus() => Focusable && base.Focus();

    /// <summary>Captures the pointer through the native WinForms mouse capture.</summary>
    /// <returns><see langword="true"/> when the capture was taken.</returns>
    public bool CapturePointer()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return false;
        }

        try
        {
            Capture = true;
            _pointerCaptured = Capture;
            return _pointerCaptured;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            _pointerCaptured = false;
            return false;
        }
    }

    /// <inheritdoc />
    public void ReleasePointerCapture()
    {
        _pointerCaptured = false;
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            if (Capture)
            {
                Capture = false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine($"[VeloxDev.Xaml] Could not release pointer capture: {exception.Message}");
        }
    }

    private static void OnFocusableChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control)
        {
            control.ApplyFocusableStyle();
            if (e.NewValue is false && control.IsKeyboardFocused)
            {
                control.SetValue(IsKeyboardFocusedProperty, false);
                FocusManager.NotifyFocusLeft(control);
            }
        }
    }

    private void ApplyFocusableStyle() => SetStyle(ControlStyles.Selectable, Focusable);

    private static void OnMarginPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Thickness margin)
        {
            return;
        }

        if (target is Control control)
        {
            if (!control.HasXamlValue(MarginProperty))
            {
                if (control._nativeMarginFallback is { } fallback)
                {
                    control.NativeMargin = fallback;
                    control._nativeMarginFallback = null;
                }

                return;
            }

            control._nativeMarginFallback ??= control.NativeMargin;
            control.NativeMargin = margin.ToPadding();
            return;
        }

        // Control.Padding / Control.Margin 也能以附加属性形式落在原生控件上。
        var native = margin.ToPadding();
        if (target.Margin != native)
        {
            target.Margin = native;
        }
    }

    private static void OnPaddingPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Thickness padding)
        {
            return;
        }

        if (target is Control control)
        {
            if (!control.HasXamlValue(PaddingProperty))
            {
                if (control._nativePaddingFallback is { } fallback)
                {
                    control.NativePadding = fallback;
                    control._nativePaddingFallback = null;
                }

                return;
            }

            control._nativePaddingFallback ??= control.NativePadding;
            control.NativePadding = padding.ToPadding();
            return;
        }

        var native = padding.ToPadding();
        if (target.Padding != native)
        {
            target.Padding = native;
        }
    }

    private static void OnBackgroundPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control)
        {
            control.OnBackColorChanged(EventArgs.Empty);
            control.Invalidate();
        }
    }

    private static void OnForegroundPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control)
        {
            control.OnForeColorChanged(EventArgs.Empty);
            control.Invalidate();
        }
    }

    private static void OnFontPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control)
        {
            return;
        }

        if (e.NewValue is Font font)
        {
            control._nativeFontFallback ??= control.NativeFont;
            control.NativeFont = font;
            control.Invalidate();
            return;
        }

        if (control._nativeFontFallback is { } fallback)
        {
            control.NativeFont = fallback;
            control._nativeFontFallback = null;
            control.Invalidate();
        }
    }

    private Font NativeFont
    {
        get => base.Font;
        set
        {
            if (!Equals(base.Font, value))
            {
                base.Font = value;
            }
        }
    }

    private static void OnWidthPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control)
        {
            return;
        }

        if (e.NewValue is int width && width >= 0)
        {
            control._nativeWidthFallback ??= control.NativeWidth;
            control.NativeWidth = width;
        }
        else if (control._nativeWidthFallback is { } fallback)
        {
            control.NativeWidth = fallback;
            control._nativeWidthFallback = null;
        }
    }

    private static void OnHeightPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control)
        {
            return;
        }

        if (e.NewValue is int height && height >= 0)
        {
            control._nativeHeightFallback ??= control.NativeHeight;
            control.NativeHeight = height;
        }
        else if (control._nativeHeightFallback is { } fallback)
        {
            control.NativeHeight = fallback;
            control._nativeHeightFallback = null;
        }
    }

    private int NativeWidth
    {
        get => base.Width;
        set
        {
            if (base.Width != value)
            {
                base.Width = value;
            }
        }
    }

    private int NativeHeight
    {
        get => base.Height;
        set
        {
            if (base.Height != value)
            {
                base.Height = value;
            }
        }
    }

    private static void OnSizeConstraintChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control)
        {
            return;
        }

        var minimum = new Size(Math.Max(0, control.MinWidth), Math.Max(0, control.MinHeight));
        if (control.MinimumSize != minimum)
        {
            control.MinimumSize = minimum;
        }

        var maximum = new Size(
            control.MaxWidth == int.MaxValue ? 0 : Math.Max(0, control.MaxWidth),
            control.MaxHeight == int.MaxValue ? 0 : Math.Max(0, control.MaxHeight));
        if (control.MaximumSize != maximum)
        {
            control.MaximumSize = maximum;
        }

        control.Parent?.PerformLayout();
    }

    private static void OnVisibilityPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control || e.NewValue is not Visibility visibility)
        {
            return;
        }

        var visible = visibility == Visibility.Visible;
        if (control.Visible != visible)
        {
            control.Visible = visible;
        }
    }

    private static void OnIsEnabledPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control || e.NewValue is not bool enabled)
        {
            return;
        }

        if (control.Enabled != enabled)
        {
            control.Enabled = enabled;
        }
    }

    private static void OnTabIndexPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && e.NewValue is int index)
        {
            control.NativeTabIndex = index;
        }
    }

    private static void OnIsTabStopPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && e.NewValue is bool tabStop)
        {
            control.NativeTabStop = tabStop;
        }
    }

    // DP 是作者面，原生属性是布局面；每次变化都把后者写一遍，WinForms 的停靠/滚动/边距才和 XAML 看到的一致。
    private Padding NativeMargin
    {
        get => base.Margin;
        set
        {
            if (base.Margin != value)
            {
                base.Margin = value;
            }
        }
    }

    private Padding NativePadding
    {
        get => base.Padding;
        set
        {
            if (base.Padding != value)
            {
                base.Padding = value;
            }
        }
    }

    // TabIndex / TabStop 是原生属性；用隐藏属性把 DP 的写入同步过去，WinForms 的 Tab 导航才能看到。
    private int NativeTabIndex
    {
        get => base.TabIndex;
        set
        {
            if (base.TabIndex != value)
            {
                base.TabIndex = value;
            }
        }
    }

    private bool NativeTabStop
    {
        get => base.TabStop;
        set
        {
            if (base.TabStop != value)
            {
                base.TabStop = value;
            }
        }
    }

    /// <summary>
    /// The inherited data context used by bindings. Assigning it makes this control's bindings, triggers and native
    /// bindings re-resolve; descendants without a local value follow.
    /// </summary>
    /// <remarks>
    /// This is the XAML tree's own context: it participates in inheritance and dependency-property change
    /// notification. Newer WinForms bases may also expose a native <c>DataContext</c>; this property is the one the
    /// XAML runtime, bindings and triggers use.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
#if NET8_0_OR_GREATER
    public new object? DataContext
#else
    public object? DataContext
#endif
    {
        get => DataContextProperty.GetValue(this);
        set => DataContextProperty.SetValue(this, value);
    }

    /// <summary>Raised when this control's effective data context changes.</summary>
#if NET8_0_OR_GREATER
    public new event EventHandler? DataContextChanged;
#else
    public event EventHandler? DataContextChanged;
#endif

    /// <summary>Raised when a pointer button is pressed on this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerPressedEventArgs>? PointerPressed;

    /// <summary>Raised when the pointer moves over this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerMovedEventArgs>? PointerMoved;

    /// <summary>Raised when a pointer button is released on this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerReleasedEventArgs>? PointerReleased;

    /// <summary>Raised when the pointer enters this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerEnteredEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerExitedEventArgs>? PointerExited;

    /// <summary>Raised when the pointer wheel turns over this control or bubbles from a descendant.</summary>
    public event EventHandler<PointerWheelEventArgs>? PointerWheelChanged;

    /// <summary>Raised when a key goes down through the explicit keyboard input abstraction.</summary>
    /// <remarks>This is separate from the native <see cref="System.Windows.Forms.Control.KeyDown"/> event, which XAML binds normally.</remarks>
    public event EventHandler<KeyInputEventArgs>? KeyInputDown;

    /// <summary>Raised when a key goes up through the explicit keyboard input abstraction.</summary>
    /// <remarks>This is separate from the native <see cref="System.Windows.Forms.Control.KeyUp"/> event, which XAML binds normally.</remarks>
    public event EventHandler<KeyInputEventArgs>? KeyInputUp;

    /// <summary>Raised after any dependency property on this control changes.</summary>
    internal event EventHandler<DependencyPropertyChangedEventArgs>? DependencyPropertyChanged;

    /// <summary>The triggers owned by this control.</summary>
    public TriggerCollection Triggers => _triggers ??= new TriggerCollection(this);

    /// <summary>The key and mouse gestures this control turns into commands.</summary>
    /// <remarks>
    /// <see cref="OnKeyDown"/> and <see cref="OnMouseDown"/> check this collection before routing the native input, so
    /// a matching binding runs its command and marks the key event handled. The collection is never <see langword="null"/>.
    /// </remarks>
    public InputBindingCollection InputBindings => _inputBindings;

    /// <summary>The resources owned by this control, addressable by key from <c>StaticResource</c>.</summary>
    public ResourceDictionary Resources => _resources;

    /// <summary>The names declared in this XAML view or template instance.</summary>
    /// <remarks>
    /// Generated code creates one scope on the view root and one on each template root; a lookup walks up to the
    /// nearest scope, so template names never leak into the outer view.
    /// </remarks>
    public NameScope? NameScope { get; set; }

    /// <summary>The control whose <see cref="Style.Template"/> created this control, or <see langword="null"/>.</summary>
    public Control? TemplatedParent { get; private set; }

    /// <summary>The declarative context menu shown on a right click.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
#pragma warning disable CS0109 // 旧 TFM 的 System.Windows.Forms.Control 还有同名废弃属性；net5+ 没有，故只有一边报。
    public new ContextMenu? ContextMenu
#pragma warning restore CS0109
    {
        get => base.ContextMenuStrip as ContextMenu;
        set => base.ContextMenuStrip = value;
    }

    /// <summary>The declarative tooltip shown on hover; a string or a <see cref="ToolTip"/> object.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? ToolTip
    {
        get => ToolTipService.GetToolTip(this);
        set => ToolTipService.SetToolTip(this, value);
    }

    /// <summary>
    /// The style currently applied to this control, or <see langword="null"/> when none is applied.
    /// </summary>
    /// <remarks>
    /// Assigning a style makes it explicit and applies its setters and triggers; assigning <see langword="null"/>
    /// removes the explicit style and lets the nearest ancestor implicit style apply. Explicit styles win over
    /// implicit styles. Style values sit below local values and below style triggers, and above defaults.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Style? Style
    {
        get => _appliedStyle;
        set => SetExplicitStyle(value);
    }

    /// <summary>The style assigned explicitly, or <see langword="null"/> when the applied style is implicit.</summary>
    internal Style? ExplicitStyle => _explicitStyle;

    /// <summary>
    /// Applies the style stored under <paramref name="key"/> in <paramref name="scope"/> and keeps following that
    /// resource: replacing it reapplies the new style, removing it clears the explicit style.
    /// </summary>
    /// <param name="scope">The control whose resources contain the style key.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public void ApplyStyleResource(Control scope, object key)
    {
        if (scope is null) throw new ArgumentNullException(nameof(scope));
        if (key is null) throw new ArgumentNullException(nameof(key));

        if (!ReferenceEquals(_styleResourceScope, scope) || !Equals(_styleResourceKey, key))
        {
            DetachStyleResource();
            _styleResourceScope = scope;
            _styleResourceKey = key;
            scope.Resources.Changed += OnStyleResourceChanged;
        }

        ApplyTrackedStyleResource();
    }

    private void OnStyleResourceChanged(object? changedKey)
    {
        if (changedKey is null || Equals(changedKey, _styleResourceKey))
        {
            ApplyTrackedStyleResource();
        }
    }

    private void ApplyTrackedStyleResource()
    {
        if (_styleResourceScope is null || _styleResourceKey is null)
        {
            return;
        }

        Style? style = null;
        if (_styleResourceScope.Resources.TryGetValue(_styleResourceKey, out var value) && value is Style found)
        {
            style = found;
        }

        SetExplicitStyle(style, fromResource: true);
    }

    private void DetachStyleResource()
    {
        if (_styleResourceScope is not null)
        {
            _styleResourceScope.Resources.Changed -= OnStyleResourceChanged;
        }

        _styleResourceScope = null;
        _styleResourceKey = null;
    }

    /// <summary>Reads a dependency property from this control.</summary>
    /// <param name="property">The property to read.</param>
    public object? GetValue(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }

    /// <summary>Writes a dependency property on this control.</summary>
    /// <param name="property">The property to write.</param>
    /// <param name="value">The new value.</param>
    public void SetValue(DependencyProperty property, object? value)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        property.SetValue(this, value);
    }

    /// <summary>Clears the local value of a dependency property, restoring its default or inherited value.</summary>
    /// <param name="property">The property to clear.</param>
    public void ClearValue(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        property.ClearValue(this);
    }

    /// <summary>True when this control holds a local value for <paramref name="property"/>.</summary>
    /// <param name="property">The property to inspect.</param>
    public bool HasLocalValue(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return _values.ContainsKey(property);
    }

    // 值可能来自本地、样式或样式触发器；Margin/Padding 的 getter 要区分「没人写过」和「写了默认值」。
    internal bool HasXamlValue(DependencyProperty property)
        => _values.ContainsKey(property)
        || _styleValues.ContainsKey(property)
        || (_styleTriggerValues.TryGetValue(property, out var layers) && layers.Count > 0);

    /// <summary>
    /// Binds a dependency property to a path on the effective data context.
    /// </summary>
    /// <param name="property">The target dependency property.</param>
    /// <param name="binding">The binding description.</param>
    /// <remarks>The binding attaches immediately when a data context exists, otherwise on the next context change.</remarks>
    public void SetBinding(DependencyProperty property, Binding binding)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (binding is null) throw new ArgumentNullException(nameof(binding));

        ClearBinding(property);
        var expression = new BindingExpression(this, property, binding);
        _bindings[property] = expression;
        expression.Attach();
    }

    /// <summary>Writes the current target value back to the source for a two-way binding.</summary>
    /// <param name="property">The bound dependency property.</param>
    /// <returns><see langword="true"/> when a binding existed and its source was available.</returns>
    /// <remarks>This is the manual path for <see cref="UpdateSourceTrigger.Explicit"/>.</remarks>
    public bool UpdateSource(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (!_bindings.TryGetValue(property, out var expression))
        {
            return false;
        }

        expression.UpdateSource();
        return true;
    }

    /// <summary>Writes a native bound property back to its source.</summary>
    /// <param name="propertyName">The native property name used by <see cref="SetNativeBinding"/>.</param>
    /// <returns><see langword="true"/> when a native binding was found.</returns>
    /// <remarks>This is the manual path for <see cref="UpdateSourceTrigger.Explicit"/> on a native binding.</remarks>
    public bool UpdateSource(string propertyName)
    {
        if (propertyName is null) throw new ArgumentNullException(nameof(propertyName));
        var entry = _nativeBindings.Find(item => string.Equals(item.PropertyName, propertyName, StringComparison.Ordinal));
        if (entry?.Applied is not { } binding)
        {
            return false;
        }

        binding.WriteValue();
        return true;
    }

    /// <summary>Finds the control registered under <paramref name="name"/> in the nearest name scope.</summary>
    /// <param name="name">The name written with <c>x:Name</c>.</param>
    public System.Windows.Forms.Control? FindName(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return NameScope.Find(this)?.Find(name);
    }

    /// <summary>Finds a named part in the applied <see cref="Style.Template"/>.</summary>
    /// <typeparam name="T">The part's control type.</typeparam>
    /// <param name="name">The part name.</param>
    /// <returns>The part, or <see langword="null"/> when the template or name is missing.</returns>
    public T? GetTemplateChild<T>(string name) where T : System.Windows.Forms.Control
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (_templateContent is Control content && content.NameScope?.Find(name) is T part)
        {
            return part;
        }

        return null;
    }

    /// <summary>Removes the binding, if any, from <paramref name="property"/>.</summary>
    /// <param name="property">The property whose binding is removed.</param>
    public void ClearBinding(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (_bindings.TryGetValue(property, out var existing))
        {
            existing.Dispose();
            _bindings.Remove(property);
        }
    }

    /// <summary>
    /// Links a native WinForms property to a path on the effective data context.
    /// </summary>
    /// <param name="target">The native control whose property is bound.</param>
    /// <param name="propertyName">The native property name, for example <c>Text</c>.</param>
    /// <param name="binding">The binding description.</param>
    /// <remarks>
    /// The link is created through <see cref="System.Windows.Forms.Control.DataBindings"/> when a data context exists. Until then it is
    /// kept pending, which lets generated code attach bindings before the constructor's caller assigns the context.
    /// </remarks>
    public void SetNativeBinding(System.Windows.Forms.Control target, string propertyName, Binding binding)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("A binding needs a property name.", nameof(propertyName));
        if (binding is null) throw new ArgumentNullException(nameof(binding));

        var entry = new NativeBindingEntry(target, propertyName, binding);
        _nativeBindings.Add(entry);
        ApplyNativeBinding(entry);
    }

    /// <summary>
    /// Binds an attached dependency property on any control to a path on this control's effective data context.
    /// </summary>
    /// <param name="target">The control receiving the value; it does not need to derive from <see cref="Control"/>.</param>
    /// <param name="property">The attached dependency property to drive.</param>
    /// <param name="binding">The binding description.</param>
    /// <remarks>
    /// <para>
    /// <see cref="BindingMode.OneWay"/> and <see cref="BindingMode.OneTime"/> are supported. A
    /// <see cref="BindingMode.TwoWay"/> binding behaves as <see cref="BindingMode.OneWay"/> for attached values on
    /// native controls: a native write has no change pipeline that could push the value back to the source.
    /// </para>
    /// <para>The binding attaches immediately when a data context exists, otherwise on the next context change.</para>
    /// </remarks>
    public void SetAttachedBinding(System.Windows.Forms.Control target, DependencyProperty property, Binding binding)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (binding is null) throw new ArgumentNullException(nameof(binding));

        for (var index = _attachedBindings.Count - 1; index >= 0; index--)
        {
            var existing = _attachedBindings[index];
            if (ReferenceEquals(existing.Target, target) && ReferenceEquals(existing.Property, property))
            {
                existing.Dispose();
                _attachedBindings.RemoveAt(index);
            }
        }

        var expression = new BindingExpression(this, target, property, binding);
        _attachedBindings.Add(expression);
        expression.Attach();
    }

    /// <summary>Re-resolves every dependency-property binding against the current data context.</summary>
    public void RefreshBindings()
    {
        foreach (var expression in _bindings.Values)
        {
            expression.Refresh();
        }

        foreach (var expression in _attachedBindings)
        {
            expression.Refresh();
        }

        foreach (var expression in _styleSetterBindings.Values)
        {
            expression.Refresh();
        }

        foreach (var state in _triggerStates.Values)
        {
            foreach (var expression in state.Bindings.Values)
            {
                expression.Refresh();
            }
        }
    }

    /// <summary>Re-evaluates every trigger against the current values.</summary>
    public void RefreshTriggers()
    {
        SyncDirectTriggerSubscriptions();
        EvaluateTriggers();
    }

    /// <summary>Searches this control and its ancestors for a resource.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The resource value, or <see langword="null"/> when no control in the chain has it.</returns>
    public object? FindResource(object key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        for (System.Windows.Forms.Control? current = this; current is not null; current = current.Parent)
        {
            if (current is Control xaml && xaml._resources.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    internal void OnDependencyPropertyChangedCore(
        DependencyProperty property,
        object? oldValue,
        object? newValue,
        XamlValueSource source)
    {
        if (_disposed)
        {
            return;
        }

        if (ReferenceEquals(property, DataContextProperty))
        {
            OnDataContextChangedCore();
            return;
        }

        if (_bindings.TryGetValue(property, out var expression))
        {
            expression.OnTargetValueChanged(oldValue, newValue, source);
        }

        // 过渡帧是视觉状态而非作者状态：每帧都评估触发器会让已经开始的动画再启动一份工作。
        if (source != XamlValueSource.Transition)
        {
            EvaluateTriggers();
        }

        OnXamlPropertyChanged(property, oldValue, newValue);

        DependencyPropertyChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(property, oldValue, newValue));
    }

    /// <summary>Called after any dependency property on this control changes.</summary>
    /// <param name="property">The property that changed.</param>
    /// <param name="oldValue">The previous value.</param>
    /// <param name="newValue">The new value.</param>
    protected virtual void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
    }

    /// <summary>Called when this control's effective data context changes.</summary>
    protected virtual void OnDataContextChanged()
    {
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var background = Background;

        // 没有作者设置背景时保持透明：WPF/Avalonia 的默认背景是透明，尤其不能让自绘文本/图形
        // 在暗色表面上盖出一块系统灰。
        if (background is null)
        {
            PaintParentBackground(e);
            return;
        }

        // 半透明背景要先画父容器的底色：WinForms 的子窗口 DC 不含父窗口像素，直接填半透明色
        // 会与上一次窗口表面的残留内容混合（缩放放大后就是花屏）。渐变可能带透明区，同样先垫底。
        if (background is SolidColorBrush solid)
        {
            if (solid.Color.A == 0)
            {
                PaintParentBackground(e);
                return;
            }

            if (solid.Color.A < 255)
            {
                PaintParentBackground(e);
            }
        }
        else
        {
            PaintParentBackground(e);
        }

        background.Paint(e.Graphics, ClientRectangle);
    }

    // WinForms 没有真正的透明子窗口；把父控件的背景/自绘内容按父坐标画进自己的 DC。
    // 与 Control.OnPaintBackground 对 α < 255 的 BackColor 所做的是同一条伪透明路径。
    protected void PaintParentBackground(PaintEventArgs e)
    {
        if (Parent is not { } parent || Width <= 0 || Height <= 0)
        {
            return;
        }

        var state = e.Graphics.Save();
        try
        {
            e.Graphics.TranslateTransform(-Left, -Top);
            var args = new PaintEventArgs(e.Graphics, ClientRectangle);
            InvokePaintBackground(parent, args);
            InvokePaint(parent, args);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        // 输入绑定先于指针事件执行；MouseEventArgs 没有 Handled，指针通路仍按原样继续。
        _inputBindings.TryHandleMouse(e.Button, e.Clicks, System.Windows.Forms.Control.ModifierKeys);
        base.OnMouseDown(e);
        XamlInput.RaisePointerPressed(XamlInput.CreatePointerPressedArgs(this, e));
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        XamlInput.RaisePointerMoved(XamlInput.CreatePointerMovedArgs(this, e));
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        XamlInput.RaisePointerReleased(XamlInput.CreatePointerReleasedArgs(this, e));

        // 按下时取得的指针捕获在释放时必须归还；否则 WinForms 的全局鼠标捕获会留在本控件上。
        if (_pointerCaptured)
        {
            ReleasePointerCapture();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        UpdatePointerOver(Cursor.Position, forceFirst: true);
        XamlInput.RaisePointerEntered(XamlInput.CreatePointerEnteredArgs(this));
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdatePointerOver(Cursor.Position);
        XamlInput.RaisePointerExited(XamlInput.CreatePointerExitedArgs(this));
    }

    // WinForms 的悬停是逐子窗口的：指针进入子控件时父窗口会收到 MouseLeave。按屏幕矩形重算自己与整条
    // XAML 祖先链，子控件上悬停才会被祖先视为 IsPointerOver（WPF 的 IsMouseOver 语义）。
    private void UpdatePointerOver(Point screenPoint) => UpdatePointerOver(screenPoint, forceFirst: false);

    private void UpdatePointerOver(Point screenPoint, bool forceFirst)
    {
        var first = true;
        for (Control? current = this; current is not null; current = current.Parent as Control)
        {
            // MouseEnter 本身已经证明指针进了本控件；没有句柄的离屏/测试场景也要保留这一点。
            var inside = first && forceFirst || current.ContainsScreenPoint(screenPoint);
            current.SetValue(IsPointerOverProperty, inside);
            first = false;
        }
    }

    private bool ContainsScreenPoint(Point screenPoint)
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            return false;
        }

        try
        {
            return RectangleToScreen(ClientRectangle).Contains(screenPoint);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        XamlInput.RaisePointerWheelChanged(XamlInput.CreatePointerWheelArgs(this, e));
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 命中的输入绑定消费这次按键；键码仍是 KeyCode，修饰键单独比较。
        if (_inputBindings.TryHandleKey(e.KeyCode, e.Modifiers))
        {
            e.Handled = true;
        }

        base.OnKeyDown(e);
        var isRepeat = !_pressedKeys.Add(e.KeyCode);
        XamlInput.RaiseKeyInputDown(XamlInput.CreateKeyInputDownArgs(this, e, isRepeat));
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _pressedKeys.Remove(e.KeyCode);
        XamlInput.RaiseKeyInputUp(XamlInput.CreateKeyInputUpArgs(this, e));
    }

    /// <inheritdoc />
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        SetValue(IsKeyboardFocusedProperty, true);
        FocusManager.NotifyFocusEntered(this);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(EventArgs e)
    {
        _pressedKeys.Clear();
        SetValue(IsKeyboardFocusedProperty, false);
        _pointerCaptured = false;
        FocusManager.NotifyFocusLeft(this);
        base.OnLostFocus(e);
    }

    /// <inheritdoc />
    protected override void OnHandleDestroyed(EventArgs e)
    {
        _pressedKeys.Clear();
        SetValue(IsKeyboardFocusedProperty, false);
        SetValue(IsPointerOverProperty, false);
        _pointerCaptured = false;
        FocusManager.NotifyFocusLeft(this);
        base.OnHandleDestroyed(e);
    }

    internal void RaisePointerPressed(PointerPressedEventArgs args) => PointerPressed?.Invoke(this, args);

    internal void RaisePointerMoved(PointerMovedEventArgs args) => PointerMoved?.Invoke(this, args);

    internal void RaisePointerReleased(PointerReleasedEventArgs args) => PointerReleased?.Invoke(this, args);

    internal void RaisePointerEntered(PointerEnteredEventArgs args) => PointerEntered?.Invoke(this, args);

    internal void RaisePointerExited(PointerExitedEventArgs args) => PointerExited?.Invoke(this, args);

    internal void RaisePointerWheelChanged(PointerWheelEventArgs args) => PointerWheelChanged?.Invoke(this, args);

    internal void RaiseKeyInputDown(KeyInputEventArgs args) => KeyInputDown?.Invoke(this, args);

    internal void RaiseKeyInputUp(KeyInputEventArgs args) => KeyInputUp?.Invoke(this, args);

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);

        // 子控件的 Panel.ZIndex 通常在加入前就写好了；加入这一趟把顺序定下来。
        XamlLayout.ApplyZOrder(this);

        // 原生子控件不会走 XAML 的 MouseEnter/Leave；替它通知父链重算悬停状态。
        if (e.Control is not null and not Control)
        {
            HookNativeHover(e.Control);

            // 原生控件默认 Margin 3，而 XAML 控件现在是 0；不抹平的话混排面板里两者会差 3px。
            if (e.Control.Margin == new Padding(3))
            {
                e.Control.Margin = System.Windows.Forms.Padding.Empty;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            UnhookNativeHover(e.Control);
        }

        base.OnControlRemoved(e);
    }

    private void HookNativeHover(System.Windows.Forms.Control child)
    {
        if (_nativeHoverHandlers.ContainsKey(child))
        {
            return;
        }

        EventHandler handler = (_, _) => UpdatePointerOver(Cursor.Position);
        _nativeHoverHandlers[child] = handler;
        child.MouseEnter += handler;
        child.MouseLeave += handler;
    }

    private void UnhookNativeHover(System.Windows.Forms.Control child)
    {
        if (_nativeHoverHandlers.TryGetValue(child, out var handler))
        {
            _nativeHoverHandlers.Remove(child);
            child.MouseEnter -= handler;
            child.MouseLeave -= handler;
        }
    }

    /// <summary>Points this control and every XAML descendant at the control that owns the applied template.</summary>
    /// <param name="parent">The templated parent, or <see langword="null"/> while the template is removed.</param>
    internal void SetTemplatedParentRecursive(Control? parent)
    {
        TemplatedParent = parent;
        RefreshBindings();
        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (child is Control xaml)
            {
                xaml.SetTemplatedParentRecursive(parent);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        // 继承上下文沿 Parent 解析，所以挂进控件树正是绑定可以附着的时候；隐式样式也按树查找。
        RefreshImplicitStyle();
        NotifyTriggersDataContextChanged();
        RefreshBindings();
        EvaluateTriggers();
        ApplyNativeBindings();
        UpdateTemplateDataContext();

        // 子树先建好、再整体挂到某个祖先下时，只有根收到 ParentChanged。资源链与隐式样式对每个后代都要重走一遍，
        // 否则嵌套视图永远看不到祖先字典里的画刷，也不会套上祖先声明的隐式样式。
        NotifyAncestorChainChanged();
    }

    private void NotifyAncestorChainChanged()
    {
        if (_disposed)
        {
            return;
        }

        RefreshImplicitStyle();
        RefreshBindings();
        XamlRuntime.RefreshDynamicResources(this);
        OnAncestorChainChanged();

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (child is Control xaml)
            {
                xaml.NotifyAncestorChainChanged();
            }
            else
            {
                RefreshNativeDescendants(child);
            }
        }
    }

    /// <summary>Called when a control above this one joined or left the tree.</summary>
    /// <remarks>Derived controls that cache an ancestor — a scroll viewer, a host, a template root — re-resolve it here.</remarks>
    protected virtual void OnAncestorChainChanged()
    {
    }

    private static void RefreshNativeDescendants(System.Windows.Forms.Control parent)
    {
        XamlRuntime.RefreshDynamicResources(parent);

        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            if (child is Control xaml)
            {
                xaml.NotifyAncestorChainChanged();
            }
            else
            {
                RefreshNativeDescendants(child);
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            FocusManager.NotifyFocusLeft(this);

            foreach (var expression in _bindings.Values)
            {
                expression.Dispose();
            }

            foreach (var expression in _attachedBindings)
            {
                expression.Dispose();
            }

            foreach (var expression in _styleSetterBindings.Values)
            {
                expression.Dispose();
            }

            foreach (var state in _triggerStates.Values)
            {
                foreach (var expression in state.Bindings.Values)
                {
                    expression.Dispose();
                }
            }

            foreach (var state in _templateTriggerStates.Values)
            {
                foreach (var expression in state.Bindings.Values)
                {
                    expression.Dispose();
                }
            }

            _bindings.Clear();
            _attachedBindings.Clear();
            _styleSetterBindings.Clear();
            _triggerStates.Clear();
            _templateTriggerStates.Clear();
            RemoveNativeBindings();
            DetachNativeBindingScope();

            if (_triggers is not null)
            {
                foreach (var trigger in _triggers)
                {
                    trigger.Detach(this);
                }
            }

            foreach (var trigger in _styleTriggers)
            {
                trigger.Detach(this);
            }

            foreach (var trigger in _observedDirectTriggers)
            {
                trigger.Changed -= OnDirectTriggerChanged;
            }

            _observedDirectTriggers.Clear();

            foreach (var styleNode in _appliedStyleChain)
            {
                styleNode.UnregisterTarget(this);
            }

            _appliedStyleChain.Clear();
            RemoveTemplateContent();
            _styleValues.Clear();
            _styleTriggerValues.Clear();
            _styleTriggers.Clear();
            _styleTriggerOrders.Clear();
            _explicitStyle = null;
            _appliedStyle = null;
            DetachStyleResource();
            _resources.Changed -= OnResourcesChanged;
            XamlTransition.Stop(this);

            foreach (var pair in _nativeHoverHandlers)
            {
                if (!pair.Key.IsDisposed)
                {
                    pair.Key.MouseEnter -= pair.Value;
                    pair.Key.MouseLeave -= pair.Value;
                }
            }

            _nativeHoverHandlers.Clear();
        }

        base.Dispose(disposing);
    }

    internal object? GetLocalValueOrDefault(DependencyProperty property)
    {
        if (_values.TryGetValue(property, out var value))
        {
            return value;
        }

        if (_styleTriggerValues.TryGetValue(property, out var styleTriggerLayers) && styleTriggerLayers.Count > 0)
        {
            return styleTriggerLayers[styleTriggerLayers.Count - 1].Value;
        }

        if (_styleValues.TryGetValue(property, out var styleValue))
        {
            return styleValue;
        }

        if (ReferenceEquals(property, DataContextProperty))
        {
            return GetInheritedDataContext();
        }

        return property.DefaultValue;
    }

    internal void SetLocalValue(DependencyProperty property, object? value) => _values[property] = value;

    internal void RemoveLocalValue(DependencyProperty property) => _values.Remove(property);

    internal void SetValueFromTransition(DependencyProperty property, object? value)
        => property.SetValueCore(this, value, XamlValueSource.Transition);

    private void SetExplicitStyle(Style? style, bool fromResource = false)
    {
        if (!fromResource)
        {
            DetachStyleResource();
        }

        if (ReferenceEquals(_explicitStyle, style))
        {
            return;
        }

        _explicitStyle = style;
        ReapplyEffectiveStyle();
    }

    internal void OnStyleMutated()
    {
        if (_disposed)
        {
            return;
        }

        ReapplyEffectiveStyle(force: true);
    }

    internal void RefreshImplicitStyle()
    {
        if (_explicitStyle is not null)
        {
            return;
        }

        ReapplyEffectiveStyle();
    }

    private void ReapplyEffectiveStyle(bool force = false)
    {
        if (_disposed)
        {
            return;
        }

        var next = _explicitStyle ?? FindImplicitStyle();
        if (!force && ReferenceEquals(next, _appliedStyle))
        {
            return;
        }

        if (next is not null)
        {
            ValidateStyle(next);
        }

        UnapplyStyleCore();
        _appliedStyle = next;
        if (next is not null)
        {
            ApplyStyleCore(next);
        }
    }

    private Style? FindImplicitStyle()
    {
        var type = GetType();
        for (System.Windows.Forms.Control? current = this; current is not null; current = current.Parent)
        {
            if (current is Control xaml
                && xaml._resources.TryGetValue(type, out var value)
                && value is Style style
                && style.TargetType == type)
            {
                return style;
            }
        }

        return ThemeStyles.Find(type);
    }

    private void OnResourcesChanged(object? key)
    {
        if (_disposed)
        {
            return;
        }

        if (key is Type type)
        {
            RefreshImplicitStyleForType(this, type);
        }
        else if (key is null)
        {
            RefreshImplicitStylesInSubtree(this);
        }
    }

    private static void RefreshImplicitStyleForType(System.Windows.Forms.Control parent, Type type)
    {
        if (parent is Control xaml && xaml.GetType() == type)
        {
            xaml.RefreshImplicitStyle();
        }

        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            RefreshImplicitStyleForType(child, type);
        }
    }

    private static void RefreshImplicitStylesInSubtree(System.Windows.Forms.Control parent)
    {
        if (parent is Control xaml)
        {
            xaml.RefreshImplicitStyle();
        }

        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            RefreshImplicitStylesInSubtree(child);
        }
    }

    private void ValidateStyle(Style style)
    {
        foreach (var current in ResolveStyleChain(style))
        {
            if (current.TargetType is null)
            {
                throw new InvalidOperationException("Style.TargetType is required.");
            }

            if (!current.TargetType.IsInstanceOfType(this))
            {
                throw new InvalidOperationException(
                    $"Style TargetType '{current.TargetType}' does not apply to '{GetType()}'.");
            }
        }
    }

    private static List<Style> ResolveStyleChain(Style style)
    {
        var chain = new List<Style>();
        var seen = new HashSet<Style>();

        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (!seen.Add(current))
            {
                throw new InvalidOperationException("A Style BasedOn chain contains a cycle.");
            }

            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }

    private void ApplyStyleCore(Style style)
    {
        var chain = ResolveStyleChain(style);

        _appliedStyleChain.Clear();
        _appliedStyleChain.AddRange(chain);
        foreach (var styleNode in chain)
        {
            styleNode.RegisterTarget(this);
        }

        var effectiveSetters = new Dictionary<DependencyProperty, Setter>();
        foreach (var current in chain)
        {
            foreach (var setter in current.Setters)
            {
                if (setter?.Property is { } property)
                {
                    // 派生样式后写，覆盖基础样式的同属性 setter。
                    effectiveSetters[property] = setter;
                }
            }
        }

        foreach (var pair in effectiveSetters)
        {
            ApplyStyleSetter(pair.Key, pair.Value);
        }

        _styleTriggers.Clear();
        _styleTriggerOrders.Clear();

        var seenTriggers = new HashSet<Trigger>();
        var order = 0;
        ControlTemplate? template = null;
        foreach (var current in chain)
        {
            foreach (var trigger in current.Triggers)
            {
                if (trigger is not null && seenTriggers.Add(trigger))
                {
                    _styleTriggers.Add(trigger);
                    _styleTriggerOrders[trigger] = order++;
                }
            }

            if (current.Template is not null)
            {
                template = current.Template;
            }
        }

        if (template is not null)
        {
            if (template.TargetType is not null && !template.TargetType.IsInstanceOfType(this))
            {
                throw new InvalidOperationException(
                    $"ControlTemplate TargetType '{template.TargetType}' does not apply to '{GetType()}'.");
            }

            _templateTemplate = template;
            template.Changed += OnTemplateMutated;
            _templateContent = template.CreateContent(this);

            // 模板根就是控件的外观：普通 Control 不会给子控件排版，靠原生 Dock 把它撑满。
            _templateContent.Dock = System.Windows.Forms.DockStyle.Fill;
            Controls.Add(_templateContent);
            if (_templateContent is Control templated)
            {
                templated.SetTemplatedParentRecursive(this);
            }

            UpdateTemplateDataContext();
        }

        EvaluateTriggers();
    }

    private void UnapplyStyleCore()
    {
        foreach (var styleNode in _appliedStyleChain)
        {
            styleNode.UnregisterTarget(this);
        }

        _appliedStyleChain.Clear();

        if (_templateTemplate is not null)
        {
            _templateTemplate.Changed -= OnTemplateMutated;
            foreach (var pair in _templateTriggerStates)
            {
                if (pair.Value.Active)
                {
                    ApplyTemplateExit(pair.Key, pair.Value);
                }

                pair.Key.Detach(this);
            }

            _templateTriggerStates.Clear();
            _templateTemplate = null;
        }

        RemoveTemplateContent();

        // 先摘掉样式触发器，再退出它们：退出动作改属性时不会再评估已经离开的样式触发器。
        var triggers = _styleTriggers.ToArray();
        _styleTriggers.Clear();
        _styleTriggerOrders.Clear();

        foreach (var trigger in triggers)
        {
            if (_triggerStates.TryGetValue(trigger, out var state))
            {
                if (state.Active)
                {
                    state.Active = false;
                    ApplyStyleExit(trigger, state);
                }

                _triggerStates.Remove(trigger);
            }

            trigger.Detach(this);
        }

        foreach (var expression in _styleSetterBindings.Values)
        {
            expression.Dispose();
        }

        _styleSetterBindings.Clear();

        foreach (var property in _styleValues.Keys.ToArray())
        {
            var oldEffective = GetLocalValueOrDefault(property);
            _styleValues.Remove(property);
            var newEffective = GetLocalValueOrDefault(property);
            property.RaiseChangedIfDifferent(this, oldEffective, newEffective, XamlValueSource.Style);
        }

        _styleTriggerValues.Clear();
    }

    private void RemoveTemplateContent()
    {
        var content = _templateContent;
        if (content is null)
        {
            return;
        }

        _templateContent = null;
        if (content is Control templated)
        {
            templated.SetTemplatedParentRecursive(null);
        }

        try
        {
            Controls.Remove(content);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[VeloxDev.Xaml] Could not remove template content: {exception.Message}");
        }

        content.Dispose();
    }

    private void UpdateTemplateDataContext()
    {
        if (_templateContent is null)
        {
            return;
        }

        var context = DataContext;
        if (_templateContent is Control xaml)
        {
            xaml.DataContext = context;
        }
        else
        {
            _templateContent.BindingContext = BindingContext;
#if NET8_0_OR_GREATER
            _templateContent.DataContext = context;
#endif
        }
    }

    private void ApplyStyleSetter(DependencyProperty property, Setter setter)
    {
        var binding = setter.Binding ?? setter.Value as Binding;
        if (binding is null)
        {
            SetStyleValue(property, setter.Value);
            return;
        }

        if (_styleSetterBindings.TryGetValue(property, out var existing))
        {
            existing.Dispose();
            _styleSetterBindings.Remove(property);
        }

        var expression = new BindingExpression(
            this,
            binding,
            new StyleSetterBindingSink(this, property));
        _styleSetterBindings[property] = expression;
        expression.Attach();
    }

    private void SetStyleValue(DependencyProperty property, object? value)
    {
        var converted = property.CoerceValue(this, value);
        var oldEffective = GetLocalValueOrDefault(property);
        _styleValues[property] = converted;
        var newEffective = GetLocalValueOrDefault(property);
        property.RaiseChangedIfDifferent(this, oldEffective, newEffective, XamlValueSource.Style);
    }

    private void AddStyleTriggerValue(Trigger trigger, DependencyProperty property, object? value)
    {
        var converted = property.CoerceValue(this, value);
        var oldEffective = GetLocalValueOrDefault(property);

        if (!_styleTriggerValues.TryGetValue(property, out var layers))
        {
            layers = [];
            _styleTriggerValues[property] = layers;
        }

        layers.RemoveAll(entry => ReferenceEquals(entry.Trigger, trigger));
        var order = _styleTriggerOrders.TryGetValue(trigger, out var knownOrder) ? knownOrder : int.MaxValue;
        layers.Add(new StyleTriggerValue(trigger, order, converted));
        layers.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        var newEffective = GetLocalValueOrDefault(property);
        property.RaiseChangedIfDifferent(this, oldEffective, newEffective, XamlValueSource.Style);
    }

    private void RemoveStyleTriggerValues(Trigger trigger)
    {
        foreach (var property in _styleTriggerValues.Keys.ToArray())
        {
            var layers = _styleTriggerValues[property];
            if (!layers.Exists(entry => ReferenceEquals(entry.Trigger, trigger)))
            {
                continue;
            }

            var oldEffective = GetLocalValueOrDefault(property);
            layers.RemoveAll(entry => ReferenceEquals(entry.Trigger, trigger));
            if (layers.Count == 0)
            {
                _styleTriggerValues.Remove(property);
            }

            var newEffective = GetLocalValueOrDefault(property);
            property.RaiseChangedIfDifferent(this, oldEffective, newEffective, XamlValueSource.Style);
        }
    }

    private object? GetInheritedDataContext()
    {
        for (System.Windows.Forms.Control? parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Control xaml)
            {
                return xaml.GetLocalValueOrDefault(DataContextProperty);
            }
        }

        return null;
    }

    private void OnDataContextChangedCore()
    {
        DataContextChanged?.Invoke(this, EventArgs.Empty);
        NotifyTriggersDataContextChanged();
        RefreshBindings();
        UpdateTemplateDataContext();
        EvaluateTriggers();
        ApplyNativeBindings();
        OnDataContextChanged();
        PropagateDataContext(this);
    }

    private static void PropagateDataContext(System.Windows.Forms.Control parent)
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            if (child is Control xaml)
            {
                if (xaml.HasLocalValue(DataContextProperty))
                {
                    // 局部上下文会截断整棵子树的继承。
                    continue;
                }

                xaml.OnAncestorDataContextChanged();
            }

            PropagateDataContext(child);
        }
    }

    private void OnAncestorDataContextChanged()
    {
        if (_disposed || HasLocalValue(DataContextProperty))
        {
            return;
        }

        DataContextChanged?.Invoke(this, EventArgs.Empty);
        NotifyTriggersDataContextChanged();
        RefreshBindings();
        UpdateTemplateDataContext();
        EvaluateTriggers();
        ApplyNativeBindings();
        OnDataContextChanged();
    }

    private void NotifyTriggersDataContextChanged()
    {
        if (_triggers is not null)
        {
            foreach (var trigger in _triggers)
            {
                trigger.OnDataContextChanged(this);
            }
        }

        foreach (var trigger in _styleTriggers)
        {
            trigger.OnDataContextChanged(this);
        }
    }

    private void EvaluateTriggers()
    {
        if (_evaluatingTriggers || _disposed)
        {
            return;
        }

        _evaluatingTriggers = true;
        try
        {
            if (_triggers is not null)
            {
                foreach (var trigger in _triggers)
                {
                    EvaluateTrigger(trigger, isStyle: false);
                }
            }

            EvaluateTemplateTriggers();

            foreach (var trigger in _styleTriggers)
            {
                EvaluateTrigger(trigger, isStyle: true);
            }
        }
        finally
        {
            _evaluatingTriggers = false;
        }
    }

    private void EvaluateTemplateTriggers()
    {
        if (_templateTemplate is null)
        {
            return;
        }

        foreach (var trigger in _templateTemplate.Triggers)
        {
            if (trigger is null)
            {
                continue;
            }

            trigger.Attach(this);
            var active = trigger.Matches(this);
            var wasActive = _templateTriggerStates.TryGetValue(trigger, out var state) && state.Active;
            if (active == wasActive)
            {
                continue;
            }

            if (state is null)
            {
                state = new TriggerRuntimeState();
                _templateTriggerStates.Add(trigger, state);
            }

            state.Active = active;
            if (active)
            {
                ApplyTemplateEnter(trigger, state);
            }
            else
            {
                ApplyTemplateExit(trigger, state);
            }
        }
    }

    private void ApplyTemplateEnter(Trigger trigger, TriggerRuntimeState state)
    {
        state.Restore.Clear();
        foreach (var setter in trigger.Setters)
        {
            if (setter is null || setter.Binding is not null || setter.Value is Binding)
            {
                continue;
            }

            var target = ResolveTemplateTarget(setter.TargetName);
            if (target is null)
            {
                continue;
            }

            if (setter.Property is { } property && target is Control xaml)
            {
                var hadLocal = xaml.HasLocalValue(property);
                var previous = property.GetValue(xaml);
                property.SetValueCore(xaml, setter.Value, XamlValueSource.Trigger);
                state.Restore.Add(() =>
                {
                    if (hadLocal)
                    {
                        property.SetValueCore(xaml, previous, XamlValueSource.Trigger);
                    }
                    else
                    {
                        property.ClearValueCore(xaml, XamlValueSource.Trigger);
                    }
                });
                continue;
            }

            if (!string.IsNullOrEmpty(setter.PropertyName))
            {
                var descriptor = TypeDescriptor.GetProperties(target).Find(setter.PropertyName!, ignoreCase: false);
                if (descriptor is null || descriptor.IsReadOnly)
                {
                    continue;
                }

                var previous = descriptor.GetValue(target);
                descriptor.SetValue(target, XamlRuntime.ConvertValue(setter.Value, descriptor.PropertyType));
                state.Restore.Add(() => descriptor.SetValue(target, previous));
            }
        }

        trigger.EnterAction?.Invoke(this);
    }

    private void ApplyTemplateExit(Trigger trigger, TriggerRuntimeState state)
    {
        trigger.ExitAction?.Invoke(this);

        for (var index = state.Restore.Count - 1; index >= 0; index--)
        {
            state.Restore[index]();
        }

        state.Restore.Clear();
    }

    private System.Windows.Forms.Control? ResolveTemplateTarget(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return this;
        }

        if (_templateContent is Control content && content.NameScope?.Find(name!) is { } part)
        {
            return part;
        }

        return null;
    }

    private void OnTemplateMutated(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            EvaluateTemplateTriggers();
        }
    }

    private void SyncDirectTriggerSubscriptions()
    {
        var current = _triggers is null ? new HashSet<Trigger>() : new HashSet<Trigger>(_triggers);

        foreach (var observed in _observedDirectTriggers.ToArray())
        {
            if (!current.Contains(observed))
            {
                observed.Changed -= OnDirectTriggerChanged;
                _observedDirectTriggers.Remove(observed);
            }
        }

        foreach (var trigger in current)
        {
            if (trigger is not null && _observedDirectTriggers.Add(trigger))
            {
                trigger.Changed += OnDirectTriggerChanged;
            }
        }
    }

    private void OnDirectTriggerChanged(object? sender, EventArgs e)
    {
        if (sender is Trigger trigger)
        {
            RefreshMutatedTrigger(trigger);
        }
    }

    private void RefreshMutatedTrigger(Trigger trigger)
    {
        if (_disposed || _evaluatingTriggers)
        {
            return;
        }

        if (_triggers is null || !_triggers.Contains(trigger))
        {
            return;
        }

        _evaluatingTriggers = true;
        try
        {
            if (_triggerStates.TryGetValue(trigger, out var state) && state.Active)
            {
                state.Active = false;
                ApplyExit(trigger, state);
            }

            EvaluateTrigger(trigger, isStyle: false);
        }
        finally
        {
            _evaluatingTriggers = false;
        }
    }

    private void EvaluateTrigger(Trigger? trigger, bool isStyle)
    {
        if (trigger is null)
        {
            return;
        }

        trigger.Attach(this);
        var active = trigger.Matches(this);
        var wasActive = _triggerStates.TryGetValue(trigger, out var state) && state.Active;
        if (active == wasActive)
        {
            return;
        }

        if (state is null)
        {
            state = new TriggerRuntimeState();
            _triggerStates.Add(trigger, state);
        }

        state.Active = active;
        if (isStyle)
        {
            if (active)
            {
                ApplyStyleEnter(trigger, state);
            }
            else
            {
                ApplyStyleExit(trigger, state);
            }
        }
        else if (active)
        {
            ApplyEnter(trigger, state);
        }
        else
        {
            ApplyExit(trigger, state);
        }
    }

    /// <summary>Re-evaluates one trigger after a condition source raised a change.</summary>
    /// <param name="trigger">The trigger to re-evaluate.</param>
    internal void RefreshTrigger(Trigger trigger)
    {
        if (_disposed || _evaluatingTriggers)
        {
            return;
        }

        var isStyle = _styleTriggers.Contains(trigger);
        if (!isStyle && (_triggers is null || !_triggers.Contains(trigger)))
        {
            return;
        }

        _evaluatingTriggers = true;
        try
        {
            EvaluateTrigger(trigger, isStyle);
        }
        finally
        {
            _evaluatingTriggers = false;
        }
    }

    private void ApplyStyleEnter(Trigger trigger, TriggerRuntimeState state)
    {
        foreach (var setter in trigger.Setters)
        {
            if (setter?.Property is not { } property)
            {
                continue;
            }

            if ((setter.Binding ?? setter.Value as Binding) is { } binding)
            {
                var expression = new BindingExpression(
                    this,
                    binding,
                    new StyleTriggerBindingSink(this, trigger, property));
                state.Bindings[setter] = expression;
                expression.Attach();
            }
            else
            {
                AddStyleTriggerValue(trigger, property, setter.Value);
            }
        }

        trigger.EnterAction?.Invoke(this);
    }

    private void ApplyStyleExit(Trigger trigger, TriggerRuntimeState state)
    {
        trigger.ExitAction?.Invoke(this);

        foreach (var expression in state.Bindings.Values)
        {
            expression.Dispose();
        }

        state.Bindings.Clear();

        // 样式触发器只摘自己压进样式触发器层的值；样式 setter 层由样式卸载单独处理。
        RemoveStyleTriggerValues(trigger);
    }

    /// <summary>Runs the exit path for a trigger that is leaving the collection while active.</summary>
    /// <param name="trigger">The trigger being removed or replaced.</param>
    internal void ExitTrigger(Trigger trigger)
    {
        if (_triggerStates.TryGetValue(trigger, out var state))
        {
            if (state.Active)
            {
                ApplyExit(trigger, state);
            }

            _triggerStates.Remove(trigger);
        }

        trigger.Detach(this);
    }

    private void ApplyEnter(Trigger trigger, TriggerRuntimeState state)
    {
        state.Previous.Clear();
        foreach (var setter in trigger.Setters)
        {
            if (setter?.Property is null)
            {
                continue;
            }

            state.Previous.Add(new PreviousValue(
                setter.Property,
                HasLocalValue(setter.Property),
                GetValue(setter.Property)));

            if ((setter.Binding ?? setter.Value as Binding) is { } binding)
            {
                var expression = new BindingExpression(
                    this,
                    binding,
                    new DirectTriggerBindingSink(this, setter.Property));
                state.Bindings[setter] = expression;
                expression.Attach();
            }
            else
            {
                setter.Property.SetValueCore(this, setter.Value, XamlValueSource.Trigger);
            }
        }

        trigger.EnterAction?.Invoke(this);
    }

    private void ApplyExit(Trigger trigger, TriggerRuntimeState state)
    {
        trigger.ExitAction?.Invoke(this);

        foreach (var expression in state.Bindings.Values)
        {
            expression.Dispose();
        }

        state.Bindings.Clear();

        // 倒序还原：叠在一起的 setter 按应用的逆序解开。
        for (var index = state.Previous.Count - 1; index >= 0; index--)
        {
            var previous = state.Previous[index];
            if (previous.HadLocalValue)
            {
                previous.Property.SetValueCore(this, previous.Value, XamlValueSource.Trigger);
            }
            else
            {
                previous.Property.ClearValueCore(this, XamlValueSource.Trigger);
            }
        }

        state.Previous.Clear();
    }

    private void ApplyNativeBindings()
    {
        if (_applyingNativeBindings || _disposed)
        {
            return;
        }

        _applyingNativeBindings = true;
        try
        {
            if (_nativeBindings.Exists(static entry => !string.IsNullOrEmpty(entry.Definition.ElementName)))
            {
                EnsureNativeBindingScope();
            }

            foreach (var entry in _nativeBindings)
            {
                ApplyNativeBinding(entry);
            }
        }
        finally
        {
            _applyingNativeBindings = false;
        }
    }

    private void ApplyNativeBinding(NativeBindingEntry entry)
    {
        if (entry.Applied is not null)
        {
            try
            {
                entry.Target.DataBindings.Remove(entry.Applied);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[VeloxDev.Xaml] Could not remove native binding: {exception.Message}");
            }

            entry.Applied = null;
        }

        var source = ResolveBindingSource(entry.Definition, entry.Target);
        if (source is null)
        {
            return;
        }

        if (entry.Definition.Mode == BindingMode.OneTime)
        {
            ApplyOneTimeNativeBinding(entry, source);
            return;
        }

        try
        {
            var trigger = entry.Definition.UpdateSourceTrigger == UpdateSourceTrigger.Default
                ? UpdateSourceTrigger.PropertyChanged
                : entry.Definition.UpdateSourceTrigger;
            var updateMode = entry.Definition.Mode == BindingMode.TwoWay
                ? trigger == UpdateSourceTrigger.LostFocus
                    ? DataSourceUpdateMode.OnValidation
                    : trigger == UpdateSourceTrigger.Explicit
                        ? DataSourceUpdateMode.Never
                        : DataSourceUpdateMode.OnPropertyChanged
                : DataSourceUpdateMode.Never;

            var binding = new System.Windows.Forms.Binding(
                entry.PropertyName,
                source,
                entry.Definition.Path,
                formattingEnabled: true,
                updateMode);

            binding.Format += (_, e) => e.Value = ConvertBindingForward(entry.Definition, e.Value, e.DesiredType ?? typeof(object));
            binding.Parse += (_, e) => e.Value = ConvertBindingBack(entry.Definition, e.Value, e.DesiredType ?? typeof(object));

            entry.Target.DataBindings.Add(binding);
            entry.Applied = binding;
        }
        catch (Exception exception)
        {
            // 路径写错不该带走整个窗口；它会表现为一个没绑上的控件。
            Debug.WriteLine($"[VeloxDev.Xaml] Could not bind '{entry.PropertyName}': {exception.Message}");
        }
    }

    private object? ResolveBindingSource(Binding definition, System.Windows.Forms.Control target)
    {
        if (definition.Source is not null)
        {
            return definition.Source;
        }

        if (!string.IsNullOrEmpty(definition.ElementName))
        {
            var scope = NameScope.Find(this);
            return scope is not null && scope.TryFind(definition.ElementName!, out var named) ? named : null;
        }

        if (definition.RelativeSource is { } relative)
        {
            switch (relative.Mode)
            {
                case RelativeSourceMode.Self:
                    return target;

                case RelativeSourceMode.TemplatedParent:
                    return TemplatedParent;

                case RelativeSourceMode.FindAncestor:
                    if (relative.AncestorType is null)
                    {
                        return null;
                    }

                    var level = Math.Max(1, relative.AncestorLevel);
                    for (System.Windows.Forms.Control? current = target.Parent; current is not null; current = current.Parent)
                    {
                        if (!relative.AncestorType.IsInstanceOfType(current))
                        {
                            continue;
                        }

                        if (--level == 0)
                        {
                            return current;
                        }
                    }

                    return null;

                default:
                    return null;
            }
        }

        return DataContext;
    }

    private void ApplyOneTimeNativeBinding(NativeBindingEntry entry, object source)
    {
        try
        {
            var path = new BindingPath(entry.Definition.Path);
            var resolved = path.TryResolve(source, out var value, out _, out _);
            if (!resolved)
            {
                value = entry.Definition.FallbackValue;
            }
            else if (value is null && entry.Definition.TargetNullValue is not null)
            {
                value = entry.Definition.TargetNullValue;
            }

            var property = TypeDescriptor.GetProperties(entry.Target).Find(entry.PropertyName, ignoreCase: false);
            if (property is null || property.IsReadOnly)
            {
                return;
            }

            property.SetValue(entry.Target, ConvertBindingForward(entry.Definition, value, property.PropertyType));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[VeloxDev.Xaml] Could not apply one-time binding '{entry.PropertyName}': {exception.Message}");
        }
    }

    private static object? ConvertBindingForward(Binding definition, object? value, Type targetType)
    {
        if (definition.Converter is { } converter)
        {
            value = converter is IParameterizedValueConverter parameterized
                ? parameterized.Convert(value, targetType, definition.ConverterParameter)
                : converter.Convert(value, targetType);
        }

        if (!string.IsNullOrEmpty(definition.StringFormat) && value is not null)
        {
            var format = definition.StringFormat!.IndexOf('{') >= 0
                ? definition.StringFormat!
                : "{0:" + definition.StringFormat + "}";
            value = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
        }

        return XamlRuntime.ConvertValue(value, targetType);
    }

    private static object? ConvertBindingBack(Binding definition, object? value, Type sourceType)
    {
        if (definition.Converter is { } converter)
        {
            return converter is IParameterizedValueConverter parameterized
                ? parameterized.ConvertBack(value, sourceType, definition.ConverterParameter)
                : converter.ConvertBack(value, sourceType);
        }

        return XamlRuntime.ConvertValue(value, sourceType);
    }

    private void EnsureNativeBindingScope()
    {
        if (_nativeBindingScope is not null)
        {
            return;
        }

        var scope = NameScope.Find(this);
        if (scope is null)
        {
            return;
        }

        _nativeBindingScope = scope;
        scope.Registered += OnNativeBindingNameRegistered;
    }

    private void OnNativeBindingNameRegistered(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            ApplyNativeBindings();
        }
    }

    private void DetachNativeBindingScope()
    {
        if (_nativeBindingScope is not null)
        {
            _nativeBindingScope.Registered -= OnNativeBindingNameRegistered;
            _nativeBindingScope = null;
        }
    }

    private void RemoveNativeBindings()
    {
        foreach (var entry in _nativeBindings)
        {
            if (entry.Applied is null)
            {
                continue;
            }

            try
            {
                entry.Target.DataBindings.Remove(entry.Applied);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[VeloxDev.Xaml] Could not remove native binding: {exception.Message}");
            }

            entry.Applied = null;
        }

        _nativeBindings.Clear();
    }

    private sealed class NativeBindingEntry
    {
        public NativeBindingEntry(System.Windows.Forms.Control target, string propertyName, Binding definition)
        {
            Target = target;
            PropertyName = propertyName;
            Definition = definition;
        }

        public System.Windows.Forms.Control Target { get; }

        public string PropertyName { get; }

        public Binding Definition { get; }

        public System.Windows.Forms.Binding? Applied { get; set; }
    }

    private sealed class TriggerRuntimeState
    {
        public bool Active { get; set; }

        public List<PreviousValue> Previous { get; } = [];

        public Dictionary<Setter, BindingExpression> Bindings { get; } = [];

        public List<Action> Restore { get; } = [];
    }

    private sealed class StyleSetterBindingSink : IBindingValueSink
    {
        private readonly Control _owner;
        private readonly DependencyProperty _property;
        private object? _current;

        public StyleSetterBindingSink(Control owner, DependencyProperty property)
        {
            _owner = owner;
            _property = property;
        }

        public Type ValueType => _property.PropertyType;

        public bool CanWriteBack => false;

        public object? GetValue() => _current;

        public void SetValue(object? value)
        {
            var converted = _property.CoerceValue(_owner, value);
            _current = converted;
            var oldEffective = _owner.GetLocalValueOrDefault(_property);
            _owner._styleValues[_property] = converted;
            var newEffective = _owner.GetLocalValueOrDefault(_property);
            _property.RaiseChangedIfDifferent(_owner, oldEffective, newEffective, XamlValueSource.Style);
        }
    }

    private sealed class StyleTriggerBindingSink : IBindingValueSink
    {
        private readonly Control _owner;
        private readonly Trigger _trigger;
        private readonly DependencyProperty _property;
        private object? _current;

        public StyleTriggerBindingSink(Control owner, Trigger trigger, DependencyProperty property)
        {
            _owner = owner;
            _trigger = trigger;
            _property = property;
        }

        public Type ValueType => _property.PropertyType;

        public bool CanWriteBack => false;

        public object? GetValue() => _current;

        public void SetValue(object? value)
        {
            _current = _property.CoerceValue(_owner, value);
            _owner.AddStyleTriggerValue(_trigger, _property, _current);
        }
    }

    private sealed class DirectTriggerBindingSink : IBindingValueSink
    {
        private readonly Control _owner;
        private readonly DependencyProperty _property;

        public DirectTriggerBindingSink(Control owner, DependencyProperty property)
        {
            _owner = owner;
            _property = property;
        }

        public Type ValueType => _property.PropertyType;

        public bool CanWriteBack => false;

        public object? GetValue() => _property.GetValue(_owner);

        public void SetValue(object? value)
            => _property.SetValueCore(_owner, value, XamlValueSource.Trigger);
    }

    private sealed class StyleTriggerValue
    {
        public StyleTriggerValue(Trigger trigger, int order, object? value)
        {
            Trigger = trigger;
            Order = order;
            Value = value;
        }

        public Trigger Trigger { get; }

        public int Order { get; }

        public object? Value { get; }
    }

    private readonly record struct PreviousValue(DependencyProperty Property, bool HadLocalValue, object? Value);
}
