using Jalium.UI;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class RectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var r1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var r2 = (Rect)(end ?? r1);
            if (steps <= 0) return [];
            if (steps == 1) return [r2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double p = (double)i / (steps - 1);
                // Convex lerp of two valid rects stays non-negative (Jalium's Rect ctor throws on negatives).
                result.Add(new Rect(
                    r1.X + (r2.X - r1.X) * p,
                    r1.Y + (r2.Y - r1.Y) * p,
                    r1.Width + (r2.Width - r1.Width) * p,
                    r1.Height + (r2.Height - r1.Height) * p));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
