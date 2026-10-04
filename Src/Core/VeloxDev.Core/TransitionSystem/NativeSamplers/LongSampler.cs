namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples a <see cref="long"/>.</summary>
    public class LongSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var l1 = (long)(start ?? 0L);
            var l2 = (long)(end ?? l1);
            if (l1 == l2) { property.SetValue(target, l1); return; }

            // 中间计算用 decimal，避免溢出。
            var delta = (decimal)l2 - (decimal)l1;
            var intermediateValue = (decimal)l1 + (decimal)t * delta;
            property.SetValue(target, (long)intermediateValue);
        }
    }
}
