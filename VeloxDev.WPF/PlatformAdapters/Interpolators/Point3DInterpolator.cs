using System.Windows.Media.Media3D;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class Point3DInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var point1 = (Point3D)(start ?? new Point3D(0, 0, 0));
            var point2 = (Point3D)(end ?? point1);

            if (steps <= 0) return [];
            if (steps == 1) return [point2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var x = point1.X + t * (point2.X - point1.X);
                var y = point1.Y + t * (point2.Y - point1.Y);
                var z = point1.Z + t * (point2.Z - point1.Z);
                result.Add(new Point3D(x, y, z));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
