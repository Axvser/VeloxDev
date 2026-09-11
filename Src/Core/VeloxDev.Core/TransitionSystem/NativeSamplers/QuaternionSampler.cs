using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    public class QuaternionSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            // Exact endpoints, not a range. The pipeline drives the last frame of every pass with exactly 1
            // (or 0 on a reverse pass), and the caller's own instance has to survive to the end: a nested path
            // such as ((TranslateTransform)x.RenderTransform).X depends on the runtime type it was declared
            // with, which the interpolated scratch would replace. An overshoot past the endpoint falls through.
            if (t == 0d) { property.SetValue(target, start); return; }
            if (t == 1d) { property.SetValue(target, end); return; }

            var q1 = (Quaternion)(start ?? Quaternion.Identity);
            var q2 = (Quaternion)(end ?? q1);
            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            property.SetValue(target, SlerpDirectional(q1, q2, (float)t, direction));
        }

        private static Quaternion SlerpDirectional(Quaternion q1, Quaternion q2, float t, RotationDirection direction)
        {
            if (direction == RotationDirection.Auto)
                return Quaternion.Slerp(q1, q2, t);

            var dot = q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z + q1.W * q2.W;

            // ClockWise => dot should be positive (shortest positive rotation); force if needed
            // CounterClockWise => dot should be negative (negate q2 to go the long way)
            bool wantNegate = direction.HasFlag(RotationDirection.CounterClockWise) && dot > 0f
                           || direction.HasFlag(RotationDirection.ClockWise) && dot < 0f;

            if (wantNegate)
                q2 = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);

            return Quaternion.Slerp(q1, q2, t);
        }
    }
#endif
}
