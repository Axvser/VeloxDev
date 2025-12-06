using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class ColorInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? c1);

            if (steps == 1) return [c2];

            List<object?> result = new(steps);
            var deltaA = c2.A - c1.A;
            var deltaR = c2.R - c1.R;
            var deltaG = c2.G - c1.G;
            var deltaB = c2.B - c1.B;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(Color.FromArgb(
                    (byte)(c1.A + deltaA * t),
                    (byte)(c1.R + deltaR * t),
                    (byte)(c1.G + deltaG * t),
                    (byte)(c1.B + deltaB * t)
                ));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
