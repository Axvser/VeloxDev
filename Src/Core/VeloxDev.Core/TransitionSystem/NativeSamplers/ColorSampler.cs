using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class ColorSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var c1 = (Color)(start ?? default(Color));
            var c2 = (Color)(end ?? c1);
            var deltaA = c2.A - c1.A;
            var deltaR = c2.R - c1.R;
            var deltaG = c2.G - c1.G;
            var deltaB = c2.B - c1.B;

            property.SetValue(target, Color.FromArgb(
                (byte)(c1.A + deltaA * t),
                (byte)(c1.R + deltaR * t),
                (byte)(c1.G + deltaG * t),
                (byte)(c1.B + deltaB * t)
            ));
        }
    }
}
