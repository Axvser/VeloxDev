using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class SizeInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var size1 = (Size)(start ?? new Size(0, 0));
            var size2 = (Size)(end ?? size1);

            if (steps <= 0) return [];
            if (steps == 1) return [size2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var width = size1.Width + t * (size2.Width - size1.Width);
                var height = size1.Height + t * (size2.Height - size1.Height);
                result.Add(new Size(width, height));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
