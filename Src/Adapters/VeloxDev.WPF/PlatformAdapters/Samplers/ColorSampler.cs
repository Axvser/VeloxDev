using System.Windows.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var color1 = (Color)(start ?? Colors.Transparent);
            var color2 = (Color)(end ?? color1);
            property.SetValue(target, Color.FromArgb(
                (byte)(color1.A + (color2.A - color1.A) * t),
                (byte)(color1.R + (color2.R - color1.R) * t),
                (byte)(color1.G + (color2.G - color1.G) * t),
                (byte)(color1.B + (color2.B - color1.B) * t)));
        }
    }
}
