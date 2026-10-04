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
/// <seealso cref="IWorkflowInputEvents"/>
public sealed class WorkflowInputRelay
{
    /// <summary>Raised when the pointer arrives over this component.</summary>
    public event EventHandler<WorkflowPointerEnteredEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this component.</summary>
    public event EventHandler<WorkflowPointerExitedEventArgs>? PointerExited;

    /// <summary>Raised while the pointer moves over this component.</summary>
    public event EventHandler<WorkflowPointerMovedEventArgs>? PointerMoved;

    /// <summary>Raised when a pointer button goes down over this component.</summary>
    public event EventHandler<WorkflowPointerPressedEventArgs>? PointerPressed;

    /// <summary>Raised when a pointer button comes up over this component.</summary>
    public event EventHandler<WorkflowPointerReleasedEventArgs>? PointerReleased;

    /// <summary>Raised when the wheel turns over this component.</summary>
    public event EventHandler<WorkflowPointerWheelEventArgs>? PointerWheelChanged;

    /// <summary>Raised when a key goes down while this component is the key's target.</summary>
    public event EventHandler<WorkflowKeyDownEventArgs>? KeyDown;

    /// <summary>Raised when a key comes up while this component is the key's target.</summary>
    public event EventHandler<WorkflowKeyUpEventArgs>? KeyUp;

    // 按运行时类型派发：一个 args 实例只对应一个事件，路由每经过一级就调一次。
    internal void Raise(WorkflowPointerEventArgs e)
    {
        switch (e)
        {
            case WorkflowPointerEnteredEventArgs entered: PointerEntered?.Invoke(this, entered); break;
            case WorkflowPointerExitedEventArgs exited: PointerExited?.Invoke(this, exited); break;
            case WorkflowPointerMovedEventArgs moved: PointerMoved?.Invoke(this, moved); break;
            case WorkflowPointerPressedEventArgs pressed: PointerPressed?.Invoke(this, pressed); break;
            case WorkflowPointerReleasedEventArgs released: PointerReleased?.Invoke(this, released); break;
            case WorkflowPointerWheelEventArgs wheel: PointerWheelChanged?.Invoke(this, wheel); break;
        }
    }

    internal void Raise(WorkflowKeyEventArgs e)
    {
        switch (e)
        {
            case WorkflowKeyDownEventArgs down: KeyDown?.Invoke(this, down); break;
            case WorkflowKeyUpEventArgs up: KeyUp?.Invoke(this, up); break;
        }
    }
}
