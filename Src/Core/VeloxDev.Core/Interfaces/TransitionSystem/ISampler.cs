namespace VeloxDev.TransitionSystem;

/// <summary>
/// Handles sampling a property for a transition. Implementations are stateless singletons registered in
/// <see cref="Abstractions.InterpolatorCore.NativeInterpolators"/> (or a per-property override).
/// <see cref="NormalizeStart"/> / <see cref="NormalizeEnd"/> produce the endpoint values written at t &lt;= 0 /
/// t &gt;= 1; <see cref="InsertFrame"/> interpolates the middle frames. Implementations must never mutate the
/// <c>start</c> / <c>end</c> values passed to <see cref="InsertFrame"/> — they are shared with the snapshot, and
/// mutating them pollutes it.
/// </summary>
public interface ISampler
{
    /// <summary>
    /// Returns the value to write at t &lt;= 0 (the normalized start). The full parameter table is provided so a
    /// sampler can customize based on the end value or options. The default is to return <paramref name="start"/>
    /// as-is; a sampler may return a copy (e.g. a clone of a mutable reference type) to keep the target from
    /// aliasing the shared start instance.
    /// </summary>
    object? NormalizeStart(object? start, object? end, object? options);

    /// <summary>
    /// Returns the value to write at t &gt;= 1 (the normalized end). The full parameter table is provided so a
    /// sampler can customize based on the start value or options. The default is to return <paramref name="end"/>
    /// as-is; a sampler may return a copy (e.g. a clone of a mutable reference type) to keep the target from
    /// aliasing the shared end instance.
    /// </summary>
    object? NormalizeEnd(object? start, object? end, object? options);

    /// <summary>
    /// Inserts the interpolated frame at time t in [0,1]: computes the value between the normalized
    /// <paramref name="start"/> and <paramref name="end"/> and writes it to <paramref name="property"/> on
    /// <paramref name="target"/>. <paramref name="working"/> is a per-animation reusable scratch object: a sampler
    /// may lazily create it on the first middle-frame call (via ref) and reuse it across frames for zero per-frame
    /// allocation; value-type samplers ignore it. Implementations must never mutate <paramref name="start"/> or
    /// <paramref name="end"/>.
    /// </summary>
    void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t);
}
