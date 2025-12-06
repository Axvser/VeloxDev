using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class GridLengthInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);

            if (steps == 1) return [g2];

            List<object?> result = new(steps);

            // 处理不同类型的GridLength
            if (g1.GridUnitType != g2.GridUnitType)
            {
                // 单位类型不同，直接切换到目标值
                for (int i = 0; i < steps; i++)
                {
                    result.Add(i == steps - 1 ? g2 : g1);
                }
            }
            else
            {
                // 相同单位类型，进行插值
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
