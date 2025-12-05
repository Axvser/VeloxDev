using System.Drawing;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class PointInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var p1 = (Point)(start ?? default(Point));
            var p2 = (Point)(end ?? p1);
            if (steps == 1) return [p2];

            List<object?> result = new(steps);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new Point(
                    p1.X + (int)(deltaX * t),
                    p1.Y + (int)(deltaY * t)
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
