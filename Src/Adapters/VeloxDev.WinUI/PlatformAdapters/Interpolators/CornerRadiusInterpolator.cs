using Microsoft.UI.Xaml;
using System.Collections.Generic;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WinUI.NativeInterpolators
{
    public class CornerRadiusInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var c1 = start is CornerRadius s ? s : new(0);
            var c2 = end is CornerRadius e ? e : c1;

            if (steps <= 1) return [c2];

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (i + 1d) / steps;
                result.Add(new CornerRadius(
                    Lerp(c1.TopLeft, c2.TopLeft, t),
                    Lerp(c1.TopRight, c2.TopRight, t),
                    Lerp(c1.BottomRight, c2.BottomRight, t),
                    Lerp(c1.BottomLeft, c2.BottomLeft, t)));
            }

            result[0] = start;
            result[^1] = end;
            return result;
        }
    }
}
