using System;
using System.Threading;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Marks a server-side zoom transaction on a workflow surface so per-node geometry writers stand down.
///
/// During <c>OnWheelZoom</c> each <c>Layout.Scale</c> change collapses every node (Anchor/Size getters
/// divide by scale), and each node component otherwise pushes its own interop/render to the browser as a
/// separate message — a wrapper repositioned (<c>setNodePosition</c>) or a re-rendered card while the
/// canvas translate/scroll/links are still the old values. Those intermediate frames are the zoom
/// flicker (position jumps to a not-yet-correct spot and back). While this scope is active the node
/// writers skip, so the single atomic <c>applyZoomSurface</c> (translate + scroll + node geometry +
/// link points in one browser frame) is the only geometry authority for the gesture.
///
/// Uses <see cref="AsyncLocal{T}"/> (never a plain static flag) so Blazor Server circuits sharing the
/// process cannot observe each other's zoom; the node PropertyChanged handlers run synchronously inside
/// the zoom call on the same ExecutionContext. Entry is counted so nested/reentrant use is safe.
/// </summary>
public static class WorkflowGeometryScope
{
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>True while a zoom transaction is applying geometry on the current async flow.</summary>
    public static bool IsZooming => Depth.Value > 0;

    /// <summary>Enters a zoom transaction; dispose the returned handle to leave it.</summary>
    public static IDisposable Zoom()
    {
        Depth.Value++;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Depth.Value--;
        }
    }
}
