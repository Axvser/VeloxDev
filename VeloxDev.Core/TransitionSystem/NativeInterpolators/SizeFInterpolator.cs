using System.Drawing;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class SizeFInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s1 = (SizeF)(start ?? default(SizeF));
            var s2 = (SizeF)(end ?? s1);
            if (steps == 1) return [s2];

            List<object?> result = new(steps);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(new SizeF(
                    s1.Width + deltaWidth * t,
                    s1.Height + deltaHeight * t
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
