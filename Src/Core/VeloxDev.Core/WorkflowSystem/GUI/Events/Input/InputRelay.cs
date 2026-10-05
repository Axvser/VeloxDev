using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The pointer and keyboard events one component receives. One instance per Helper, so the raising logic exists once
/// rather than in every helper implementation.
/// </summary>
/// <remarks>
/// The events are raised by <see cref="WorkflowInput"/> as an input event bubbles through this component; the
/// <c>sender</c> is this relay, which is how a subscriber tells which element of the route it is hearing from. The
/// framework's own reaction, if any, runs after the route — see <see cref="WorkflowEventHandle"/>.
/// </remarks>
/// <seealso cref="IInputEvents"/>
public sealed class InputRelay
{
    /// <summary>Raised when the pointer arrives over this component.</summary>
    public event EventHandler<PointerEnteredEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this component.</summary>
    public event EventHandler<PointerExitedEventArgs>? PointerExited;

    /// <summary>Raised while the pointer moves over this component.</summary>
    public event EventHandler<PointerMovedEventArgs>? PointerMoved;

    /// <summary>Raised when a pointer button goes down over this component.</summary>
    public event EventHandler<PointerPressedEventArgs>? PointerPressed;

    /// <summary>Raised when a pointer button comes up over this component.</summary>
    public event EventHandler<PointerReleasedEventArgs>? PointerReleased;

    /// <summary>Raised when the wheel turns over this component.</summary>
    public event EventHandler<PointerWheelEventArgs>? PointerWheelChanged;

    /// <summary>Raised when a key goes down while this component is the key's target.</summary>
    public event EventHandler<KeyDownEventArgs>? KeyDown;

    /// <summary>Raised when a key comes up while this component is the key's target.</summary>
    public event EventHandler<KeyUpEventArgs>? KeyUp;

    // 按运行时类型派发：一个 args 实例只对应一个事件，路由每经过一级就调一次。
    internal void Raise(PointerEventArgs e)
    {
        switch (e)
        {
            case PointerEnteredEventArgs entered: PointerEntered?.Invoke(this, entered); break;
            case PointerExitedEventArgs exited: PointerExited?.Invoke(this, exited); break;
            case PointerMovedEventArgs moved: PointerMoved?.Invoke(this, moved); break;
            case PointerPressedEventArgs pressed: PointerPressed?.Invoke(this, pressed); break;
            case PointerReleasedEventArgs released: PointerReleased?.Invoke(this, released); break;
            case PointerWheelEventArgs wheel: PointerWheelChanged?.Invoke(this, wheel); break;
        }
    }

    internal void Raise(KeyEventArgs e)
    {
        switch (e)
        {
            case KeyDownEventArgs down: KeyDown?.Invoke(this, down); break;
            case KeyUpEventArgs up: KeyUp?.Invoke(this, up); break;
        }
    }
}
