using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class RectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var rect1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var rect2 = (Rect)(end ?? rect1);

            if (steps <= 0) return [];
            if (steps == 1) return [rect2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                var x = rect1.X + t * (rect2.X - rect1.X);
                var y = rect1.Y + t * (rect2.Y - rect1.Y);
                var width = rect1.Width + t * (rect2.Width - rect1.Width);
                var height = rect1.Height + t * (rect2.Height - rect1.Height);
                result.Add(new Rect(x, y, width, height));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
