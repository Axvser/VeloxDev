using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    public class PointFInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0) return [];

            var p1 = (PointF)(start ?? default(PointF));
            var p2 = (PointF)(end ?? p1);
            if (steps == 1) return [p2];

            List<object?> result = new(steps);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)i / (steps - 1);
                result.Add(new PointF(
                    p1.X + deltaX * t,
                    p1.Y + deltaY * t
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
