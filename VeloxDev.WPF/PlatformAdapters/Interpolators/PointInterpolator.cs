using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class PointInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var point1 = (Point)(start ?? new Point(0, 0));
            var point2 = (Point)(end ?? point1);
            if (steps == 1)
            {
                return [point2];
            }

            List<object?> result = new(steps);

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var x = point1.X + t * (point2.X - point1.X);
                var y = point1.Y + t * (point2.Y - point1.Y);
                result.Add(new Point(x, y));
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
