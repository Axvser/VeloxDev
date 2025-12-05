using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class SizeInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);

            if (steps == 1) return [s2];

            List<object?> result = new(steps);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new Size(
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
