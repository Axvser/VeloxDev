using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class GridLengthInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);
            if (steps == 1) return [g2];

            List<object?> result = new(steps);

            // 如果网格单位不同，无法插值
            if (g1.GridUnitType != g2.GridUnitType)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(i == steps - 1 ? g2 : g1);
                }
                return result;
            }

            var delta = g2.Value - g1.Value;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new GridLength(
                    Math.Max(0, g1.Value + delta * t),
                    g1.GridUnitType
                ));
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
