using System.Windows.Media.Media3D;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class Vector3DInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var vector1 = (Vector3D)(start ?? new Vector3D(0, 0, 0));
            var vector2 = (Vector3D)(end ?? vector1);

            if (steps <= 0) return [];
            if (steps == 1) return [vector2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var x = vector1.X + t * (vector2.X - vector1.X);
                var y = vector1.Y + t * (vector2.Y - vector1.Y);
                var z = vector1.Z + t * (vector2.Z - vector1.Z);
                result.Add(new Vector3D(x, y, z));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
