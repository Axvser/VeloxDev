using System.Drawing.Drawing2D;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Interpolates the six affine components of a <see cref="Matrix"/>.</summary>
    /// <remarks>
    /// Each frame builds a new <see cref="Matrix"/>: the dependency-property store compares values by reference for
    /// a reference type, so mutating one instance in place would change every stored layer without notifying the
    /// element. A non-matrix endpoint falls back to the identity.
    /// </remarks>
    public class MatrixSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;

        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var from = start is Matrix startMatrix ? startMatrix : new Matrix();
            var to = end is Matrix endMatrix ? endMatrix : from;

            var a = from.Elements;
            var b = to.Elements;
            property.SetValue(target, new Matrix(
                Lerp(a[0], b[0], t),
                Lerp(a[1], b[1], t),
                Lerp(a[2], b[2], t),
                Lerp(a[3], b[3], t),
                Lerp(a[4], b[4], t),
                Lerp(a[5], b[5], t)));
        }

        private static float Lerp(float start, float end, double t)
            => (float)(start + ((end - start) * t));
    }
}
