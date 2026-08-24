using Jalium.UI;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class SizeInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);
            if (steps <= 0) return [];
            if (steps == 1) return [s2];

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double p = (double)(i + 1) / steps;
                // Convex lerp of two valid sizes stays non-negative (Jalium's Size ctor throws on negatives).
                result.Add(new Size(
                    s1.Width + deltaWidth * p,
                    s1.Height + deltaHeight * p));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
