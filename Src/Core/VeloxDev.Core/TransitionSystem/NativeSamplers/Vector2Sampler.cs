using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    /// <summary>Samples a <see cref="Vector2"/>.</summary>
    public class Vector2Sampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var v1 = (Vector2)(start ?? default(Vector2));
            var v2 = (Vector2)(end ?? v1);
            property.SetValue(target, v1 + (v2 - v1) * (float)t);
        }
    }
#endif
}
