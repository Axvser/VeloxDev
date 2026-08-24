using Jalium.UI;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class CornerRadiusInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var c1 = (CornerRadius)(start ?? new CornerRadius(0));
            var c2 = (CornerRadius)(end ?? c1);
            if (steps <= 0) return [];
            if (steps == 1) return [c2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double p = (double)(i + 1) / steps;
                result.Add(new CornerRadius(
                    c1.TopLeft + (c2.TopLeft - c1.TopLeft) * p,
                    c1.TopRight + (c2.TopRight - c1.TopRight) * p,
                    c1.BottomRight + (c2.BottomRight - c1.BottomRight) * p,
                    c1.BottomLeft + (c2.BottomLeft - c1.BottomLeft) * p));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
