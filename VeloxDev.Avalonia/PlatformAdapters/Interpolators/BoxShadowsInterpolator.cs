using Avalonia.Media;
using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class BoxShadowsInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var shadows1 = start as BoxShadows? ?? default;
            var shadows2 = end as BoxShadows? ?? shadows1;

            if (steps == 1) return [shadows2];

            List<object?> result = new(steps);

            if (shadows1.Count == 0 && shadows2.Count == 0)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(default(BoxShadows));
                }
                return result;
            }

            var count = Math.Max(shadows1.Count, shadows2.Count);

            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var interpolatedShadows = new List<BoxShadow>();

                for (int j = 0; j < count; j++)
                {
                    var shadow1 = j < shadows1.Count ? shadows1[j] : default;
                    var shadow2 = j < shadows2.Count ? shadows2[j] : default;

                    interpolatedShadows.Add(InterpolateBoxShadow(shadow1, shadow2, t));
                }

                BoxShadows resultShadows;
                if (interpolatedShadows.Count == 0)
                {
                    resultShadows = default;
                }
                else if (interpolatedShadows.Count == 1)
                {
                    resultShadows = new BoxShadows(interpolatedShadows[0]);
                }
                else
                {
                    var first = interpolatedShadows[0];
                    var rest = interpolatedShadows.GetRange(1, interpolatedShadows.Count - 1).ToArray();
                    resultShadows = new BoxShadows(first, rest);
                }

                result.Add(resultShadows);
            }

            result[0] = start ?? default(BoxShadows);
            result[steps - 1] = end ?? default(BoxShadows);

            return result;
        }

        private static BoxShadow InterpolateBoxShadow(BoxShadow s1, BoxShadow s2, double t)
        {
            if (s1 == default && s2 == default)
                return default;

            if (s1 == default)
                return s2;

            if (s2 == default)
                return s1;

            return new BoxShadow
            {
                OffsetX = s1.OffsetX + (s2.OffsetX - s1.OffsetX) * t,
                OffsetY = s1.OffsetY + (s2.OffsetY - s1.OffsetY) * t,
                Blur = s1.Blur + (s2.Blur - s1.Blur) * t,
                Spread = s1.Spread + (s2.Spread - s1.Spread) * t,
                Color = InterpolateColor(s1.Color, s2.Color, t),
                IsInset = t < 0.5 ? s1.IsInset : s2.IsInset
            };
        }

        private static Color InterpolateColor(Color c1, Color c2, double t)
        {
            if (c1 == default) c1 = Colors.Transparent;
            if (c2 == default) c2 = Colors.Transparent;

#if NETSTANDARD
            return Color.FromArgb(
                Clamp((byte)(c1.A + (c2.A - c1.A) * t), 0, 255),
                Clamp((byte)(c1.R + (c2.R - c1.R) * t), 0, 255),
                Clamp((byte)(c1.G + (c2.G - c1.G) * t), 0, 255),
                Clamp((byte)(c1.B + (c2.B - c1.B) * t), 0, 255)
            );
#else
            return Color.FromArgb(
                (byte)Math.Clamp(c1.A + (c2.A - c1.A) * t, 0, 255),
                (byte)Math.Clamp(c1.R + (c2.R - c1.R) * t, 0, 255),
                (byte)Math.Clamp(c1.G + (c2.G - c1.G) * t, 0, 255),
                (byte)Math.Clamp(c1.B + (c2.B - c1.B) * t, 0, 255)
            );
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