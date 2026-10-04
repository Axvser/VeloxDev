namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples a <see cref="float"/>.</summary>
    public class FloatSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var f1 = (float)(start ?? 0f);
            var f2 = (float)(end ?? f1);
            property.SetValue(target, f1 + (f2 - f1) * (float)t);
        }
    }
}
