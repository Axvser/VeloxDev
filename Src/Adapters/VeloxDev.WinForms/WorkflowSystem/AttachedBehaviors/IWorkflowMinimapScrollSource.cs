using System;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay whose viewport can be dragged to request surface scrolling.
/// </summary>
/// <remarks>
/// Kept off <see cref="IWorkflowMinimapOverlay"/> so the shared Core interface stays framework-agnostic — the
/// event carries screen-space scroll, which only a host with its own scroll model can consume.
/// <para>
/// Declared by the adapter rather than by the generated minimap: <see cref="WorkflowTreeView"/> subscribes to it,
/// so a tree-only host would otherwise fail to compile for want of an interface that only the minimap item emits.
/// </para>
/// </remarks>
public interface IWorkflowMinimapScrollSource
{
    /// <summary>Raised when the minimap's viewport is dragged; the arguments are the requested world origin.</summary>
    event Action<double, double>? ViewportScrollRequested;
}
