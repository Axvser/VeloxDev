using Microsoft.UI.Xaml.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ProjectionSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            var s = Normalize(start);
            var e = Normalize(end);

            // Zero per-frame allocation: reuse a scratch projection, recomputing its fields from the pristine start/end.
            if (working is not PlaneProjection wp)
            {
                wp = new PlaneProjection();
                working = wp;
            }
            wp.RotationX = LerpAngle(s.RotationX, e.RotationX, t, direction, axis: 'X');
            wp.RotationY = LerpAngle(s.RotationY, e.RotationY, t, direction, axis: 'Y');
            wp.RotationZ = LerpAngle(s.RotationZ, e.RotationZ, t, direction, axis: 'Z');

            wp.CenterOfRotationX = Lerp(s.CenterOfRotationX, e.CenterOfRotationX, t);
            wp.CenterOfRotationY = Lerp(s.CenterOfRotationY, e.CenterOfRotationY, t);
            wp.CenterOfRotationZ = Lerp(s.CenterOfRotationZ, e.CenterOfRotationZ, t);

            wp.GlobalOffsetX = Lerp(s.GlobalOffsetX, e.GlobalOffsetX, t);
            wp.GlobalOffsetY = Lerp(s.GlobalOffsetY, e.GlobalOffsetY, t);
            wp.GlobalOffsetZ = Lerp(s.GlobalOffsetZ, e.GlobalOffsetZ, t);
            property.SetValue(target, wp);
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

            // Default initial state: no rotation, no offset.
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
