using System.Windows.Media;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class ColorInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var color1 = (Color)(start ?? Colors.Transparent);
            var color2 = (Color)(end ?? color1);

            if (steps <= 0) return [];
            if (steps == 1) return [color2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var a = (byte)(color1.A + (color2.A - color1.A) * t);
                var r = (byte)(color1.R + (color2.R - color1.R) * t);
                var g = (byte)(color1.G + (color2.G - color1.G) * t);
                var b = (byte)(color1.B + (color2.B - color1.B) * t);
                result.Add(Color.FromArgb(a, r, g, b));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
