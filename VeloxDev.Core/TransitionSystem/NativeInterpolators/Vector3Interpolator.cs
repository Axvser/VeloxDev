using System.Numerics;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
#if NETCOREAPP || NETFRAMEWORK
    public class Vector3Interpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var v1 = (Vector3)(start ?? default(Vector3));
            var v2 = (Vector3)(end ?? v1);
            if (steps == 1) return [v2];

            List<object?> result = new(steps);
            var delta = v2 - v1;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(v1 + delta * t);
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
#endif
}
