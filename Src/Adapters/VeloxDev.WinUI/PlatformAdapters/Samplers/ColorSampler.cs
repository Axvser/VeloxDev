using Microsoft.UI;
using Windows.UI;

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

            property.SetValue(target, Color.FromArgb(
                (byte)(c1.A + deltaA * (float)t),
                (byte)(c1.R + deltaR * (float)t),
                (byte)(c1.G + deltaG * (float)t),
                (byte)(c1.B + deltaB * (float)t)
            ));
        }
    }
}
