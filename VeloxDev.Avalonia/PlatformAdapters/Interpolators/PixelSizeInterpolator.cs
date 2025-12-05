using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class PixelSizeInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s1 = (PixelSize)(start ?? default(PixelSize));
            var s2 = (PixelSize)(end ?? s1);
            if (steps == 1) return [s2];

            List<object?> result = new(steps);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new PixelSize(
                    Math.Max(0, s1.Width + (int)(deltaWidth * t)),
                    Math.Max(0, s1.Height + (int)(deltaHeight * t))
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
