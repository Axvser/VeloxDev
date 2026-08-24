using Jalium.UI.Media;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class ColorInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var color1 = (Color)(start ?? Color.Transparent);
            var color2 = (Color)(end ?? color1);

            if (steps <= 0) return [];
            if (steps == 1) return [color2];

            var deltaA = color2.A - color1.A;
            var deltaR = color2.R - color1.R;
            var deltaG = color2.G - color1.G;
            var deltaB = color2.B - color1.B;

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(Color.FromArgb(
                    (byte)Math.Clamp(color1.A + deltaA * t, 0, 255),
                    (byte)Math.Clamp(color1.R + deltaR * t, 0, 255),
                    (byte)Math.Clamp(color1.G + deltaG * t, 0, 255),
                    (byte)Math.Clamp(color1.B + deltaB * t, 0, 255)));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
