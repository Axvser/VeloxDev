using Jalium.UI.Media;

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

            var color1 = (Color)(start ?? Color.Transparent);
            var color2 = (Color)(end ?? color1);

            var deltaA = color2.A - color1.A;
            var deltaR = color2.R - color1.R;
            var deltaG = color2.G - color1.G;
            var deltaB = color2.B - color1.B;

            property.SetValue(target, Color.FromArgb(
                (byte)Math.Clamp(color1.A + deltaA * t, 0, 255),
                (byte)Math.Clamp(color1.R + deltaR * t, 0, 255),
                (byte)Math.Clamp(color1.G + deltaG * t, 0, 255),
                (byte)Math.Clamp(color1.B + deltaB * t, 0, 255)));
        }
    }
}
