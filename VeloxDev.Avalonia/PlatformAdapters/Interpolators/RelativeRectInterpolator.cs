using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class RelativeRectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var r1 = (RelativeRect)(start ?? new RelativeRect());
            var r2 = (RelativeRect)(end ?? r1);
            if (steps == 1) return [r2];

            List<object?> result = new(steps);

            // 如果单位不同，无法插值
            if (r1.Unit != r2.Unit)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(i == steps - 1 ? r2 : r1);
                }
                return result;
            }

            var deltaX = r2.Rect.X - r1.Rect.X;
            var deltaY = r2.Rect.Y - r1.Rect.Y;
            var deltaWidth = r2.Rect.Width - r1.Rect.Width;
            var deltaHeight = r2.Rect.Height - r1.Rect.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new RelativeRect(
                    r1.Rect.X + deltaX * t,
                    r1.Rect.Y + deltaY * t,
                    Math.Max(0, r1.Rect.Width + deltaWidth * t),
                    Math.Max(0, r1.Rect.Height + deltaHeight * t),
                    r1.Unit
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
