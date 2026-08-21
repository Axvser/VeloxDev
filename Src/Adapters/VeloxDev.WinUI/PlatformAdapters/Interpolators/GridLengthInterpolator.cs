using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class GridLengthInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);

            if (steps == 1) return [g2];

            List<object?> result = new(steps);

            // Handle GridLength values of different types.
            if (g1.GridUnitType != g2.GridUnitType)
            {
                // If the unit types differ, switch directly to the target value.
                for (int i = 0; i < steps; i++)
                {
                    result.Add(i == steps - 1 ? g2 : g1);
                }
            }
            else
            {
                // With the same unit type, interpolate.
                var delta = g2.Value - g1.Value;

                for (int i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var value = g1.Value + delta * t;
                    result.Add(new GridLength(value, g1.GridUnitType));
                }
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
