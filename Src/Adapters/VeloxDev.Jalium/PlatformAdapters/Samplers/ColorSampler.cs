using Jalium.UI.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var color1 = (Color)(start ?? Color.Transparent);
            var color2 = (Color)(end ?? color1);

            // RGB share one progress so an overshoot cannot shift the hue; alpha is its own range.
            var rgb = new BoundedProgress(t, 0d, 255d);
            rgb.Add(color1.R, color2.R);
            rgb.Add(color1.G, color2.G);
            rgb.Add(color1.B, color2.B);

            property.SetValue(target, Color.FromArgb(
                Channel(color1.A + (color2.A - color1.A) * t),
                Channel(rgb.At(color1.R, color2.R)),
                Channel(rgb.At(color1.G, color2.G)),
                Channel(rgb.At(color1.B, color2.B))));
        }

        /// <summary>Saturates instead of wrapping — a bare byte cast turns 300 into 44.</summary>
        private static byte Channel(double value)
        {
            if (value <= 0d) return 0;
            if (value >= 255d) return 255;
            return (byte)value;
        }
    }
}
