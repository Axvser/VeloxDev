namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    using VeloxDev.TransitionSystem;

    public class DoubleInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0)
                return [];

            var d1 = (double)(start ?? 0d);
            var d2 = (double)(end ?? d1);

            if (steps == 1)
                return [d2];

            List<object?> result = new(steps);

            if (options is RotationDirection direction && direction != RotationDirection.Auto)
            {
                var delta = (d2 - d1) % 360d;
                if (direction.HasFlag(RotationDirection.CounterClockWise) && delta > 0d)
                    delta -= 360d;
                else if (direction.HasFlag(RotationDirection.ClockWise) && delta < 0d)
                    delta += 360d;

                for (int i = 0; i < steps; i++)
                {
                    var t = (double)i / (steps - 1);
                    result.Add(d1 + delta * t);
                }
            }
            else
            {
                var delta = d2 - d1;
                for (int i = 0; i < steps; i++)
                {
                    var t = (double)i / (steps - 1);
                    result.Add(d1 + t * delta);
                }
            }

            result[0] = d1;
            result[steps - 1] = d2;

            return result;
        }
    }
}
