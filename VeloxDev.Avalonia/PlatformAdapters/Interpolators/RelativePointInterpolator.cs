using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class RelativePointInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var p1 = (RelativePoint)(start ?? RelativePoint.TopLeft);
            var p2 = (RelativePoint)(end ?? p1);
            if (steps == 1) return [p2];

            List<object?> result = new(steps);

            // 如果单位不同，无法插值，直接使用目标值
            if (p1.Unit != p2.Unit)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(i == steps - 1 ? p2 : p1);
                }
                return result;
            }

            var deltaX = p2.Point.X - p1.Point.X;
            var deltaY = p2.Point.Y - p1.Point.Y;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new RelativePoint(
                    p1.Point.X + deltaX * t,
                    p1.Point.Y + deltaY * t,
                    p1.Unit
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
