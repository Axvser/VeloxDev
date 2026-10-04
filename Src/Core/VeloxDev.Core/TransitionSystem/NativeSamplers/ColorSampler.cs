using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples a <see cref="Color"/>.</summary>
    public class ColorSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var c1 = (Color)(start ?? default(Color));
            var c2 = (Color)(end ?? c1);

            // RGB 共用一个进度，超调才不会偏色；alpha 走自己的区间。
            var rgb = new BoundedProgress(t, 0d, 255d);
            rgb.Add(c1.R, c2.R);
            rgb.Add(c1.G, c2.G);
            rgb.Add(c1.B, c2.B);

            property.SetValue(target, Color.FromArgb(
                Channel(c1.A + (c2.A - c1.A) * t),
                Channel(rgb.At(c1.R, c2.R)),
                Channel(rgb.At(c1.G, c2.G)),
                Channel(rgb.At(c1.B, c2.B))
            ));
        }

        // 饱和截断而不是回绕——直接转 byte 会把 300 变成 44。
        private static byte Channel(double value)
        {
            if (value <= 0d) return 0;
            if (value >= 255d) return 255;
            return (byte)value;
        }
    }
}
