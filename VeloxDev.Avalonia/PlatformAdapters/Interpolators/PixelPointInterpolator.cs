using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class PixelPointInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var p1 = (PixelPoint)(start ?? default(PixelPoint));
            var p2 = (PixelPoint)(end ?? p1);
            if (steps == 1) return [p2];

            List<object?> result = new(steps);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new PixelPoint(
                    p1.X + (int)(deltaX * t),
                    p1.Y + (int)(deltaY * t)
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
