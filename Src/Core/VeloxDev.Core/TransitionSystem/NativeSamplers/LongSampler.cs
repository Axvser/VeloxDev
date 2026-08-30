namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class LongSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var l1 = (long)(start ?? 0L);
            var l2 = (long)(end ?? l1);
            if (l1 == l2) { property.SetValue(target, l1); return; }

            // Use decimal for intermediate calculations to avoid overflow
            var delta = (decimal)l2 - (decimal)l1;
            var intermediateValue = (decimal)l1 + (decimal)t * delta;
            property.SetValue(target, (long)intermediateValue);
        }
    }
}
