namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples an <see cref="int"/>.</summary>
    public class IntSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var i1 = (int)(start ?? 0);
            var i2 = (int)(end ?? i1);
            if (i1 == i2) { property.SetValue(target, i1); return; }

            var delta = (double)i2 - i1;
            property.SetValue(target, (int)Math.Round(i1 + t * delta));
        }
    }
}
