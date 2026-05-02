using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    public class PointInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0) return [];

            var p1 = (Point)(start ?? default(Point));
            var p2 = (Point)(end ?? p1);
            if (steps == 1) return [p2];

            List<object?> result = new(steps);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(new Point(
                    p1.X + (int)Math.Round(deltaX * t),
                    p1.Y + (int)Math.Round(deltaY * t)
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
