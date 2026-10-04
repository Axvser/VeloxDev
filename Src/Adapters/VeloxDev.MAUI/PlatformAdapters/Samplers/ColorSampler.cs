namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            // null 时给默认值。
            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? Colors.Transparent);

            // R/G/B 共用同一进度，越界时不会偏色；alpha 走自己的范围。MAUI 通道是 float，范围是 [0,1] 而非 [0,255]。
            var rgb = new BoundedProgress(t, 0f, 1f);
            rgb.Add(c1.Red, c2.Red);
            rgb.Add(c1.Green, c2.Green);
            rgb.Add(c1.Blue, c2.Blue);

            property.SetValue(target, new Color(
                (float)Channel(rgb.At(c1.Red, c2.Red)),
                (float)Channel(rgb.At(c1.Green, c2.Green)),
                (float)Channel(rgb.At(c1.Blue, c2.Blue)),
                (float)Channel(c1.Alpha + (c2.Alpha - c1.Alpha) * t)
            ));
        }

        /// <summary>Saturates instead of letting an out-of-gamut channel through — MAUI's colour is float-based.</summary>
        private static double Channel(double value)
        {
            if (value <= 0d) return 0d;
            if (value >= 1d) return 1d;
            return value;
        }
    }
}
