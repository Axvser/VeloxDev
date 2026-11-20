using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>Whether the pointer is currently over this control; updated from the native mouse enter/leave callbacks.</summary>
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
        new PropertyMetadata(Thickness.DefaultMargin, OnMarginPropertyChanged));

    /// <summary>The inner spacing between this control's edge and its children, animatable like a WPF padding.</summary>
    /// <remarks>Every change is written to the native <see cref="System.Windows.Forms.ScrollableControl.Padding"/>, so native layout and scrolling keep seeing it.</remarks>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding),
        typeof(Thickness),
        typeof(Control),
        new PropertyMetadata(Thickness.Empty, OnPaddingPropertyChanged));

    private readonly Dictionary<DependencyProperty, object?> _values = [];
    private readonly Dictionary<DependencyProperty, BindingExpression> _bindings = [];
    private readonly List<BindingExpression> _attachedBindings = [];
    private readonly List<NativeBindingEntry> _nativeBindings = [];
    private readonly Dictionary<Trigger, TriggerRuntimeState> _triggerStates = [];
    private readonly ResourceDictionary _resources = new();
    private readonly Dictionary<DependencyProperty, object?> _styleValues = [];
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
    private readonly HashSet<Keys> _pressedKeys = [];
    private bool _evaluatingTriggers;
    private bool _applyingNativeBindings;
    private bool _pointerCaptured;
    private bool _disposed;

    /// <summary>Creates the control and connects its resource change signal.</summary>
    public Control()
    {
        // 所有 XAML 基础控件统一双缓冲：局部重绘先落到该控件自己的后缓冲，避免父/子窗口各画一半。
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

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
        get => HasLocalValue(MarginProperty) ? (Thickness)GetValue(MarginProperty)! : Thickness.FromPadding(base.Margin);
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
        get => HasLocalValue(PaddingProperty) ? (Thickness)GetValue(PaddingProperty)! : Thickness.FromPadding(base.Padding);
        set => SetValue(PaddingProperty, value);
    }

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
            control.ApplyNativeMargin(margin);
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
            control.ApplyNativePadding(padding);
            return;
        }

        var native = padding.ToPadding();
        if (target.Padding != native)
        {
            target.Padding = native;
        }
    }

    // DP 是作者面，原生属性是布局面；每次变化都把后者写一遍，WinForms 的停靠/滚动/边距才和 XAML 看到的一致。
    private void ApplyNativeMargin(Thickness margin)
    {
        var native = margin.ToPadding();
        if (base.Margin != native)
        {
            base.Margin = native;
        }
    }

    private void ApplyNativePadding(Thickness padding)
    {
        var native = padding.ToPadding();
        if (base.Padding != native)
        {
            base.Padding = native;
        }
    }

    /// <summary>
    /// The inherited data context used by bindings. Assigning it makes this control's bindings, triggers and native
    /// bindings re-resolve; descendants without a local value follow.
    /// </summary>
    /// <remarks>
    /// The .NET 10 base <see cref="System.Windows.Forms.Control.DataContext"/> is deliberately hidden: this one participates in the XAML
    /// control tree's inheritance and in dependency-property change notification.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new object? DataContext
    {
        get => DataContextProperty.GetValue(this);
        set => DataContextProperty.SetValue(this, value);
    }

    /// <summary>Raised when this control's effective data context changes.</summary>
    public new event EventHandler? DataContextChanged;

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

    /// <summary>The triggers owned by this control.</summary>
    public TriggerCollection Triggers => _triggers ??= new TriggerCollection(this);

    /// <summary>The resources owned by this control, addressable by key from <c>StaticResource</c>.</summary>
    public ResourceDictionary Resources => _resources;

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
    protected override void OnMouseDown(MouseEventArgs e)
    {
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
        SetValue(IsPointerOverProperty, true);
        XamlInput.RaisePointerEntered(XamlInput.CreatePointerEnteredArgs(this));
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetValue(IsPointerOverProperty, false);
        XamlInput.RaisePointerExited(XamlInput.CreatePointerExitedArgs(this));
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
    }

    /// <inheritdoc />
    protected override void OnLostFocus(EventArgs e)
    {
        _pressedKeys.Clear();
        SetValue(IsKeyboardFocusedProperty, false);
        _pointerCaptured = false;
        base.OnLostFocus(e);
    }

    /// <inheritdoc />
    protected override void OnHandleDestroyed(EventArgs e)
    {
        _pressedKeys.Clear();
        SetValue(IsKeyboardFocusedProperty, false);
        SetValue(IsPointerOverProperty, false);
        _pointerCaptured = false;
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
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            foreach (var expression in _bindings.Values)
            {
                expression.Dispose();
            }

            foreach (var expression in _attachedBindings)
            {
                expression.Dispose();
            }

            _bindings.Clear();
            _attachedBindings.Clear();
            RemoveNativeBindings();

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

        return null;
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

        var effectiveSetters = new Dictionary<DependencyProperty, object?>();
        foreach (var current in chain)
        {
            foreach (var setter in current.Setters)
            {
                if (setter?.Property is { } property)
                {
                    // 派生样式后写，覆盖基础样式的同属性 setter。
                    effectiveSetters[property] = setter.Value;
                }
            }
        }

        foreach (var pair in effectiveSetters)
        {
            SetStyleValue(pair.Key, pair.Value);
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
            _templateContent = template.CreateContent();
            Controls.Add(_templateContent);
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
            _templateContent.DataContext = context;
        }
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
            if (setter?.Property is { } property)
            {
                AddStyleTriggerValue(trigger, property, setter.Value);
            }
        }

        trigger.EnterAction?.Invoke(this);
    }

    private void ApplyStyleExit(Trigger trigger, TriggerRuntimeState state)
    {
        trigger.ExitAction?.Invoke(this);

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

            setter.Property.SetValueCore(this, setter.Value, XamlValueSource.Trigger);
        }

        trigger.EnterAction?.Invoke(this);
    }

    private void ApplyExit(Trigger trigger, TriggerRuntimeState state)
    {
        trigger.ExitAction?.Invoke(this);

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

        var context = DataContext;
        if (context is null)
        {
            return;
        }

        if (entry.Definition.Mode == BindingMode.OneTime)
        {
            ApplyOneTimeNativeBinding(entry, context);
            return;
        }

        try
        {
            var updateMode = entry.Definition.Mode == BindingMode.TwoWay
                ? DataSourceUpdateMode.OnPropertyChanged
                : DataSourceUpdateMode.Never;

            var binding = new System.Windows.Forms.Binding(
                entry.PropertyName,
                context,
                entry.Definition.Path,
                formattingEnabled: true,
                updateMode);

            entry.Target.DataBindings.Add(binding);
            entry.Applied = binding;
        }
        catch (Exception exception)
        {
            // 路径写错不该带走整个窗口；它会表现为一个没绑上的控件。
            Debug.WriteLine($"[VeloxDev.Xaml] Could not bind '{entry.PropertyName}': {exception.Message}");
        }
    }

    private static void ApplyOneTimeNativeBinding(NativeBindingEntry entry, object context)
    {
        try
        {
            var value = XamlRuntime.ConvertValue(ResolvePath(context, entry.Definition.Path), typeof(object));
            if (entry.Definition.Converter is { } converter)
            {
                var descriptor = TypeDescriptor.GetProperties(entry.Target).Find(entry.PropertyName, ignoreCase: false);
                value = converter.Convert(value, descriptor?.PropertyType ?? typeof(object));
            }

            var property = TypeDescriptor.GetProperties(entry.Target).Find(entry.PropertyName, ignoreCase: false);
            if (property is null || property.IsReadOnly)
            {
                return;
            }

            property.SetValue(entry.Target, XamlRuntime.ConvertValue(value, property.PropertyType));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[VeloxDev.Xaml] Could not apply one-time binding '{entry.PropertyName}': {exception.Message}");
        }
    }

    private static object? ResolvePath(object? source, string path)
    {
        if (source is null || string.IsNullOrEmpty(path) || path == ".")
        {
            return source;
        }

        var current = source;
        foreach (var segment in path.Split('.'))
        {
            if (current is null || segment.Length == 0)
            {
                return null;
            }

            var descriptor = TypeDescriptor.GetProperties(current).Find(segment, ignoreCase: false);
            if (descriptor is null)
            {
                return null;
            }

            current = descriptor.GetValue(current);
        }

        return current;
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
