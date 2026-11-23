using System.Drawing;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Interpolates a nullable <see cref="RectangleF"/>, the type of the render <c>Clip</c> property.</summary>
    /// <remarks>
    /// The registry matches exact types, base classes and interfaces; <see cref="Nullable{T}"/> matches none of the
    /// registered <see cref="RectangleF"/> path, so it is registered explicitly. A <see langword="null"/> endpoint
    /// normalizes to an empty rectangle for the middle frames and is written back as <see langword="null"/> at the
    /// endpoint the caller supplied.
    /// </remarks>
    public class NullableRectangleFSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;

        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var from = start is RectangleF startRect ? startRect : RectangleF.Empty;
            var to = end is RectangleF endRect ? endRect : RectangleF.Empty;
            property.SetValue(target, new RectangleF(
                Lerp(from.X, to.X, t),
                Lerp(from.Y, to.Y, t),
                Lerp(from.Width, to.Width, t),
                Lerp(from.Height, to.Height, t)));
        }

        private static float Lerp(float start, float end, double t)
            => (float)(start + ((end - start) * t));
    }
}
