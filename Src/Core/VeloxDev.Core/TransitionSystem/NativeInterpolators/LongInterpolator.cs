namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    public class LongInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0)
                return [];

            var l1 = (long)(start ?? 0L);
            var l2 = (long)(end ?? l1);

            if (steps == 1)
                return [l2];

            List<object?> result = new(steps);

            // Handle the boundary case where start and end values are equal
            if (l1 == l2)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(l1);
                }
                return result;
            }

            // Use decimal for intermediate calculations to avoid overflow
            var delta = (decimal)l2 - (decimal)l1;

            for (int i = 0; i < steps; i++)
            {
                var t = (decimal)i / (steps - 1);
                var intermediateValue = (decimal)l1 + t * delta;
                var value = (long)intermediateValue;
                result.Add(value);
            }

            // Ensure the first and last frames are exact
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
