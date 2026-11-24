using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer <see cref="SurfaceViewportRestoreCore"/> drives: the current scroll limits, the two
/// writes that apply a restored position, and the adapter's dispatcher deferral.
/// </summary>
/// <remarks>
/// A restore must run after the platform has laid out the newly attached tree, or the extent is still zero
/// and the position is clamped away. Only the adapter knows how to wait for that point, so it supplies
/// <see cref="DeferRestore"/>; everything that decides whether and what to restore stays in Core.
/// </remarks>
/// <seealso cref="SurfaceViewportRestoreCore"/>
public interface ISurfaceViewportRestoreHost
{
    /// <summary>
    /// Whether the surface is still attached and its scroll container is resolved. When it is
    /// <see langword="false"/> the deferred restore keeps the pending position and waits for a later pass.
    /// </summary>
    bool IsRestoreEnabled { get; }

    /// <summary>Largest horizontal offset the surface reports now.</summary>
    double HorizontalMaximum { get; }

    /// <summary>Largest vertical offset the surface reports now.</summary>
    double VerticalMaximum { get; }

    /// <summary>Writes both scroll offsets.</summary>
    /// <param name="horizontalOffset">The horizontal offset to apply.</param>
    /// <param name="verticalOffset">The vertical offset to apply.</param>
    void ApplyOffset(double horizontalOffset, double verticalOffset);

    /// <summary>
    /// Schedules <paramref name="restore"/> to run once the platform has completed its layout pass for the
    /// newly attached tree.
    /// </summary>
    /// <param name="restore">The callback to run on the platform's dispatcher.</param>
    void DeferRestore(Action restore);
}
