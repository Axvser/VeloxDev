using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeInterpolators
{
    public class SizeInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0) return [];

            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);
            if (steps == 1) return [s2];

            List<object?> result = new(steps);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(new Size(
                    s1.Width + (int)Math.Round(deltaWidth * t),
                    s1.Height + (int)Math.Round(deltaHeight * t)
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
