using Jalium.UI;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class ThicknessInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var t1 = (Thickness)(start ?? new Thickness(0));
            var t2 = (Thickness)(end ?? t1);
            if (steps <= 0) return [];
            if (steps == 1) return [t2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double p = (double)(i + 1) / steps;
                result.Add(new Thickness(
                    t1.Left + (t2.Left - t1.Left) * p,
                    t1.Top + (t2.Top - t1.Top) * p,
                    t1.Right + (t2.Right - t1.Right) * p,
                    t1.Bottom + (t2.Bottom - t1.Bottom) * p));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
