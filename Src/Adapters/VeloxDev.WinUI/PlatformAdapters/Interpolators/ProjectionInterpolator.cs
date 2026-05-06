using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class ProjectionInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            var s = Normalize(start);
            var e = Normalize(end);

            if (steps <= 1) return [e];

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);

                result.Add(new PlaneProjection
                {
                    RotationX = LerpAngle(s.RotationX, e.RotationX, t, direction, axis: 'X'),
                    RotationY = LerpAngle(s.RotationY, e.RotationY, t, direction, axis: 'Y'),
                    RotationZ = LerpAngle(s.RotationZ, e.RotationZ, t, direction, axis: 'Z'),

                    CenterOfRotationX = Lerp(s.CenterOfRotationX, e.CenterOfRotationX, t),
                    CenterOfRotationY = Lerp(s.CenterOfRotationY, e.CenterOfRotationY, t),
                    CenterOfRotationZ = Lerp(s.CenterOfRotationZ, e.CenterOfRotationZ, t),

                    GlobalOffsetX = Lerp(s.GlobalOffsetX, e.GlobalOffsetX, t),
                    GlobalOffsetY = Lerp(s.GlobalOffsetY, e.GlobalOffsetY, t),
                    GlobalOffsetZ = Lerp(s.GlobalOffsetZ, e.GlobalOffsetZ, t)
                });
            }

            result[0] = s;
            result[^1] = e;
            return result;
        }

        protected virtual double LerpAngle(double start, double end, double t, RotationDirection direction, char axis)
        {
            bool reverse = axis switch
            {
                'X' => direction.HasFlag(RotationDirection.CounterClockWiseX),
                'Y' => direction.HasFlag(RotationDirection.CounterClockWiseY),
                'Z' => direction.HasFlag(RotationDirection.CounterClockWiseZ),
                _ => false
            } || direction.HasFlag(RotationDirection.CounterClockWise);

            bool forceClockWise = axis switch
            {
                'X' => direction.HasFlag(RotationDirection.ClockWiseX),
                'Y' => direction.HasFlag(RotationDirection.ClockWiseY),
                'Z' => direction.HasFlag(RotationDirection.ClockWiseZ),
                _ => false
            } || direction.HasFlag(RotationDirection.ClockWise);

            if (reverse)
                return LerpDirectionalAngle(start, end, t, reverse: true);
            if (forceClockWise)
                return LerpDirectionalAngle(start, end, t, reverse: false);
            return Lerp(start, end, t);
        }

        private static PlaneProjection Normalize(object? obj)
        {
            if (obj is PlaneProjection p)
                return p;

            // 默认初始状态：无旋转、无偏移
            return new PlaneProjection
            {
                RotationX = 0,
                RotationY = 0,
                RotationZ = 0,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5,
                CenterOfRotationZ = 0,
                GlobalOffsetX = 0,
                GlobalOffsetY = 0,
                GlobalOffsetZ = 0
            };
        }

        protected static double LerpDirectionalAngle(double start, double end, double t, bool reverse)
        {
            var delta = (end - start) % 360d;
            if (reverse)
            {
                if (delta > 0d)
                {
                    delta -= 360d;
                }
            }
            else if (delta < 0d)
            {
                delta += 360d;
            }

            return start + delta * t;
        }
    }
}
