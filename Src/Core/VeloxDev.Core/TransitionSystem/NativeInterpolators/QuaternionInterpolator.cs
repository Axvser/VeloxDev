using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeInterpolators
{
#if NETCOREAPP || NETFRAMEWORK || NET
    public class QuaternionInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0) return [];

            var q1 = (Quaternion)(start ?? Quaternion.Identity);
            var q2 = (Quaternion)(end ?? q1);
            if (steps == 1) return [q2];

            var direction = options is RotationDirection d ? d : RotationDirection.Auto;

            List<object?> result = new(steps);

            for (int i = 0; i < steps; i++)
            {
                var t = (float)i / (steps - 1);
                result.Add(SlerpDirectional(q1, q2, t, direction));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
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
