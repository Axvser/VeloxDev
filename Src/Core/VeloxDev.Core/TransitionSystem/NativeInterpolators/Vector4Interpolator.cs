using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeInterpolators
{
#if NETCOREAPP || NETFRAMEWORK || NET
    public class Vector4Interpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0) return [];

            var v1 = (Vector4)(start ?? default(Vector4));
            var v2 = (Vector4)(end ?? v1);
            if (steps == 1) return [v2];

            List<object?> result = new(steps);
            var delta = v2 - v1;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)i / (steps - 1);
                result.Add(v1 + delta * t);
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
#endif
}
