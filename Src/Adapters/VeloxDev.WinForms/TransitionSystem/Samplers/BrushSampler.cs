using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// Interpolates Easel brushes by structure: colour, stop list, gradient geometry and brush opacity.
    /// </summary>
    /// <remarks>
    /// The algorithm lives in Easel (<see cref="Easel.BrushInterpolation"/>) because the brush kinds live there;
    /// this sampler is only the transition-engine adapter. A gradient therefore keeps its shape while it moves,
    /// and a pair with no shared structure comes back as a composed cross-fade instead of a flat colour.
    /// </remarks>
    public class BrushSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;

        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(
            object target,
            ITransitionProperty property,
            ref object? working,
            object? start,
            object? end,
            object? options,
            double t)
            => property.SetValue(
                target,
                Easel.BrushInterpolation.Interpolate(start as Easel.Brush, end as Easel.Brush, t));
    }
}
