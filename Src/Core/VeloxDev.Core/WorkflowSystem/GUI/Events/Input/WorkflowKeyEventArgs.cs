using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What every routed key event carries: which key, on which component, from which view, and the handle that decides
/// what the framework and the rest of the route do with it.
/// </summary>
/// <remarks>
/// <para>
/// One instance travels the whole route, so every subscriber on it shares one <see cref="WorkflowEventHandle"/>.
/// The concrete subclasses are <see cref="WorkflowKeyDownEventArgs"/> and <see cref="WorkflowKeyUpEventArgs"/> — a down and an up
/// are two separate events, not two phases of one.
/// </para>
/// <para>
/// A key event's <see cref="WorkflowPointerEventArgs.Target"/> is whatever the adapter decided the key applies to,
/// which for the Delete key is the component the pointer is on — a key has no position of its own.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowInput"/>
public abstract class WorkflowKeyEventArgs : EventArgs
{
    /// <summary>Creates the argument. Called by the concrete subclasses.</summary>
    /// <param name="key">The key, or <see cref="WorkflowKey.Unknown"/> when this enum does not name it.</param>
    /// <param name="rawKeyCode">The platform's own key code; not comparable across adapters.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="isRepeat">Whether the platform reports this as an auto-repeat.</param>
    /// <param name="source">The view that had the keyboard focus, or <see langword="null"/>.</param>
    /// <param name="target">The component the adapter applies this key to, or <see langword="null"/>.</param>
    /// <param name="handle">The handle for this action, shared with the whole route.</param>
    protected WorkflowKeyEventArgs(
        WorkflowKey key, int rawKeyCode, InputModifiers modifiers, bool isRepeat,
        object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
    {
        Key = key;
        RawKeyCode = rawKeyCode;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
        Source = source;
        Target = target;
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    /// <summary>The key, or <see cref="WorkflowKey.Unknown"/> when <see cref="WorkflowKey"/> does not name it.</summary>
    public WorkflowKey Key { get; }

    /// <summary>
    /// The platform's own key code. Only meaningful together with <see cref="Key"/> being
    /// <see cref="WorkflowKey.Unknown"/>, and only on the platform that produced it.
    /// </summary>
    public int RawKeyCode { get; }

    /// <summary>The modifier keys held while the event happened.</summary>
    public InputModifiers Modifiers { get; }

    /// <summary>Whether the platform reports this as an auto-repeat rather than a fresh press.</summary>
    public bool IsRepeat { get; }

    /// <summary>The view that had the keyboard focus when the key was handled.</summary>
    public object? Source { get; }

    /// <summary>The component the adapter applies this key to, or <see langword="null"/> when none applies.</summary>
    public IWorkflowViewModel? Target { get; }

    /// <summary>
    /// The handle for this action. <see cref="WorkflowEventHandle.PreventDefault"/> suppresses the framework's own
    /// reaction — for a Delete key that means the link is not deleted — and
    /// <see cref="WorkflowEventHandle.StopPropagation"/> keeps the event from reaching the target's ancestors.
    /// </summary>
    public WorkflowEventHandle Handle { get; }
}
