namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class DoubleSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var d1 = (double)(start ?? 0d);
            var d2 = (double)(end ?? d1);

            double value;
            if (options is RotationDirection direction && direction != RotationDirection.Auto)
            {
                var delta = (d2 - d1) % 360d;
                if (direction.HasFlag(RotationDirection.CounterClockWise) && delta > 0d) delta -= 360d;
                else if (direction.HasFlag(RotationDirection.ClockWise) && delta < 0d) delta += 360d;
                value = d1 + delta * t;
            }
            else
            {
                value = d1 + (d2 - d1) * t;
            }
            property.SetValue(target, value);
        }
    }
}
