using System.Drawing;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class RectangleFInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var r1 = (RectangleF)(start ?? default(RectangleF));
            var r2 = (RectangleF)(end ?? r1);
            if (steps == 1) return [r2];

            List<object?> result = new(steps);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(new RectangleF(
                    r1.X + deltaX * t,
                    r1.Y + deltaY * t,
                    r1.Width + deltaWidth * t,
                    r1.Height + deltaHeight * t
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
