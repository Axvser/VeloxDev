using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What every routed pointer event carries: where it happened, on which component, from which view, and the handle
/// that decides what the framework and the rest of the route do with it.
/// </summary>
/// <remarks>
/// <para>
/// A concrete subclass is raised per pointer action (<see cref="WorkflowPointerEnteredEventArgs"/> and friends); the
/// adapter creates exactly one instance, and the same instance travels the whole route, so every subscriber sees
/// one <see cref="WorkflowEventHandle"/> for one action.
/// </para>
/// <para>
/// <see cref="Position"/> is in the surface's own coordinates, and its <see cref="Anchor.Layer"/> is the layer of
/// the view the input came from — the Z the route resolves against. A pointer has no layer of its own; adapters
/// pass <c>0</c> wherever a layerless position is required (the virtual-link endpoint takes the start endpoint's
/// layer instead).
/// </para>
/// </remarks>
/// <seealso cref="WorkflowInput"/>
public abstract class WorkflowPointerEventArgs : EventArgs
{
    /// <summary>Creates the argument. Called by the concrete subclasses.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from, or <see langword="null"/> when the platform has none.</param>
    /// <param name="target">The component the pointer is on, or <see langword="null"/> for empty canvas.</param>
    /// <param name="handle">The handle for this action, shared with the whole route.</param>
    protected WorkflowPointerEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
    {
        Position = position ?? throw new ArgumentNullException(nameof(position));
        Modifiers = modifiers;
        Source = source;
        Target = target;
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    /// <summary>Where the pointer is, in the surface's coordinates; its <see cref="Anchor.Layer"/> is the
    /// source view's layer.</summary>
    public Anchor Position { get; }

    /// <summary>The modifier keys held while the event happened.</summary>
    public InputModifiers Modifiers { get; }

    /// <summary>
    /// The view the input came from — the link view that caught the pointer, or the surface itself when the
    /// platform owns the hit region. This is what used to be the event's <c>sender</c>.
    /// </summary>
    public object? Source { get; }

    /// <summary>
    /// The component the adapter resolved the pointer onto, or <see langword="null"/> when it is over empty canvas.
    /// </summary>
    /// <remarks>
    /// The route hands this same value to every element it bubbles through — an ancestor sees the original target,
    /// not itself.
    /// </remarks>
    public IWorkflowViewModel? Target { get; }

    /// <summary>
    /// The handle for this action. Set <see cref="WorkflowEventHandle.PreventDefault"/> to keep the framework's own
    /// reaction from happening, and <see cref="WorkflowEventHandle.StopPropagation"/> to keep the event from
    /// reaching the target's ancestors.
    /// </summary>
    public WorkflowEventHandle Handle { get; }
}
