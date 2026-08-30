using Avalonia.Media;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? c1);

            var deltaA = c2.A - c1.A;
            var deltaR = c2.R - c1.R;
            var deltaG = c2.G - c1.G;
            var deltaB = c2.B - c1.B;

#if NETSTANDARD
            property.SetValue(target, Color.FromArgb(
                Clamp((byte)(c1.A + deltaA * t), 0, 255),
                Clamp((byte)(c1.R + deltaR * t), 0, 255),
                Clamp((byte)(c1.G + deltaG * t), 0, 255),
                Clamp((byte)(c1.B + deltaB * t), 0, 255)
            ));
#else
            property.SetValue(target, Color.FromArgb(
                (byte)Math.Clamp(c1.A + deltaA * t, 0, 255),
                (byte)Math.Clamp(c1.R + deltaR * t, 0, 255),
                (byte)Math.Clamp(c1.G + deltaG * t, 0, 255),
                (byte)Math.Clamp(c1.B + deltaB * t, 0, 255)
            ));
#endif
        }

#if NETSTANDARD
        private static byte Clamp(byte value, byte min, byte max)
        {
            return value < min ? min : (value > max ? max : value);
        }
#endif
    }
}