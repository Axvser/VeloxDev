using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class PixelRectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var r1 = (PixelRect)(start ?? default(PixelRect));
            var r2 = (PixelRect)(end ?? r1);
            if (steps == 1) return [r2];

            List<object?> result = new(steps);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new PixelRect(
                    r1.X + (int)(deltaX * t),
                    r1.Y + (int)(deltaY * t),
                    Math.Max(0, r1.Width + (int)(deltaWidth * t)),
                    Math.Max(0, r1.Height + (int)(deltaHeight * t))
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
