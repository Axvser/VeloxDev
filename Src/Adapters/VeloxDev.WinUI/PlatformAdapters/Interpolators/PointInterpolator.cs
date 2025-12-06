using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class PointInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var p1 = start is Point s ? s : new(0, 0);
            var p2 = end is Point e ? e : p1;

            if (steps <= 1) return [p2];

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (i + 1d) / steps;
                result.Add(new Point(Lerp(p1.X, p2.X, t), Lerp(p1.Y, p2.Y, t)));
            }

            result[0] = start;
            result[^1] = end;
            return result;
        }
    }
}
