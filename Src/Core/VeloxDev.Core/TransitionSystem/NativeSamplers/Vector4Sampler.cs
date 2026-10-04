using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    /// <summary>Samples a <see cref="Vector4"/>.</summary>
    public class Vector4Sampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var v1 = (Vector4)(start ?? default(Vector4));
            var v2 = (Vector4)(end ?? v1);
            property.SetValue(target, v1 + (v2 - v1) * (float)t);
        }
    }
#endif
}
