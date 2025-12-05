using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class PointFInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            // 处理空值，提供默认值
            var p1 = (PointF)(start ?? new PointF());
            var p2 = (PointF)(end ?? new PointF());

            if (steps <= 0) return [];
            if (steps == 1) return [p2];

            List<object?> result = new(steps);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(new PointF(
                    p1.X + deltaX * t,
                    p1.Y + deltaY * t
                ));
            }

            result[0] = start ?? p1;
            result[steps - 1] = end ?? p2;
            return result;
        }
    }
}
