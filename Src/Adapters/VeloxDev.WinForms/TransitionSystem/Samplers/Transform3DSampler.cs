using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// Interpolates <see cref="Easel.Transform3D"/> values: the transform's own components when both sides share
    /// a kind, the composed matrix otherwise.
    /// </summary>
    /// <remarks>
    /// The algorithm lives in Easel (<see cref="Easel.Transform3DInterpolation"/>) next to the transform kinds it
    /// moves; this sampler only adapts it to the transition engine.
    /// </remarks>
    public class Transform3DSampler : ISampler
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
                Easel.Transform3DInterpolation.Interpolate(
                    start as Easel.Transform3D,
                    end as Easel.Transform3D,
                    t));
    }
}
