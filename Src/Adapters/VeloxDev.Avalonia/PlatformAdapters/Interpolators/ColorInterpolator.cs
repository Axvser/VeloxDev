using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
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
                var t = (double)(i + 1) / steps;

#if NETSTANDARD
                result.Add(Color.FromArgb(
                    Clamp((byte)(c1.A + deltaA * t), 0, 255),
                    Clamp((byte)(c1.R + deltaR * t), 0, 255),
                    Clamp((byte)(c1.G + deltaG * t), 0, 255),
                    Clamp((byte)(c1.B + deltaB * t), 0, 255)
                ));
#else
            result.Add(Color.FromArgb(
                (byte)Math.Clamp(c1.A + deltaA * t, 0, 255),
                (byte)Math.Clamp(c1.R + deltaR * t, 0, 255),
                (byte)Math.Clamp(c1.G + deltaG * t, 0, 255),
                (byte)Math.Clamp(c1.B + deltaB * t, 0, 255)
            ));
#endif
            }
            result[0] = start;
            result[steps - 1] = end;
            return result;
        }

#if NETSTANDARD
        private static byte Clamp(byte value, byte min, byte max)
        {
            return value < min ? min : (value > max ? max : value);
        }
#endif
    }
}
