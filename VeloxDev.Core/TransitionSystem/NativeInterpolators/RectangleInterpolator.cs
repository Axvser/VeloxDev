using System.Drawing;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class RectangleInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var r1 = (Rectangle)(start ?? default(Rectangle));
            var r2 = (Rectangle)(end ?? r1);
            if (steps == 1) return [r2];

            List<object?> result = new(steps);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new Rectangle(
                    r1.X + (int)(deltaX * t),
                    r1.Y + (int)(deltaY * t),
                    r1.Width + (int)(deltaWidth * t),
                    r1.Height + (int)(deltaHeight * t)
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
