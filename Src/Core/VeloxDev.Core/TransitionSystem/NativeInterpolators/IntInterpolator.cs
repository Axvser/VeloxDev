namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    public class IntInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0)
                return [];

            var i1 = (int)(start ?? 0);
            var i2 = (int)(end ?? i1);

            if (steps == 1)
                return [i2];

            List<object?> result = new(steps);

            if (i1 == i2)
            {
                for (int i = 0; i < steps; i++)
                    result.Add(i1);
                return result;
            }

            var delta = (double)i2 - i1;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add((int)Math.Round(i1 + t * delta));
            }

            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
