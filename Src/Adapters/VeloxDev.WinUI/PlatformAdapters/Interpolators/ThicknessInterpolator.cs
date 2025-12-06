using Microsoft.UI.Xaml;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class ThicknessInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var t1 = start is Thickness s ? s : new(0);
            var t2 = end is Thickness e ? e : t1;

            if (steps <= 1) return [t2];

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (i + 1d) / steps;
                result.Add(new Thickness(
                    Lerp(t1.Left, t2.Left, t),
                    Lerp(t1.Top, t2.Top, t),
                    Lerp(t1.Right, t2.Right, t),
                    Lerp(t1.Bottom, t2.Bottom, t)));
            }

            result[0] = start;
            result[^1] = end;
            return result;
        }
    }
}
