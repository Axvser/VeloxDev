using Easel.Controls;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Samples the four sides of a <see cref="Thickness"/> independently.</summary>
    /// <remarks>
    /// Each frame builds one value type; the box handed to <see cref="ITransitionProperty.SetValue"/> is unavoidable
    /// because the dependency-property store keys on <see cref="object"/>.
    /// </remarks>
    public class ThicknessSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var from = start is Thickness startThickness ? startThickness : Thickness.Empty;
            var to = end is Thickness endThickness ? endThickness : from;

            property.SetValue(target, new Thickness(
                from.Left + (float)(t * (to.Left - from.Left)),
                from.Top + (float)(t * (to.Top - from.Top)),
                from.Right + (float)(t * (to.Right - from.Right)),
                from.Bottom + (float)(t * (to.Bottom - from.Bottom))));
        }
    }
}
