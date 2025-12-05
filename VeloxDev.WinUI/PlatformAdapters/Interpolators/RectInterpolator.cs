using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class RectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var r1 = (Rect)(start ?? default(Rect));
            var r2 = (Rect)(end ?? r1);

            if (steps == 1) return [r2];

            List<object?> result = new(steps);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(new Rect(
                    r1.X + deltaX * t,
                    r1.Y + deltaY * t,
                    r1.Width + deltaWidth * t,
                    r1.Height + deltaHeight * t
                ));
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }
    }
}
