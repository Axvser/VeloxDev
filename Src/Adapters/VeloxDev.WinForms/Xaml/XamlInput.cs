using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The translation and bridging layer for WinForms-native pointer and keyboard input.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> translates its own native mouse/key messages into the custom events directly. This
/// class adds the same input to native WinForms controls through a bridge that subscribes to their native events,
/// and exposes the translation helpers so a future surface can map the same data to other input models without
/// repeating the WinForms conversion.
/// </para>
/// <para>
/// Raise helpers bubble the event from the source control through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> instances. The
/// event's <see cref="PointerEventArgs.Source"/> remains the original control, and setting
/// <see cref="PointerEventArgs.Handled"/> stops further ancestors. Native WinForms events are left untouched.
/// </para>
/// </remarks>
public static class XamlInput
{
    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, NativeInputBridge> Bridges = new();

    /// <summary>Adds a pointer-pressed handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerPressed(System.Windows.Forms.Control target, EventHandler<PointerPressedEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerPressed += handler;
            return;
        }

        GetBridge(target).AddPointerPressed(handler);
    }

    /// <summary>Adds a pointer-moved handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerMoved(System.Windows.Forms.Control target, EventHandler<PointerMovedEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerMoved += handler;
            return;
        }

        GetBridge(target).AddPointerMoved(handler);
    }

    /// <summary>Adds a pointer-released handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerReleased(System.Windows.Forms.Control target, EventHandler<PointerReleasedEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerReleased += handler;
            return;
        }

        GetBridge(target).AddPointerReleased(handler);
    }

    /// <summary>Adds a pointer-entered handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerEntered(System.Windows.Forms.Control target, EventHandler<PointerEnteredEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerEntered += handler;
            return;
        }

        GetBridge(target).AddPointerEntered(handler);
    }

    /// <summary>Adds a pointer-exited handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerExited(System.Windows.Forms.Control target, EventHandler<PointerExitedEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerExited += handler;
            return;
        }

        GetBridge(target).AddPointerExited(handler);
    }

    /// <summary>Adds a pointer-wheel handler to a control, bridging native controls.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddPointerWheelChanged(System.Windows.Forms.Control target, EventHandler<PointerWheelEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.PointerWheelChanged += handler;
            return;
        }

        GetBridge(target).AddPointerWheelChanged(handler);
    }

    /// <summary>Adds a handler to the explicit <see cref="VeloxDev.WinForms.Xaml.Controls.Control.KeyInputDown"/> keyboard abstraction.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    /// <remarks>
    /// This does not subscribe to the native <see cref="System.Windows.Forms.Control.KeyDown"/> event. XAML <c>KeyDown</c> binds that
    /// native event directly; the explicit keyboard abstraction is a separate, opt-in API.
    /// </remarks>
    public static void AddKeyInputDown(System.Windows.Forms.Control target, EventHandler<KeyInputEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.KeyInputDown += handler;
            return;
        }

        GetBridge(target).AddKeyInputDown(handler);
    }

    /// <summary>Adds a handler to the explicit <see cref="VeloxDev.WinForms.Xaml.Controls.Control.KeyInputUp"/> keyboard abstraction.</summary>
    /// <param name="target">The control whose input is observed.</param>
    /// <param name="handler">The handler to add.</param>
    /// <remarks>
    /// This does not subscribe to the native <see cref="System.Windows.Forms.Control.KeyUp"/> event. XAML <c>KeyUp</c> binds that native
    /// event directly; the explicit keyboard abstraction is a separate, opt-in API.
    /// </remarks>
    public static void AddKeyInputUp(System.Windows.Forms.Control target, EventHandler<KeyInputEventArgs> handler)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.KeyInputUp += handler;
            return;
        }

        GetBridge(target).AddKeyInputUp(handler);
    }

    /// <summary>Removes all bridge handlers from a native control and unsubscribes its native events.</summary>
    /// <param name="target">The native control to clean up; Control handlers are removed with <c>-=</c>.</param>
    public static void Remove(System.Windows.Forms.Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (target is VeloxDev.WinForms.Xaml.Controls.Control)
        {
            return;
        }

        if (Bridges.TryGetValue(target, out var bridge))
        {
            bridge.Dispose();
            Bridges.Remove(target);
        }
    }

    /// <summary>Raises pointer-pressed data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerPressed(PointerPressedEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerPressed(value), args, static value => value.Handled);
    }

    /// <summary>Raises pointer movement data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerMoved(PointerMovedEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerMoved(value), args, static value => value.Handled);
    }

    /// <summary>Raises pointer release data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerReleased(PointerReleasedEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerReleased(value), args, static value => value.Handled);
    }

    /// <summary>Raises pointer entry data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerEntered(PointerEnteredEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerEntered(value), args, static value => value.Handled);
    }

    /// <summary>Raises pointer exit data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerExited(PointerExitedEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerExited(value), args, static value => value.Handled);
    }

    /// <summary>Raises pointer-wheel data, bubbling it from the source through ancestor <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>s.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaisePointerWheelChanged(PointerWheelEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaisePointerWheelChanged(value), args, static value => value.Handled);
    }

    /// <summary>Raises explicit keyboard-down data through <see cref="VeloxDev.WinForms.Xaml.Controls.Control.KeyInputDown"/>; native KeyDown is untouched.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaiseKeyInputDown(KeyInputEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaiseKeyInputDown(value), args, static value => value.Handled);
    }

    /// <summary>Raises explicit keyboard-up data through <see cref="VeloxDev.WinForms.Xaml.Controls.Control.KeyInputUp"/>; native KeyUp is untouched.</summary>
    /// <param name="args">The data to raise.</param>
    public static void RaiseKeyInputUp(KeyInputEventArgs args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        Bubble(args.Source, static (control, value) => control.RaiseKeyInputUp(value), args, static value => value.Handled);
    }

    /// <summary>Raises a pointer press with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    /// <param name="button">The pressed button.</param>
    /// <param name="clickCount">The click count; at least one.</param>
    public static void RaisePointerPressed(System.Windows.Forms.Control source, Point position, MouseButtons button, int clickCount = 1)
        => RaisePointerPressed(new PointerPressedEventArgs(source, position, System.Windows.Forms.Control.ModifierKeys, button, clickCount));

    /// <summary>Raises pointer movement with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    public static void RaisePointerMoved(System.Windows.Forms.Control source, Point position)
        => RaisePointerMoved(new PointerMovedEventArgs(source, position, System.Windows.Forms.Control.ModifierKeys));

    /// <summary>Raises a pointer release with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    /// <param name="button">The released button.</param>
    public static void RaisePointerReleased(System.Windows.Forms.Control source, Point position, MouseButtons button)
        => RaisePointerReleased(new PointerReleasedEventArgs(source, position, System.Windows.Forms.Control.ModifierKeys, button));

    /// <summary>Raises pointer entry with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    public static void RaisePointerEntered(System.Windows.Forms.Control source, Point position)
        => RaisePointerEntered(new PointerEnteredEventArgs(source, position, System.Windows.Forms.Control.ModifierKeys));

    /// <summary>Raises pointer exit with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    public static void RaisePointerExited(System.Windows.Forms.Control source, Point position)
        => RaisePointerExited(new PointerExitedEventArgs(source, position, System.Windows.Forms.Control.ModifierKeys));

    /// <summary>Raises pointer-wheel data with the current modifier keys.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="position">The position in the source's client coordinates.</param>
    /// <param name="delta">The vertical wheel delta.</param>
    /// <param name="deltaX">The horizontal wheel delta.</param>
    /// <param name="deltaY">The vertical wheel delta; defaults to <paramref name="delta"/>.</param>
    public static void RaisePointerWheelChanged(System.Windows.Forms.Control source, Point position, int delta, int deltaX = 0, int deltaY = 0)
        => RaisePointerWheelChanged(new PointerWheelEventArgs(
            source,
            position,
            System.Windows.Forms.Control.ModifierKeys,
            delta,
            deltaX,
            deltaY == 0 ? delta : deltaY));

    /// <summary>Raises key-down data with an explicit modifier set.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="keyCode">The key code.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <param name="isRepeat">Whether this is a repeat.</param>
    public static void RaiseKeyInputDown(System.Windows.Forms.Control source, Keys keyCode, Keys modifiers, bool isRepeat = false)
        => RaiseKeyInputDown(new KeyInputEventArgs(source, keyCode, modifiers, isRepeat));

    /// <summary>Raises key-up data with an explicit modifier set.</summary>
    /// <param name="source">The source control.</param>
    /// <param name="keyCode">The key code.</param>
    /// <param name="modifiers">The modifier keys.</param>
    public static void RaiseKeyInputUp(System.Windows.Forms.Control source, Keys keyCode, Keys modifiers)
        => RaiseKeyInputUp(new KeyInputEventArgs(source, keyCode, modifiers, isRepeat: false));

    /// <summary>Creates pointer-press data from a native <see cref="MouseEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event.</param>
    /// <param name="e">The native event data.</param>
    public static PointerPressedEventArgs CreatePointerPressedArgs(System.Windows.Forms.Control source, MouseEventArgs e)
        => new(source, e.Location, System.Windows.Forms.Control.ModifierKeys, e.Button, e.Clicks < 1 ? 1 : e.Clicks);

    /// <summary>Creates pointer-movement data from a native <see cref="MouseEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event.</param>
    /// <param name="e">The native event data.</param>
    public static PointerMovedEventArgs CreatePointerMovedArgs(System.Windows.Forms.Control source, MouseEventArgs e)
        => new(source, e.Location, System.Windows.Forms.Control.ModifierKeys);

    /// <summary>Creates pointer-release data from a native <see cref="MouseEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event.</param>
    /// <param name="e">The native event data.</param>
    public static PointerReleasedEventArgs CreatePointerReleasedArgs(System.Windows.Forms.Control source, MouseEventArgs e)
        => new(source, e.Location, System.Windows.Forms.Control.ModifierKeys, e.Button);

    /// <summary>Creates pointer-entry data using the current cursor position.</summary>
    /// <param name="source">The control that received the native event.</param>
    public static PointerEnteredEventArgs CreatePointerEnteredArgs(System.Windows.Forms.Control source)
        => new(source, PointerPosition(source), System.Windows.Forms.Control.ModifierKeys);

    /// <summary>Creates pointer-exit data using the current cursor position.</summary>
    /// <param name="source">The control that received the native event.</param>
    public static PointerExitedEventArgs CreatePointerExitedArgs(System.Windows.Forms.Control source)
        => new(source, PointerPosition(source), System.Windows.Forms.Control.ModifierKeys);

    /// <summary>Creates pointer-wheel data from a native <see cref="MouseEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event.</param>
    /// <param name="e">The native event data.</param>
    public static PointerWheelEventArgs CreatePointerWheelArgs(System.Windows.Forms.Control source, MouseEventArgs e)
        => new(source, e.Location, System.Windows.Forms.Control.ModifierKeys, e.Delta, 0, e.Delta);

    /// <summary>Creates key-down data from a native <see cref="KeyEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event; used for consistency of the call site.</param>
    /// <param name="e">The native event data.</param>
    /// <param name="isRepeat">Whether the key was already down.</param>
    public static KeyInputEventArgs CreateKeyInputDownArgs(System.Windows.Forms.Control source, KeyEventArgs e, bool isRepeat)
        => new(source, e.KeyCode, e.Modifiers, isRepeat);

    /// <summary>Creates key-up data from a native <see cref="KeyEventArgs"/>.</summary>
    /// <param name="source">The control that received the native event; used for consistency of the call site.</param>
    /// <param name="e">The native event data.</param>
    public static KeyInputEventArgs CreateKeyInputUpArgs(System.Windows.Forms.Control source, KeyEventArgs e)
        => new(source, e.KeyCode, e.Modifiers, isRepeat: false);

    private static NativeInputBridge GetBridge(System.Windows.Forms.Control target)
    {
        if (target.IsDisposed || target.Disposing)
        {
            throw new ObjectDisposedException(target.Name);
        }

        return Bridges.GetValue(target, static control => new NativeInputBridge(control));
    }

    private static void Bubble<TArgs>(
        System.Windows.Forms.Control? start,
        Action<VeloxDev.WinForms.Xaml.Controls.Control, TArgs> invoke,
        TArgs args,
        Func<TArgs, bool> handled)
    {
        for (System.Windows.Forms.Control? current = start; current is not null; current = current.Parent)
        {
            if (current is VeloxDev.WinForms.Xaml.Controls.Control xaml)
            {
                invoke(xaml, args);
                if (handled(args))
                {
                    return;
                }
            }
        }
    }

    private static Point PointerPosition(System.Windows.Forms.Control source)
    {
        try
        {
            return source.PointToClient(Cursor.Position);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return Point.Empty;
        }
    }

    private sealed class NativeInputBridge : IDisposable
    {
        private readonly WeakReference<System.Windows.Forms.Control> _target;
        private readonly List<EventHandler<PointerPressedEventArgs>> _pointerPressed = [];
        private readonly List<EventHandler<PointerMovedEventArgs>> _pointerMoved = [];
        private readonly List<EventHandler<PointerReleasedEventArgs>> _pointerReleased = [];
        private readonly List<EventHandler<PointerEnteredEventArgs>> _pointerEntered = [];
        private readonly List<EventHandler<PointerExitedEventArgs>> _pointerExited = [];
        private readonly List<EventHandler<PointerWheelEventArgs>> _pointerWheelChanged = [];
        private readonly List<EventHandler<KeyInputEventArgs>> _keyInputDown = [];
        private readonly List<EventHandler<KeyInputEventArgs>> _keyInputUp = [];
        private readonly HashSet<Keys> _pressedKeys = [];
        private bool _disposed;

        public NativeInputBridge(System.Windows.Forms.Control target)
        {
            _target = new WeakReference<System.Windows.Forms.Control>(target);
            target.MouseDown += OnMouseDown;
            target.MouseMove += OnMouseMove;
            target.MouseUp += OnMouseUp;
            target.MouseEnter += OnMouseEnter;
            target.MouseLeave += OnMouseLeave;
            target.MouseWheel += OnMouseWheel;
            target.KeyDown += OnKeyDown;
            target.KeyUp += OnKeyUp;
            target.LostFocus += OnLostFocus;
            target.Disposed += OnDisposed;
        }

        public void AddPointerPressed(EventHandler<PointerPressedEventArgs> handler)
        {
            if (!_pointerPressed.Contains(handler)) _pointerPressed.Add(handler);
        }

        public void AddPointerMoved(EventHandler<PointerMovedEventArgs> handler)
        {
            if (!_pointerMoved.Contains(handler)) _pointerMoved.Add(handler);
        }

        public void AddPointerReleased(EventHandler<PointerReleasedEventArgs> handler)
        {
            if (!_pointerReleased.Contains(handler)) _pointerReleased.Add(handler);
        }

        public void AddPointerEntered(EventHandler<PointerEnteredEventArgs> handler)
        {
            if (!_pointerEntered.Contains(handler)) _pointerEntered.Add(handler);
        }

        public void AddPointerExited(EventHandler<PointerExitedEventArgs> handler)
        {
            if (!_pointerExited.Contains(handler)) _pointerExited.Add(handler);
        }

        public void AddPointerWheelChanged(EventHandler<PointerWheelEventArgs> handler)
        {
            if (!_pointerWheelChanged.Contains(handler)) _pointerWheelChanged.Add(handler);
        }

        public void AddKeyInputDown(EventHandler<KeyInputEventArgs> handler)
        {
            if (!_keyInputDown.Contains(handler)) _keyInputDown.Add(handler);
        }

        public void AddKeyInputUp(EventHandler<KeyInputEventArgs> handler)
        {
            if (!_keyInputUp.Contains(handler)) _keyInputUp.Add(handler);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_target.TryGetTarget(out var target))
            {
                target.MouseDown -= OnMouseDown;
                target.MouseMove -= OnMouseMove;
                target.MouseUp -= OnMouseUp;
                target.MouseEnter -= OnMouseEnter;
                target.MouseLeave -= OnMouseLeave;
                target.MouseWheel -= OnMouseWheel;
                target.KeyDown -= OnKeyDown;
                target.KeyUp -= OnKeyUp;
                target.LostFocus -= OnLostFocus;
                target.Disposed -= OnDisposed;
            }

            _pointerPressed.Clear();
            _pointerMoved.Clear();
            _pointerReleased.Clear();
            _pointerEntered.Clear();
            _pointerExited.Clear();
            _pointerWheelChanged.Clear();
            _keyInputDown.Clear();
            _keyInputUp.Clear();
            _pressedKeys.Clear();
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (_pointerPressed.Count == 0) return;
            Dispatch(XamlInput.CreatePointerPressedArgs(Target(sender), e), _pointerPressed);
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_pointerMoved.Count == 0) return;
            Dispatch(XamlInput.CreatePointerMovedArgs(Target(sender), e), _pointerMoved);
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (_pointerReleased.Count == 0) return;
            Dispatch(XamlInput.CreatePointerReleasedArgs(Target(sender), e), _pointerReleased);
        }

        private void OnMouseEnter(object? sender, EventArgs e)
        {
            if (_pointerEntered.Count == 0) return;
            Dispatch(XamlInput.CreatePointerEnteredArgs(Target(sender)), _pointerEntered);
        }

        private void OnMouseLeave(object? sender, EventArgs e)
        {
            if (_pointerExited.Count == 0) return;
            Dispatch(XamlInput.CreatePointerExitedArgs(Target(sender)), _pointerExited);
        }

        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            if (_pointerWheelChanged.Count == 0) return;
            Dispatch(XamlInput.CreatePointerWheelArgs(Target(sender), e), _pointerWheelChanged);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (_keyInputDown.Count == 0) return;

            var target = Target(sender);
            var isRepeat = !_pressedKeys.Add(e.KeyCode);
            Dispatch(XamlInput.CreateKeyInputDownArgs(target, e, isRepeat), _keyInputDown);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Remove(e.KeyCode);
            if (_keyInputUp.Count == 0) return;

            Dispatch(XamlInput.CreateKeyInputUpArgs(Target(sender), e), _keyInputUp);
        }

        private void OnLostFocus(object? sender, EventArgs e) => _pressedKeys.Clear();

        private void OnDisposed(object? sender, EventArgs e)
        {
            Dispose();
            if (sender is System.Windows.Forms.Control target)
            {
                Bridges.Remove(target);
            }
        }

        private void Dispatch<TArgs>(TArgs args, List<EventHandler<TArgs>> handlers)
            where TArgs : PointerEventArgs
        {
            if (_disposed)
            {
                return;
            }

            var target = args.Source;
            foreach (var handler in handlers.ToArray())
            {
                handler(target, args);
            }

            if (args.Handled)
            {
                return;
            }

            switch (args)
            {
                case PointerPressedEventArgs pressed:
                    XamlInput.RaisePointerPressed(pressed);
                    break;
                case PointerMovedEventArgs moved:
                    XamlInput.RaisePointerMoved(moved);
                    break;
                case PointerReleasedEventArgs released:
                    XamlInput.RaisePointerReleased(released);
                    break;
                case PointerEnteredEventArgs entered:
                    XamlInput.RaisePointerEntered(entered);
                    break;
                case PointerExitedEventArgs exited:
                    XamlInput.RaisePointerExited(exited);
                    break;
                case PointerWheelEventArgs wheel:
                    XamlInput.RaisePointerWheelChanged(wheel);
                    break;
            }
        }

        private void Dispatch(KeyInputEventArgs args, List<EventHandler<KeyInputEventArgs>> handlers)
        {
            if (_disposed)
            {
                return;
            }

            var target = args.Source;
            foreach (var handler in handlers.ToArray())
            {
                handler(target, args);
            }

            if (args.Handled)
            {
                return;
            }

            if (ReferenceEquals(handlers, _keyInputDown))
            {
                XamlInput.RaiseKeyInputDown(args);
            }
            else
            {
                XamlInput.RaiseKeyInputUp(args);
            }
        }

        private System.Windows.Forms.Control Target(object? sender)
        {
            if (sender is System.Windows.Forms.Control control)
            {
                return control;
            }

            if (_target.TryGetTarget(out var target))
            {
                return target;
            }

            throw new ObjectDisposedException(nameof(NativeInputBridge));
        }
    }
}
