using System.Numerics;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
#if NETCOREAPP || NETFRAMEWORK || NET
    public class QuaternionInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var q1 = (Quaternion)(start ?? Quaternion.Identity);
            var q2 = (Quaternion)(end ?? q1);
            if (steps == 1) return [q2];

            List<object?> result = new(steps);

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(Quaternion.Slerp(q1, q2, t));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
#endif
}
