using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    public class QuaternionSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

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
