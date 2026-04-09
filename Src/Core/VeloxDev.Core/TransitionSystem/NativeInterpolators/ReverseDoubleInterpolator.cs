using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class ReverseDoubleInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            if (steps <= 0)
                return [];

            var d1 = ToDouble(start, 0d);
            var d2 = ToDouble(end, d1);

            if (steps == 1)
                return [end ?? d2];

            List<object?> result = new(steps);
            var delta = (d2 - d1) % 360d;
            if (delta > 0d)
            {
                delta -= 360d;
            }

            for (int i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(d1 + t * delta);
            }

            result[0] = start ?? d1;
            result[steps - 1] = end ?? d2;
            return result;
        }

        private static double ToDouble(object? value, double fallback)
        {
            try
            {
                return value switch
                {
                    null => fallback,
                    double doubleValue => doubleValue,
                    float floatValue => floatValue,
                    decimal decimalValue => (double)decimalValue,
                    int intValue => intValue,
                    long longValue => longValue,
                    _ => System.Convert.ToDouble(value),
                };
            }
            catch
            {
                return fallback;
            }
        }
    }
}
