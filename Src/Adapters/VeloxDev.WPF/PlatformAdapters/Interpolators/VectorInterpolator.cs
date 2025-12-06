using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class VectorInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var vector1 = (Vector)(start ?? new Vector(0, 0));
            var vector2 = (Vector)(end ?? vector1);

            if (steps <= 0) return [];
            if (steps == 1) return [vector2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var x = vector1.X + t * (vector2.X - vector1.X);
                var y = vector1.Y + t * (vector2.Y - vector1.Y);
                result.Add(new Vector(x, y));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
